#include "model/ModelView.h"

#include <DirectXPackedVector.h>

#include "coords.h"
#include "model.h"

namespace PerkeleRim::ModelView
{
    namespace
    {
        namespace W32 = REX::W32;

        // A vanilla mesh whose first plain BSTriShape is cloned for every shape built here: the game
        // has no constructor we can call, and a clone comes out of the game's own allocator. Its data,
        // shader and texture are all replaced.
        constexpr const char* kTemplateModel = "Meshes\\Furniture\\Cart\\CartFurnStatic01.nif";

        // Frame cost is averaged over windows this long, before and after a build.
        constexpr float kFrameWindow = 5.0f;

        // Vertex layout: full precision position + bitangent x, half UV, byte normal + bitangent y,
        // byte tangent + bitangent z (how Skyrim SE stores static meshes).
        constexpr std::uint32_t kStride = 28;
        constexpr std::uint32_t kUvOffset = 16;
        constexpr std::uint32_t kNormalOffset = 20;
        constexpr std::uint32_t kTangentOffset = 24;

        // A received model: its blob (the parsed model points into it) and its textures, built once.
        struct Cached
        {
            std::vector<std::uint8_t>         blob;
            Model::Model                      model;
            std::vector<RE::NiSourceTexture*> textures;  // per model texture, null when it failed
            bool                              logged = false;  // first instance logged
        };

        struct State
        {
            Model::Assembler                                   assembler;
            bool                                               dropLogged = false;
            RE::NiPointer<RE::NiNode>                          templateModel;
            RE::BSTriShape*                                    templateShape = nullptr;
            bool                                               templateFailed = false;
            std::map<std::uint32_t, std::unique_ptr<Cached>>  models;  // by MSC's model id
            std::map<std::string, std::uint32_t, std::less<>>  byName;  // the latest model id per name
            // Textures built here are never destroyed: the game's NiSourceTexture destructor expects
            // one it loaded itself. A model sent again keeps its old textures alive here.
            std::vector<RE::NiPointer<RE::NiSourceTexture>>    textures;
            RE::NiSourceTexture*                               flatNormal = nullptr;
            RE::NiSourceTexture*                               shinyNormal = nullptr;
            // Hand-built like the textures, so never destroyed either; shared by every shape.
            RE::NiPointer<RE::NiAlphaProperty>                 cutoutAlpha[256];  // by alpha threshold
            RE::NiPointer<RE::NiAlphaProperty>                 blendAlpha;

            float frameTime = 0.0f;
            int   frames = 0;
            float lastFrameMs = 0.0f;  // average of the last full window, 0 = none yet
            bool  logNextWindow = false;
        } state;

        std::uint8_t Byte(float a_unit) { return static_cast<std::uint8_t>(std::clamp(std::lround((a_unit + 1.0f) * 127.5f), 0L, 255L)); }

        RE::NiPoint3 Point(const float (&a_v)[3]) { return { a_v[0], a_v[1], a_v[2] }; }

        bool LoadTemplate()
        {
            if (state.templateShape || state.templateFailed) {
                return state.templateShape != nullptr;
            }
            state.templateFailed = true;
            if (RE::BSModelDB::Demand(kTemplateModel, state.templateModel, {}) != RE::BSResource::ErrorCode::kNone || !state.templateModel) {
                SKSE::log::warn("model view: template {} did not load", kTemplateModel);
                return false;
            }
            RE::BSVisit::TraverseScenegraphGeometries(state.templateModel.get(), [](RE::BSGeometry* a_geometry) {
                const auto* rtti = a_geometry->GetRTTI();
                if (rtti && std::string_view(rtti->GetName()) == "BSTriShape" && !a_geometry->GetGeometryRuntimeData().skinInstance) {
                    state.templateShape = a_geometry->AsTriShape();
                    return RE::BSVisit::BSVisitControl::kStop;
                }
                return RE::BSVisit::BSVisitControl::kContinue;
            });
            if (!state.templateShape) {
                SKSE::log::warn("model view: no plain BSTriShape in {}", kTemplateModel);
                return false;
            }
            state.templateFailed = false;
            return true;
        }

