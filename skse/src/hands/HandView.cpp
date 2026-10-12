#include "hands/HandView.h"

#include "model/ModelView.h"

namespace PerkeleRim::HandView
{
    namespace
    {
        namespace W32 = REX::W32;

        // MSC stops copying when its HandCamera is off; a frozen hand would stay on screen, so the
        // layer goes away once no new copy came for this long.
        constexpr float kStaleSeconds = 0.25f;
        constexpr float kStatWindow = 5.0f;

        struct State
        {
            std::uint64_t handle = 0;  // MSC's shared handle we opened (or failed to)
            std::string   image;       // registered image name, empty = none
            std::uint32_t width = 0, height = 0;
            int           serial = 0;
            bool          shown = false;
            std::uint32_t lastFrame = 0;
            float         sinceCopy = 0.0f;

            float         statTime = 0.0f;
            std::uint32_t statFrames = 0, statCopies = 0;
            std::uint64_t statCost = 0;
        } state;

        const std::string kNone;

        // A new texture handle (MSC started or Skyrim's screen size changed): open it and register
        // it under a new name, so Scaleform's image cache never hands out the old one. Earlier ones
        // are kept, like ModelView's textures.
        bool Open(const Protocol::HandLayer& a_layer)
        {
            auto* device = RE::BSGraphics::Renderer::GetDevice();
            W32::ID3D11Texture2D* texture = nullptr;
            if (!device || !W32::SUCCESS(device->OpenSharedResource(reinterpret_cast<W32::HANDLE>(a_layer.texture), W32::IID_ID3D11Texture2D,
                               reinterpret_cast<void**>(&texture))) ||
                !texture) {
                SKSE::log::warn("hand layer: could not open MSC's texture 0x{:X}", a_layer.texture);
                return false;
            }
            W32::ID3D11ShaderResourceView* view = nullptr;
            if (!W32::SUCCESS(device->CreateShaderResourceView(texture, nullptr, &view)) || !view) {
                texture->Release();
                SKSE::log::warn("hand layer: CreateShaderResourceView failed (format {})", a_layer.format);
                return false;
            }

            const std::string name = fmt::format("PerkeleRimHands{}", ++state.serial);
            auto*             source = ModelView::Wrap(texture, view, a_layer.width, a_layer.height, 1, name);
            auto*             external = new RE::BSScaleformExternalTexture();  // kept: its destructor drops the image
            external->SetTexture(source);
            external->filePath = name;
            auto* manager = RE::BSScaleformManager::GetSingleton();
            if (!manager || !manager->imageLoader || !manager->imageLoader->AddTexture(*external)) {
                SKSE::log::warn("hand layer: the image loader refused {}", name);
                return false;
            }
            state.image = name;
            state.width = a_layer.width;
            state.height = a_layer.height;
            SKSE::log::info("hand layer: opened MSC's texture 0x{:X} {}x{} format {} as img://{}", a_layer.texture, a_layer.width, a_layer.height,
                a_layer.format, name);
            return true;
        }

        void LogStats(float a_delta)
        {
            state.statTime += a_delta;
            state.statFrames++;
            if (state.statTime < kStatWindow) {
                return;
            }
            SKSE::log::info("hand layer: shown={} msc copies={} skyrim frames={} skyrim frame={:.2f} ms msc layer cpu={} us", state.shown,
                state.statCopies, state.statFrames, state.statTime * 1000.0f / static_cast<float>(state.statFrames),
                state.statCopies ? state.statCost / state.statCopies : 0);
            state.statTime = 0.0f;
            state.statFrames = state.statCopies = 0;
            state.statCost = 0;
        }
    }

    void Update(const Protocol::HandLayer* a_layer, float a_delta)
    {
        LogStats(a_delta);
        if (!a_layer) {  // torn read: keep what's shown
            return;
        }
        if ((a_layer->flags & Protocol::kHandLayerValid) == 0 || a_layer->texture == 0) {
            state.shown = false;
            return;
        }
        if (a_layer->texture != state.handle) {
            state.handle = a_layer->texture;  // a failed open isn't retried every frame
            state.image.clear();
            Open(*a_layer);
        }
        if (a_layer->frame != state.lastFrame) {
            state.lastFrame = a_layer->frame;
            state.sinceCopy = 0.0f;
            state.statCopies++;
            state.statCost += a_layer->costMicros;
        } else {
            state.sinceCopy += a_delta;
        }
        state.shown = !state.image.empty() && state.sinceCopy < kStaleSeconds;
    }

    const std::string& Image() { return state.shown ? state.image : kNone; }
    std::uint32_t      Width() { return state.width; }
    std::uint32_t      Height() { return state.height; }

    void Hide() { state.shown = false; }

    std::array<float, 3> Light(RE::Actor* a_player)
    {
        // Ambient plus part of the sun (or a cell's directional light). A clear Skyrim noon comes
        // to about kDaylight, which is what MSC's neutral lighting stands for.
        constexpr float kSunShare = 0.5f;
        constexpr float kDaylight = 0.95f;
        constexpr float kFloor = 0.12f;  // never fully black, like Skyrim's own night

        float rgb[3] = { kDaylight, kDaylight, kDaylight };
        auto* cell = a_player ? a_player->GetParentCell() : nullptr;
        if (cell && cell->IsInteriorCell() && !cell->UsesSkyLighting()) {
            if (const auto* data = cell->GetLighting()) {
                const auto* tmpl = cell->GetRuntimeData().lightingTemplate;
                const auto  inherit = data->lightingTemplateInheritanceFlags;
                const auto& ambient = tmpl && inherit.any(RE::INTERIOR_DATA::Inherit::kAmbientColor) ? tmpl->data.ambient : data->ambient;
                const auto& directional =
                    tmpl && inherit.any(RE::INTERIOR_DATA::Inherit::kDirectionalColor) ? tmpl->data.directional : data->directional;
                const std::uint8_t a[3] = { ambient.red, ambient.green, ambient.blue };
                const std::uint8_t d[3] = { directional.red, directional.green, directional.blue };
                for (int i = 0; i < 3; i++) {
                    rgb[i] = (a[i] + kSunShare * d[i]) / 255.0f;
                }
            }
        } else if (auto* sky = RE::Sky::GetSingleton()) {
            const auto& ambient = sky->skyColor[RE::TESWeather::ColorTypes::kAmbient];
            const auto& sun = sky->skyColor[RE::TESWeather::ColorTypes::kSunlight];
            rgb[0] = ambient.red + kSunShare * sun.red;
            rgb[1] = ambient.green + kSunShare * sun.green;
            rgb[2] = ambient.blue + kSunShare * sun.blue;
        }
        // Brighter than daylight keeps its hue at full brightness.
        const float top = (std::max)({ rgb[0], rgb[1], rgb[2], kDaylight }) / kDaylight;
        std::array<float, 3> tint{};
        for (int i = 0; i < 3; i++) {
            tint[i] = std::clamp(rgb[i] / kDaylight / top, kFloor, 1.0f);
        }
        return tint;
    }
}
