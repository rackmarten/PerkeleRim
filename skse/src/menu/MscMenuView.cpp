#include "menu/MscMenuView.h"

#include <filesystem>

#include "hands/HandView.h"
#include "link/LinkService.h"
#include "player/PlayerSync.h"

namespace PerkeleRim::MscMenuView
{
    namespace
    {
        constexpr std::string_view kMenuName = "PerkeleRim MSC Menu";
        constexpr const char*      kMovie = "PerkeleRim/MscMenu";  // Data/Interface/PerkeleRim/MscMenu.swf
        constexpr const char*      kMovieFile = "Data/Interface/PerkeleRim/MscMenu.swf";
        constexpr double           kBackdropAlpha = 45.0;  // Skyrim's paused world dimmed behind MSC's menu, percent
        constexpr const char*      kBackdropClip = "perkeleRimBackdrop";
        constexpr const char*      kMenuClip = "perkeleRimMscMenu";

        MscMenu::Machine machine;
        bool             mscOpen = false;     // PlayerState kPlayerMscMenu, last good read
        bool             holdEscape = false;  // Escape closed the menu: Skyrim's own Escape waits for its release
        bool             sized = false;       // the image's natural size was logged
        bool             cursorLogged = false;

        void Show(bool a_show)
        {
            if (auto* queue = RE::UIMessageQueue::GetSingleton()) {
                queue->AddMessage(kMenuName, a_show ? RE::UI_MESSAGE_TYPE::kShow : RE::UI_MESSAGE_TYPE::kHide, nullptr);
            }
        }

        void Call(RE::GFxValue& a_target, const char* a_name, std::initializer_list<RE::GFxValue> a_args)
        {
            std::vector<RE::GFxValue> args(a_args);
            a_target.Invoke(a_name, nullptr, args.data(), args.size());
        }

        bool SkyrimMenuUp()
        {
            auto* ui = RE::UI::GetSingleton();
            return ui && (ui->GameIsPaused() || ui->IsMenuOpen(RE::CursorMenu::MENU_NAME));
        }