        // Each mip level a 2x2 box filter of the one above.
        std::vector<std::vector<std::uint8_t>> Mips(const Model::Texture& a_texture)
        {
            std::vector<std::vector<std::uint8_t>> levels;
            levels.emplace_back(a_texture.rgba, a_texture.rgba + static_cast<std::size_t>(a_texture.width) * a_texture.height * 4);
            std::uint32_t w = a_texture.width, h = a_texture.height;
            while (w > 1 || h > 1) {
                const std::uint32_t nw = (std::max)(1u, w / 2), nh = (std::max)(1u, h / 2);
                const auto&         src = levels.back();
                std::vector<std::uint8_t> dst(static_cast<std::size_t>(nw) * nh * 4);
                for (std::uint32_t y = 0; y < nh; y++) {
                    for (std::uint32_t x = 0; x < nw; x++) {
                        const std::uint32_t x0 = x * 2, x1 = (std::min)(x0 + 1, w - 1);
                        const std::uint32_t y0 = y * 2, y1 = (std::min)(y0 + 1, h - 1);
                        for (std::uint32_t c = 0; c < 4; c++) {
                            const std::uint32_t sum = src[(y0 * w + x0) * 4 + c] + src[(y0 * w + x1) * 4 + c] +
                                                      src[(y1 * w + x0) * 4 + c] + src[(y1 * w + x1) * 4 + c];
                            dst[(y * nw + x) * 4 + c] = static_cast<std::uint8_t>((sum + 2) / 4);
                        }
                    }
                }
                levels.push_back(std::move(dst));
                w = nw;
                h = nh;
            }
            return levels;
        }

        // A GPU texture with mips, wrapped (Wrap) so the lighting shader can bind it.
        RE::NiSourceTexture* MakeTexture(const Model::Texture& a_texture, const std::string& a_name)
        {
            auto* device = RE::BSGraphics::Renderer::GetDevice();
            if (!device) {
                return nullptr;
            }
            const auto levels = Mips(a_texture);
            W32::D3D11_TEXTURE2D_DESC desc{};
            desc.width = a_texture.width;
            desc.height = a_texture.height;
            desc.mipLevels = static_cast<std::uint32_t>(levels.size());
            desc.arraySize = 1;
            desc.format = W32::DXGI_FORMAT_R8G8B8A8_UNORM;
            desc.sampleDesc = { 1, 0 };
            desc.usage = W32::D3D11_USAGE_IMMUTABLE;
            desc.bindFlags = W32::D3D11_BIND_SHADER_RESOURCE;
            std::vector<W32::D3D11_SUBRESOURCE_DATA> init;
            std::uint32_t                            w = a_texture.width;
            for (const auto& level : levels) {
                init.push_back({ level.data(), w * 4, 0 });
                w = (std::max)(1u, w / 2);
            }
            W32::ID3D11Texture2D* texture = nullptr;
            if (!W32::SUCCESS(device->CreateTexture2D(&desc, init.data(), &texture)) || !texture) {
                SKSE::log::warn("model view: CreateTexture2D {}x{} failed", a_texture.width, a_texture.height);
                return nullptr;
            }
            W32::ID3D11ShaderResourceView* view = nullptr;
            if (!W32::SUCCESS(device->CreateShaderResourceView(texture, nullptr, &view)) || !view) {
                texture->Release();
                SKSE::log::warn("model view: CreateShaderResourceView failed");
                return nullptr;
            }

            auto* source = Wrap(texture, view, a_texture.width, a_texture.height, static_cast<std::uint32_t>(levels.size()), a_name);
            state.textures.emplace_back(source);
            return source;
        }

