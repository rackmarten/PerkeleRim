#include "model/ModelView.h"

#include <DirectXPackedVector.h>

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

        // How far in front of the player a new model is put down.
        constexpr float kAhead = 120.0f;

        // Frame cost is averaged over windows this long, before and after a build.
        constexpr float kFrameWindow = 5.0f;

        // Vertex layout: full precision position + bitangent x, half UV, byte normal + bitangent y,
        // byte tangent + bitangent z (how Skyrim SE stores static meshes).
        constexpr std::uint32_t kStride = 28;
        constexpr std::uint32_t kUvOffset = 16;
        constexpr std::uint32_t kNormalOffset = 20;
        constexpr std::uint32_t kTangentOffset = 24;

        struct State
        {
            Model::Assembler                                   assembler;
            bool                                               dropLogged = false;
            RE::NiPointer<RE::NiNode>                          templateModel;
            RE::BSTriShape*                                    templateShape = nullptr;
            bool                                               templateFailed = false;
            std::map<std::string, RE::NiPointer<RE::NiNode>>  models;  // by name, the newest of each
            // Textures built here are never destroyed: the game's NiSourceTexture destructor expects
            // one it loaded itself.
            std::vector<RE::NiPointer<RE::NiSourceTexture>>    textures;

            float frameTime = 0.0f;
            int   frames = 0;
            float lastFrameMs = 0.0f;  // average of the last full window, 0 = none yet
            bool  logNextWindow = false;
        } state;

        std::uint8_t Byte(float a_unit) { return static_cast<std::uint8_t>(std::clamp(std::lround((a_unit + 1.0f) * 127.5f), 0L, 255L)); }

        RE::NiPoint3 Point(const float (&a_v)[3]) { return { a_v[0], a_v[1], a_v[2] }; }

        // The same node VehicleView hangs the van in, so models are lit and culled like the world.
        RE::NiNode* Scene()
        {
            auto* player = RE::PlayerCharacter::GetSingleton();
            auto* root = player ? player->Get3D() : nullptr;
            if (root && root->parent) {
                return root->parent;
            }
            return RE::BSShaderManager::State::GetSingleton().shadowSceneNode[0];
        }

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

        // A GPU texture with mips, wrapped in a NiSourceTexture the lighting shader can bind. The
        // NiSourceTexture is put together by hand (vtable, renderer texture) since the game only makes
        // them while loading a file.
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

            auto* renderer = static_cast<RE::BSGraphics::Texture*>(RE::malloc(sizeof(RE::BSGraphics::Texture)));
            std::memset(renderer, 0, sizeof(*renderer));
            renderer->texture = texture;
            renderer->resourceView = view;
            renderer->width = static_cast<std::uint16_t>(a_texture.width);
            renderer->height = static_cast<std::uint16_t>(a_texture.height);
            renderer->mips = static_cast<std::uint8_t>(levels.size());
            renderer->refCount = 1;

            static REL::Relocation<std::uintptr_t> vtable{ RE::NiSourceTexture::VTABLE[0] };
            auto* source = static_cast<RE::NiSourceTexture*>(RE::malloc(sizeof(RE::NiSourceTexture)));
            std::memset(source, 0, sizeof(*source));
            *reinterpret_cast<std::uintptr_t*>(source) = vtable.address();
            source->name = a_name.c_str();
            source->rendererTexture = renderer;
            state.textures.emplace_back(source);
            return source;
        }

        RE::BSGraphics::VertexDesc VertexDesc()
        {
            using V = RE::BSGraphics::Vertex;
            const std::uint64_t flags = V::VF_VERTEX | V::VF_UV | V::VF_NORMAL | V::VF_TANGENT | V::VF_FULLPREC;
            const std::uint64_t raw = (kStride / 4) | (static_cast<std::uint64_t>(kUvOffset / 4) << 4) |
                                      (static_cast<std::uint64_t>(kNormalOffset / 4) << 12) |
                                      (static_cast<std::uint64_t>(kTangentOffset / 4) << 16) | (flags << 44);
            RE::BSGraphics::VertexDesc desc;
            std::memcpy(&desc, &raw, sizeof(raw));
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

        RE::BSLightingShaderProperty* MakeShader(RE::NiSourceTexture* a_diffuse)
        {
            auto&       shaderState = *RE::BSGraphics::State::GetSingleton();
            auto*       material = RE::BSLightingShaderMaterialBase::CreateMaterial<RE::BSLightingShaderMaterial>();
            material->diffuseTexture = RE::NiPointer<RE::NiSourceTexture>(a_diffuse ? a_diffuse : shaderState.GetRuntimeData().defaultTextureWhite.get());
            material->normalTexture = shaderState.GetRuntimeData().defaultTextureNormalMap;
            material->specularColorScale = 0.0f;

            auto* shader = RE::BSLightingShaderProperty::Create();
            // The material is left alive: destroying it here crashed the game, and it is a few bytes
            // per mesh.
            shader->SetMaterial(material, true);
            // SetMaterial copies everything but the textures (they come from a texture set, which has
            // none here), so they go on the shader's own copy.
            auto* own = static_cast<RE::BSLightingShaderMaterialBase*>(shader->material);
            own->diffuseTexture = material->diffuseTexture;
            own->normalTexture = material->normalTexture;
            using F = RE::BSShaderProperty::EShaderPropertyFlag;
            shader->flags.set(F::kReceiveShadows, F::kCastShadows, F::kZBufferTest, F::kZBufferWrite);
            return shader;
        }

        RE::NiPointer<RE::BSTriShape> MakeShape(const Model::Mesh& a_mesh, RE::NiSourceTexture* a_diffuse)
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
            geometry.alphaProperty.reset();
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

            auto* shader = MakeShader(a_diffuse);
            geometry.shaderProperty.reset(shader);
            shader->SetupGeometry(shape.get());
            shader->FinishSetupGeometry(shape.get());
            return shape;
        }

        void Build(const Model::Model& a_model)
        {
            const auto start = std::chrono::steady_clock::now();
            if (!LoadTemplate()) {
                return;
            }
            std::vector<RE::NiSourceTexture*> textures;
            for (std::size_t i = 0; i < a_model.textures.size(); i++) {
                textures.push_back(MakeTexture(a_model.textures[i], std::format("PerkeleRim\\{}\\{}", a_model.name, i)));
            }
            RE::NiPointer<RE::NiNode> node(RE::NiNode::Create(static_cast<std::uint16_t>(a_model.meshes.size())));
            node->name = a_model.name.c_str();
            std::uint32_t vertices = 0, triangles = 0;
            float         lowest = FLT_MAX;
            for (const auto& mesh : a_model.meshes) {
                auto shape = MakeShape(mesh, mesh.texture >= 0 ? textures[mesh.texture] : nullptr);
                if (!shape) {
                    continue;
                }
                node->AttachChild(shape.get(), true);
                vertices += mesh.vertexCount;
                triangles += mesh.indexCount / 3;
                for (std::uint32_t i = 0; i < mesh.vertexCount; i++) {
                    lowest = (std::min)(lowest, mesh.Vertex(i).position[2]);
                }
            }

            // Standing on the ground in front of the player, replacing an older copy.
            auto* player = RE::PlayerCharacter::GetSingleton();
            auto* scene = Scene();
            if (!player || !scene) {
                SKSE::log::warn("model view: no player or scene for {}", a_model.name);
                return;
            }
            const float heading = player->GetAngleZ();
            const auto  feet = player->GetPosition();
            node->local.translate = { feet.x + std::sin(heading) * kAhead, feet.y + std::cos(heading) * kAhead,
                feet.z - (lowest == FLT_MAX ? 0.0f : lowest) };
            if (auto old = state.models.find(a_model.name); old != state.models.end() && old->second->parent) {
                old->second->parent->DetachChild(old->second.get());
            }
            state.models[a_model.name] = node;
            scene->AttachChild(node.get(), true);
            RE::NiUpdateData update{};
            node->Update(update);

            const auto ms = std::chrono::duration<double, std::milli>(std::chrono::steady_clock::now() - start).count();
            SKSE::log::info("model view: built {} in {:.1f} ms: {} meshes, {} vertices, {} triangles, {} textures, at ({:.0f},{:.0f},{:.0f})",
                a_model.name, ms, a_model.meshes.size(), vertices, triangles, textures.size(), node->local.translate.x,
                node->local.translate.y, node->local.translate.z);
            if (state.lastFrameMs > 0.0f) {
                SKSE::log::info("model view: frame before the build {:.2f} ms", state.lastFrameMs);
            }
            state.frameTime = 0.0f;
            state.frames = 0;
            state.logNextWindow = true;
        }
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
        const auto& blob = state.assembler.Blob();
        const auto  model = Model::Parse(blob.data(), blob.size());
        if (!model) {
            SKSE::log::warn("model view: model {} is malformed ({} bytes)", state.assembler.Id(), blob.size());
            return;
        }
        SKSE::log::info("model view: received {} ({} bytes)", model->name, blob.size());
        Build(*model);
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