        // Skyrim's paused world dimmed, then MSC's menu over the visible frame, flipped like the hand
        // layer (Unity renders into textures bottom row first).
        void Draw(RE::GFxMovieView* a_movie)
        {
            RE::GFxValue root;
            if (!a_movie || !a_movie->GetVariable(&root, "_root")) {
                return;
            }
            const auto   rect = a_movie->GetVisibleFrameRect();
            RE::GFxValue backdrop;
            root.GetMember(kBackdropClip, &backdrop);
            if (!backdrop.IsDisplayObject()) {
                Call(root, "createEmptyMovieClip", { RE::GFxValue(kBackdropClip), RE::GFxValue(1.0) });
                if (!root.GetMember(kBackdropClip, &backdrop) || !backdrop.IsDisplayObject()) {
                    return;
                }
            }
            Call(backdrop, "clear", {});
            Call(backdrop, "beginFill", { RE::GFxValue(0.0), RE::GFxValue(kBackdropAlpha) });
            Call(backdrop, "moveTo", { RE::GFxValue(static_cast<double>(rect.left)), RE::GFxValue(static_cast<double>(rect.top)) });
            Call(backdrop, "lineTo", { RE::GFxValue(static_cast<double>(rect.right)), RE::GFxValue(static_cast<double>(rect.top)) });
            Call(backdrop, "lineTo", { RE::GFxValue(static_cast<double>(rect.right)), RE::GFxValue(static_cast<double>(rect.bottom)) });
            Call(backdrop, "lineTo", { RE::GFxValue(static_cast<double>(rect.left)), RE::GFxValue(static_cast<double>(rect.bottom)) });
            Call(backdrop, "endFill", {});

            const auto&  name = HandView::MenuImage();
            RE::GFxValue menu;
            root.GetMember(kMenuClip, &menu);
            if (!menu.IsDisplayObject()) {
                Call(root, "createEmptyMovieClip", { RE::GFxValue(kMenuClip), RE::GFxValue(2.0) });
                if (!root.GetMember(kMenuClip, &menu) || !menu.IsDisplayObject()) {
                    SKSE::log::warn("msc menu: could not create the menu clip");
                    return;
                }
            }
            menu.SetMember("_visible", RE::GFxValue(!name.empty()));
            if (name.empty()) {
                return;  // MSC hasn't drawn its menu yet
            }
            RE::GFxValue loaded, image;
            menu.GetMember("image", &loaded);
            if (!loaded.IsString() || name != loaded.GetString()) {
                if (!menu.CreateEmptyMovieClip(&image, "img", 1)) {
                    return;
                }
                const std::string url = "img://" + name;
                Call(image, "loadMovie", { RE::GFxValue(url.c_str()) });
                menu.SetMember("image", RE::GFxValue(name.c_str()));
                sized = false;
                SKSE::log::info("msc menu: image {}", url);
            }
            menu.SetMember("_x", RE::GFxValue(static_cast<double>(rect.left)));
            menu.SetMember("_y", RE::GFxValue(static_cast<double>(rect.top)));
            RE::GFxValue width, height, xscale, yscale;
            if (!menu.GetMember("img", &image) || !image.IsDisplayObject() || !image.GetMember("_width", &width) ||
                !image.GetMember("_height", &height) || !image.GetMember("_xscale", &xscale) || !image.GetMember("_yscale", &yscale) ||
                !width.IsNumber() || !height.IsNumber() || !xscale.IsNumber() || !yscale.IsNumber() || width.GetNumber() <= 0.0 ||
                height.GetNumber() <= 0.0 || xscale.GetNumber() == 0.0 || yscale.GetNumber() == 0.0) {
                return;  // not loaded yet
            }
            const double naturalWidth = width.GetNumber() * 100.0 / std::abs(xscale.GetNumber());
            const double naturalHeight = height.GetNumber() * 100.0 / std::abs(yscale.GetNumber());
            image.SetMember("_xscale", RE::GFxValue(100.0 * (rect.right - rect.left) / naturalWidth));
            image.SetMember("_yscale", RE::GFxValue(-100.0 * (rect.bottom - rect.top) / naturalHeight));
            image.SetMember("_y", RE::GFxValue(static_cast<double>(rect.bottom - rect.top)));
            if (!sized) {
                sized = true;
                SKSE::log::info("msc menu: image is {:.0f} x {:.0f} units, scaled to the visible frame {} x {}", naturalWidth, naturalHeight,
                    rect.right - rect.left, rect.bottom - rect.top);
            }
        }

        // Pauses Skyrim and shows its cursor; draws MSC's menu every frame, also while paused (the
        // player update that ticks everything else stands still then).
        class Menu : public RE::IMenu
        {
        public:
            Menu()
            {
                menuFlags.set(Flag::kPausesGame, Flag::kUsesCursor, Flag::kDisablePauseMenu, Flag::kRequiresUpdate, Flag::kUsesMenuContext);
                depthPriority = 10;  // over the HUD, under Skyrim's cursor
                inputContext = Context::kMenuMode;
                auto* scaleform = RE::BSScaleformManager::GetSingleton();
                if (!scaleform || !scaleform->LoadMovieEx(this, kMovie, RE::BSScaleformManager::ScaleModeType::kExactFit, [](RE::GFxMovieDef*) {})) {
                    SKSE::log::warn("msc menu: could not load Interface/{}.swf", kMovie);
                }
            }

            static RE::IMenu* Create() { return new Menu(); }

            void AdvanceMovie(float a_interval, std::uint32_t a_currentTime) override
            {
                auto&               link = LinkService::Get();
                Protocol::HandLayer hands{};
                HandView::Update(link.ReadHandLayer(hands) ? &hands : nullptr, a_interval);
                Tick(a_interval);
                Draw(uiMovie.get());
                IMenu::AdvanceMovie(a_interval, a_currentTime);
            }
        };
    }

    void OnDataLoaded()
    {
        if (auto* ui = RE::UI::GetSingleton()) {
            ui->Register(kMenuName, Menu::Create);
            SKSE::log::info("msc menu: registered (F1 while MSC has the player)");
        }
    }