        RE::BSGraphics::VertexDesc VertexDesc()
        {
            using V = RE::BSGraphics::Vertex;
            const std::uint64_t flags = V::VF_VERTEX | V::VF_UV | V::VF_NORMAL | V::VF_TANGENT | V::VF_FULLPREC;
            // The vertex size in the low nibble; each attribute's offset goes where the game reads it
            // (SetAttributeOffset). Hand-placed nibbles one slot too low once made Skyrim take the UVs
            // from the position bytes, which striped every texture.
            const std::uint64_t raw = (kStride / 4) | (flags << 44);
            RE::BSGraphics::VertexDesc desc;
            std::memcpy(&desc, &raw, sizeof(raw));
            desc.SetAttributeOffset(V::VA_TEXCOORD0, kUvOffset);
            desc.SetAttributeOffset(V::VA_NORMAL, kNormalOffset);
            desc.SetAttributeOffset(V::VA_BINORMAL, kTangentOffset);
            static bool logged = false;
            if (!logged) {
                logged = true;
                SKSE::log::info("model view: vertex layout uv at {}, normal at {}, tangent at {}", desc.GetAttributeOffset(V::VA_TEXCOORD0),
                    desc.GetAttributeOffset(V::VA_NORMAL), desc.GetAttributeOffset(V::VA_BINORMAL));
            }
            return desc;
        }

        // Any unit vector at right angles to a_n: with Skyrim's flat default normal map the shading
        // only uses the vertex normal, so the tangent frame just has to be valid.
        RE::NiPoint3 Tangent(const RE::NiPoint3& a_n)
        {
            const RE::NiPoint3 axis = std::abs(a_n.z) < 0.9f ? RE::NiPoint3(0, 0, 1) : RE::NiPoint3(1, 0, 0);
            auto t = axis.Cross(a_n);
            t.Unitize();
            return t;
        }

        RE::BSGraphics::TriShape* MakeBuffers(const Model::Mesh& a_mesh)
        {
            auto* device = RE::BSGraphics::Renderer::GetDevice();
            if (!device || a_mesh.vertexCount == 0 || a_mesh.indexCount == 0) {
                return nullptr;
            }
            const std::size_t vertexBytes = static_cast<std::size_t>(a_mesh.vertexCount) * kStride;
            const std::size_t indexBytes = static_cast<std::size_t>(a_mesh.indexCount) * 2;
            auto* vertices = static_cast<std::uint8_t*>(RE::malloc(vertexBytes));
            auto* indices = static_cast<std::uint16_t*>(RE::malloc(indexBytes));
            for (std::uint32_t i = 0; i < a_mesh.vertexCount; i++) {
                const auto   v = a_mesh.Vertex(i);
                auto         n = Point(v.normal);
                n.Unitize();
                const auto   t = Tangent(n);
                const auto   b = n.Cross(t);
                std::uint8_t* out = vertices + static_cast<std::size_t>(i) * kStride;
                const float  position[4] = { v.position[0], v.position[1], v.position[2], b.x };
                std::memcpy(out, position, sizeof(position));
                const std::uint16_t uv[2] = { DirectX::PackedVector::XMConvertFloatToHalf(v.uv[0]),
                    DirectX::PackedVector::XMConvertFloatToHalf(v.uv[1]) };
                std::memcpy(out + kUvOffset, uv, sizeof(uv));
                const std::uint8_t normal[4] = { Byte(n.x), Byte(n.y), Byte(n.z), Byte(b.y) };
                std::memcpy(out + kNormalOffset, normal, sizeof(normal));
                const std::uint8_t tangent[4] = { Byte(t.x), Byte(t.y), Byte(t.z), Byte(b.z) };
                std::memcpy(out + kTangentOffset, tangent, sizeof(tangent));
            }
            for (std::uint32_t i = 0; i < a_mesh.indexCount; i++) {
                indices[i] = a_mesh.Index(i);
            }

            W32::ID3D11Buffer*         vb = nullptr;
            W32::ID3D11Buffer*         ib = nullptr;
            W32::D3D11_BUFFER_DESC     vdesc{ static_cast<std::uint32_t>(vertexBytes), W32::D3D11_USAGE_IMMUTABLE, W32::D3D11_BIND_VERTEX_BUFFER, 0, 0, 0 };
            W32::D3D11_BUFFER_DESC     idesc{ static_cast<std::uint32_t>(indexBytes), W32::D3D11_USAGE_IMMUTABLE, W32::D3D11_BIND_INDEX_BUFFER, 0, 0, 0 };
            W32::D3D11_SUBRESOURCE_DATA vinit{ vertices, 0, 0 };
            W32::D3D11_SUBRESOURCE_DATA iinit{ indices, 0, 0 };
            if (!W32::SUCCESS(device->CreateBuffer(&vdesc, &vinit, &vb)) || !W32::SUCCESS(device->CreateBuffer(&idesc, &iinit, &ib))) {
                if (vb) {
                    vb->Release();
                }
                RE::free(vertices);
                RE::free(indices);
                SKSE::log::warn("model view: CreateBuffer failed for {}", a_mesh.name);
                return nullptr;
            }

            auto* data = static_cast<RE::BSGraphics::TriShape*>(RE::malloc(sizeof(RE::BSGraphics::TriShape)));
            std::memset(data, 0, sizeof(*data));
            data->vertexBuffer = vb;
            data->indexBuffer = ib;
            data->vertexDesc = VertexDesc();
            data->refCount = 1;
            data->rawVertexData = vertices;
            data->rawIndexData = indices;
            return data;
        }

        // A normal map pointing straight out with alpha 0. Skyrim reads the specular mask from the
        // normal map's alpha, and its default normal map has it at full, so everything shone white in
        // the sun (the converted van got the same fix in tools/nif/build_nif.py).
        RE::NiSourceTexture* FlatNormal()
        {
            if (!state.flatNormal) {
                constexpr std::uint32_t   kSize = 16;
                std::vector<std::uint8_t> pixels;
                for (std::uint32_t i = 0; i < kSize * kSize; i++) {
                    pixels.insert(pixels.end(), { 128, 128, 255, 0 });
                }
                state.flatNormal = MakeTexture({ kSize, kSize, pixels.data() }, "PerkeleRim\\FlatNormal");
            }
            return state.flatNormal;
        }

        // The same with full alpha, for materials that shine: the specular strength then comes from
        // the material alone.
        RE::NiSourceTexture* ShinyNormal()
        {
            if (!state.shinyNormal) {
                constexpr std::uint32_t   kSize = 16;
                std::vector<std::uint8_t> pixels;
                for (std::uint32_t i = 0; i < kSize * kSize; i++) {
                    pixels.insert(pixels.end(), { 128, 128, 255, 255 });
                }
                state.shinyNormal = MakeTexture({ kSize, kSize, pixels.data() }, "PerkeleRim\\ShinyNormal");
            }
            return state.shinyNormal;
        }

        // An NiAlphaProperty put together by hand like the textures (the game only makes them while
        // loading a file). Flags as in vanilla NIFs: blending src alpha / inverse src alpha
        // (0x00ED), or testing "greater than threshold" without blending (0x12EC).
        RE::NiAlphaProperty* MakeAlpha(std::uint16_t a_flags, std::uint8_t a_threshold)
        {
            static REL::Relocation<std::uintptr_t> vtable{ RE::NiAlphaProperty::VTABLE[0] };
            auto* alpha = static_cast<RE::NiAlphaProperty*>(RE::malloc(sizeof(RE::NiAlphaProperty)));
            std::memset(alpha, 0, sizeof(*alpha));
            *reinterpret_cast<std::uintptr_t*>(alpha) = vtable.address();
            alpha->alphaFlags = a_flags;
            alpha->alphaThreshold = a_threshold;
            return alpha;
        }

        RE::NiAlphaProperty* AlphaFor(const Protocol::ModelMaterial& a_material)
        {
            if ((a_material.flags & Protocol::kMaterialBlend) != 0) {
                if (!state.blendAlpha) {
                    state.blendAlpha.reset(MakeAlpha(0x00ED, 0));
                }
                return state.blendAlpha.get();
            }
            if ((a_material.flags & Protocol::kMaterialCutout) != 0) {
                const auto threshold = static_cast<std::uint8_t>(std::clamp(std::lround(a_material.alphaCutoff * 255.0f), 0L, 255L));
                if (!state.cutoutAlpha[threshold]) {
                    state.cutoutAlpha[threshold].reset(MakeAlpha(0x12EC, threshold));
                }
                return state.cutoutAlpha[threshold].get();
            }
            return nullptr;
        }