    bool Key(std::uint32_t a_dik, bool a_down, bool a_up)
    {
        if (a_dik == Input::kDikEscape && holdEscape) {
            holdEscape = !a_up;  // the press that closed the menu, until let go
            return true;
        }
        if (!MscMenu::ClosesMenu(a_dik) || !PlayerSync::Owned()) {
            return false;
        }
        if (!a_down) {
            return a_dik == MscMenu::kDikF1 || machine.Shown();
        }
        if (machine.Shown()) {
            machine.Close();
            holdEscape = a_dik == Input::kDikEscape;
            Show(false);
            SKSE::log::info("msc menu: closed from Skyrim ({})", a_dik == MscMenu::kDikF1 ? "F1" : "Escape");
            return true;
        }
        if (a_dik != MscMenu::kDikF1) {
            return false;  // Escape with the menu shut: Skyrim's own
        }
        if (SkyrimMenuUp()) {
            return true;  // not over Skyrim's own menus
        }
        static const bool movie = std::filesystem::exists(kMovieFile);
        if (!movie) {
            SKSE::log::warn("msc menu: {} is missing, F1 does nothing (reinstall PerkeleRim)", kMovieFile);
            RE::SendHUDMessage::ShowHUDMessage("PerkeleRim's MscMenu.swf is missing: reinstall PerkeleRim to use My Summer Car's menu.");
            return true;
        }
        const MscMenu::Frame frame{ PlayerSync::Owned(), LinkService::Get().IsUp(), mscOpen, 0.0f };
        if (machine.Press(frame)) {
            Show(true);
            cursorLogged = false;  // log the cursor's mapping once per opening
            SKSE::log::info("msc menu: F1, opening My Summer Car's menu");
        }
        return true;
    }

    void Tick(float a_delta)
    {
        if (machine.Current() == MscMenu::Phase::Closed) {
            return;
        }
        auto&                link = LinkService::Get();
        Protocol::PlayerState pose{};
        if (link.ReadPlayer(pose)) {  // a torn read keeps the last answer
            mscOpen = (pose.flags & Protocol::kPlayerMscMenu) != 0;
        }
        const MscMenu::Frame frame{ PlayerSync::Owned(), link.IsUp(), mscOpen, a_delta };
        switch (machine.Tick(frame)) {
        case MscMenu::Say::Opened:
            SKSE::log::info("msc menu: My Summer Car's menu is up");
            break;
        case MscMenu::Say::Closed:
            Show(false);
            SKSE::log::info("msc menu: closed");
            break;
        case MscMenu::Say::NoAnswer:
            Show(false);
            SKSE::log::warn("msc menu: My Summer Car didn't open its menu within {:.0f} s", MscMenu::kOpenSeconds);
            RE::SendHUDMessage::ShowHUDMessage("My Summer Car's menu didn't open.");
            break;
        case MscMenu::Say::Abandoned:
            Show(false);
            SKSE::log::info("msc menu: closed, Kumpo is back in Skyrim or the link is down");
            break;
        default:
            break;
        }
    }

    bool HoldsKey(std::uint32_t a_dik)
    {
        return machine.Shown() || (a_dik == Input::kDikEscape && holdEscape) || (a_dik == MscMenu::kDikF1 && PlayerSync::Owned());
    }

    bool Requested() { return machine.Requested(); }
    bool Shown() { return machine.Shown(); }

    void Cursor(float& a_x, float& a_y)
    {
        a_x = a_y = 0.5f;
        auto* cursor = RE::MenuCursor::GetSingleton();
        if (!cursor) {
            return;
        }
        const auto& data = cursor->GetRuntimeData();
        const auto  screen = RE::BSGraphics::Renderer::GetScreenSize();
        const float width = MscMenu::CursorSpan(static_cast<float>(screen.width), data.screenWidthX);
        const float height = MscMenu::CursorSpan(static_cast<float>(screen.height), data.screenWidthY);
        a_x = MscMenu::Normalize(data.cursorPosX, width);
        a_y = MscMenu::Normalize(data.cursorPosY, height);
        if (!cursorLogged) {
            cursorLogged = true;
            SKSE::log::info("msc menu: cursor at {:.0f}, {:.0f} of {:.0f} x {:.0f} (MenuCursor screen {:.0f} x {:.0f}, safe zone {:.0f}, {:.0f})",
                data.cursorPosX, data.cursorPosY, width, height, data.screenWidthX, data.screenWidthY, data.safeZoneX, data.safeZoneY);
        }
    }
}