        // Unity's smoothness (0..1) as Skyrim's specular power (glossiness, about 4 to 200).
        float SpecularPower(float a_glossiness)
        {
            const float g = std::clamp(a_glossiness, 0.0f, 1.0f);
            return 4.0f + 196.0f * g * g;
        }

        RE::BSLightingShaderProperty* MakeShader(RE::NiSourceTexture* a_diffuse, const Protocol::ModelMaterial& a_material)
        {
            const bool  shiny = a_material.specular[0] + a_material.specular[1] + a_material.specular[2] > 0.0f;
            auto&       shaderState = *RE::BSGraphics::State::GetSingleton();
            auto*       material = RE::BSLightingShaderMaterialBase::CreateMaterial<RE::BSLightingShaderMaterial>();
            material->diffuseTexture = RE::NiPointer<RE::NiSourceTexture>(a_diffuse ? a_diffuse : shaderState.GetRuntimeData().defaultTextureWhite.get());
            auto* normal = shiny ? ShinyNormal() : FlatNormal();
            material->normalTexture = normal ? RE::NiPointer<RE::NiSourceTexture>(normal) : shaderState.GetRuntimeData().defaultTextureNormalMap;

            auto* shader = RE::BSLightingShaderProperty::Create();
            // The material is left alive: destroying it here crashed the game, and it is a few bytes
            // per mesh.
            shader->SetMaterial(material, true);
            // SetMaterial copies everything but the textures (they come from a texture set, which has
            // none here), so they go on the shader's own copy.
            auto* own = static_cast<RE::BSLightingShaderMaterialBase*>(shader->material);
            own->diffuseTexture = material->diffuseTexture;
            own->normalTexture = material->normalTexture;
            own->materialAlpha = 1.0f;
            own->specularColorScale = shiny ? 1.0f : 0.0f;
            own->specularColor = RE::NiColor(a_material.specular[0], a_material.specular[1], a_material.specular[2]);
            own->specularPower = SpecularPower(a_material.glossiness);

            using F = RE::BSShaderProperty::EShaderPropertyFlag;
            const bool unlit = (a_material.flags & Protocol::kMaterialUnlit) != 0;
            const bool glows = a_material.emissive[0] + a_material.emissive[1] + a_material.emissive[2] > 0.0f;
            if (unlit || glows) {
                // Lit by its own glow: white for unlit (the texture as is, give or take the scene's
                // light on top), else the material's emissive color.
                auto* color = static_cast<RE::NiColor*>(RE::malloc(sizeof(RE::NiColor)));
                *color = unlit ? RE::NiColor(1.0f, 1.0f, 1.0f) : RE::NiColor(a_material.emissive[0], a_material.emissive[1], a_material.emissive[2]);
                shader->emissiveColor = color;
                shader->emissiveMult = 1.0f;
                shader->flags.set(F::kOwnEmit);
            }
            if (shiny) {
                shader->flags.set(F::kSpecular);
            }
            // No kCastShadows: thin hand-built shapes shadowed themselves in flickering streaks.
            if ((a_material.flags & Protocol::kMaterialOverlay) != 0) {
                // Hung just in front of the camera, so the depth test only hides it behind what is
                // closer still; it writes no depth and takes no shadows.
                shader->flags.set(F::kZBufferTest);
                return shader;
            }
            if (!unlit) {
                shader->flags.set(F::kReceiveShadows);
            }
            shader->flags.set(F::kZBufferTest);
            if ((a_material.flags & Protocol::kMaterialBlend) == 0) {
                shader->flags.set(F::kZBufferWrite);
            } else {
                // Glass and smoke don't hide what's behind them. Cleared explicitly: a new shader
                // can start with it set, and smoke squares then hid each other and the fog behind
                // them (dark, hard-edged squares).
                shader->flags.reset(F::kZBufferWrite);
            }
            return shader;
        }

        // Opaque, lit, no highlight: meshes without a material.
        constexpr Protocol::ModelMaterial kDefaultMaterial{};

        RE::NiPointer<RE::BSTriShape> MakeShape(const Model::Mesh& a_mesh, RE::NiSourceTexture* a_diffuse, const Protocol::ModelMaterial& a_material)
        {
            auto* data = MakeBuffers(a_mesh);
            if (!data) {
                return nullptr;
            }
            RE::NiPointer<RE::BSTriShape> shape(static_cast<RE::BSTriShape*>(state.templateShape->Clone()));
            shape->name = a_mesh.name.c_str();
            shape->local = RE::NiTransform();

            // The clone still points at the template's buffers; that reference is simply dropped
            // (the template stays loaded anyway).
            auto& geometry = shape->GetGeometryRuntimeData();
            geometry.rendererData = data;
            geometry.vertexDesc = data->vertexDesc;
            geometry.skinInstance.reset();
            geometry.alphaProperty.reset(AlphaFor(a_material));
            auto& counts = shape->GetTrishapeRuntimeData();
            counts.vertexCount = static_cast<std::uint16_t>(a_mesh.vertexCount);
            counts.triangleCount = static_cast<std::uint16_t>(a_mesh.indexCount / 3);

            RE::NiPoint3 lo(FLT_MAX, FLT_MAX, FLT_MAX), hi(-FLT_MAX, -FLT_MAX, -FLT_MAX);
            for (std::uint32_t i = 0; i < a_mesh.vertexCount; i++) {
                const auto p = Point(a_mesh.Vertex(i).position);
                lo = { (std::min)(lo.x, p.x), (std::min)(lo.y, p.y), (std::min)(lo.z, p.z) };
                hi = { (std::max)(hi.x, p.x), (std::max)(hi.y, p.y), (std::max)(hi.z, p.z) };
            }
            auto& bound = shape->GetModelData().modelBound;
            bound.center = (lo + hi) * 0.5f;
            bound.radius = (hi - lo).Length() * 0.5f;

            auto* shader = MakeShader(a_diffuse, a_material);
            geometry.shaderProperty.reset(shader);
            shader->SetupGeometry(shape.get());
            shader->FinishSetupGeometry(shape.get());
            return shape;
        }

        void MarkBuild()
        {
            if (state.lastFrameMs > 0.0f && !state.logNextWindow) {
                SKSE::log::info("model view: frame before the build {:.2f} ms", state.lastFrameMs);
            }
            state.frameTime = 0.0f;
            state.frames = 0;
            state.logNextWindow = true;
        }

        void Store(std::uint32_t a_id, const std::vector<std::uint8_t>& a_blob)
        {
            const auto start = std::chrono::steady_clock::now();
            auto       cached = std::make_unique<Cached>();
            cached->blob = a_blob;
            auto model = Model::Parse(cached->blob.data(), cached->blob.size());
            if (!model) {
                SKSE::log::warn("model view: model {:08X} is malformed ({} bytes)", a_id, a_blob.size());
                return;
            }
            cached->model = std::move(*model);
            for (std::size_t i = 0; i < cached->model.textures.size(); i++) {
                cached->textures.push_back(MakeTexture(cached->model.textures[i], std::format("PerkeleRim\\{:08X}\\{}", a_id, i)));
            }
            const auto ms = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - start).count();
            SKSE::log::info("model view: {:08X} {} received ({} bytes, {} meshes, {} textures built in {:.1f} ms)", a_id,
                cached->model.name, a_blob.size(), cached->model.meshes.size(), cached->textures.size(), ms);
            state.byName[cached->model.name] = a_id;
            state.models[a_id] = std::move(cached);
            MarkBuild();
        }
    }

    RE::NiSourceTexture* Wrap(REX::W32::ID3D11Texture2D* a_texture, REX::W32::ID3D11ShaderResourceView* a_view, std::uint32_t a_width,
        std::uint32_t a_height, std::uint32_t a_mips, const std::string& a_name)
    {
        auto* renderer = static_cast<RE::BSGraphics::Texture*>(RE::malloc(sizeof(RE::BSGraphics::Texture)));
        std::memset(renderer, 0, sizeof(*renderer));
        renderer->texture = a_texture;
        renderer->resourceView = a_view;
        renderer->width = static_cast<std::uint16_t>(a_width);
        renderer->height = static_cast<std::uint16_t>(a_height);
        renderer->mips = static_cast<std::uint8_t>(a_mips);
        renderer->refCount = 1;

        static REL::Relocation<std::uintptr_t> vtable{ RE::NiSourceTexture::VTABLE[0] };
        auto* source = static_cast<RE::NiSourceTexture*>(RE::malloc(sizeof(RE::NiSourceTexture)));
        std::memset(source, 0, sizeof(*source));
        *reinterpret_cast<std::uintptr_t*>(source) = vtable.address();
        source->name = a_name.c_str();
        source->rendererTexture = renderer;
        return source;
    }

    void OnChunk(const std::uint8_t* a_payload, std::uint32_t a_size)
    {
        switch (state.assembler.Add(a_payload, a_size)) {
        case Model::Assembler::Result::Partial:
            state.dropLogged = false;
            return;
        case Model::Assembler::Result::Dropped:
            if (!state.dropLogged) {
                state.dropLogged = true;
                SKSE::log::warn("model view: chunk out of order or malformed ({} bytes), model dropped", a_size);
            }
            return;
        case Model::Assembler::Result::Complete:
            break;
        }
        Store(state.assembler.Id(), state.assembler.Blob());
    }

    RE::NiPointer<RE::NiNode> Instance(std::uint32_t a_model, std::vector<RE::NiNode*>* a_nodes)
    {
        const auto found = state.models.find(a_model);
        if (found == state.models.end() || !LoadTemplate()) {
            return nullptr;
        }
        const auto  start = std::chrono::steady_clock::now();
        auto&       cached = *found->second;
        const auto& model = cached.model;
        RE::NiPointer<RE::NiNode> node(RE::NiNode::Create(static_cast<std::uint16_t>(model.meshes.size())));
        node->name = model.name.c_str();
        // The model's own nodes, each under its parent (always built before it) at its rest pose.
        std::vector<RE::NiNode*> nodes;
        for (const auto& in : model.nodes) {
            auto* child = RE::NiNode::Create(0);
            child->name = in.name.c_str();
            float m[3][3];
            Coords::ToMatrix({ in.rotation[0], in.rotation[1], in.rotation[2], in.rotation[3] }, m);
            for (int r = 0; r < 3; r++) {
                for (int c = 0; c < 3; c++) {
                    child->local.rotate.entry[r][c] = m[r][c];
                }
            }
            child->local.translate = Point(in.translation);
            (in.parent >= 0 ? nodes[in.parent] : node.get())->AttachChild(child, true);
            nodes.push_back(child);
        }
        std::uint32_t vertices = 0, triangles = 0;
        for (const auto& mesh : model.meshes) {
            auto shape = MakeShape(mesh, mesh.texture >= 0 ? cached.textures[mesh.texture] : nullptr,
                mesh.material >= 0 ? model.materials[mesh.material] : kDefaultMaterial);
            if (!shape) {
                continue;
            }
            (mesh.node >= 0 ? nodes[mesh.node] : node.get())->AttachChild(shape.get(), true);
            vertices += mesh.vertexCount;
            triangles += mesh.indexCount / 3;
        }
        if (!cached.logged) {
            cached.logged = true;
            const auto ms = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - start).count();
            SKSE::log::info("model view: {:08X} {} first instance in {:.1f} ms: {} vertices, {} triangles, {} nodes", a_model, model.name, ms,
                vertices, triangles, nodes.size());
        }
        if (a_nodes) {
            *a_nodes = std::move(nodes);
        }
        return node;
    }

    std::uint32_t Find(std::string_view a_name)
    {
        const auto found = state.byName.find(a_name);
        return found != state.byName.end() ? found->second : 0;
    }

    void Tick(float a_delta)
    {
        state.frameTime += a_delta;
        state.frames++;
        if (state.frameTime < kFrameWindow) {
            return;
        }
        state.lastFrameMs = state.frameTime * 1000.0f / static_cast<float>(state.frames);
        if (state.logNextWindow) {
            state.logNextWindow = false;
            SKSE::log::info("model view: frame after the build {:.2f} ms", state.lastFrameMs);
        }
        state.frameTime = 0.0f;
        state.frames = 0;
    }
}
