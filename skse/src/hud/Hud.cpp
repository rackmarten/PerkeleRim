#include "hud/Hud.h"

namespace PerkeleRim::Hud
{
    namespace
    {
        constexpr const char* kClipName = "perkeleRim";
        constexpr double      kLabelSize = 22.0;   // stage units (the HUD stage is 1280 x 720)
        constexpr double      kLabelBelow = 18.0;  // label top, below the screen center
        constexpr double      kBoxPadX = 8.0, kBoxPadY = 3.0;
        constexpr double      kHandCenter = 34.0;  // hand icon center, below the screen center
        constexpr double      kHandSpace = 34.0;   // the label moves down this much under the hand

        bool        shown = false;
        std::string label;
        bool        hand = false;

        void Call(RE::GFxValue& a_target, const char* a_name, std::initializer_list<RE::GFxValue> a_args)
        {
            const std::vector<RE::GFxValue> args(a_args);
            a_target.Invoke(a_name, nullptr, args.data(), args.size());
        }

        // A filled circle from 8 quadratic curves (AS2 drawing API).
        void Circle(RE::GFxValue& a_clip, std::uint32_t a_color, double a_alpha, double a_radius)
        {
            constexpr double kStep = std::numbers::pi / 4.0;
            const double     control = a_radius / std::cos(kStep / 2.0);
            Call(a_clip, "beginFill", { RE::GFxValue(static_cast<double>(a_color)), RE::GFxValue(a_alpha) });
            Call(a_clip, "moveTo", { RE::GFxValue(a_radius), RE::GFxValue(0.0) });
            for (int i = 1; i <= 8; i++) {
                const double mid = (i - 0.5) * kStep, end = i * kStep;
                Call(a_clip, "curveTo", { RE::GFxValue(control * std::cos(mid)), RE::GFxValue(control * std::sin(mid)),
                                            RE::GFxValue(a_radius * std::cos(end)), RE::GFxValue(a_radius * std::sin(end)) });
            }
            Call(a_clip, "endFill", {});
        }

        void Rect(RE::GFxValue& a_clip, double a_x0, double a_y0, double a_x1, double a_y1)
        {
            Call(a_clip, "beginFill", { RE::GFxValue(static_cast<double>(0xFFFFFF)), RE::GFxValue(100.0) });
            Call(a_clip, "moveTo", { RE::GFxValue(a_x0), RE::GFxValue(a_y0) });
            Call(a_clip, "lineTo", { RE::GFxValue(a_x1), RE::GFxValue(a_y0) });
            Call(a_clip, "lineTo", { RE::GFxValue(a_x1), RE::GFxValue(a_y1) });
            Call(a_clip, "lineTo", { RE::GFxValue(a_x0), RE::GFxValue(a_y1) });
            Call(a_clip, "endFill", {});
        }

        // An open hand on a dark disc, like MSC's "use" icon: palm, four fingers, thumb.
        void DrawHand(RE::GFxValue& a_clip)
        {
            Circle(a_clip, 0x000000, 55.0, 15.0);
            Rect(a_clip, -6.0, -1.0, 6.0, 9.0);  // palm
            const double fingerTop[] = { -8.0, -10.5, -10.0, -7.0 };
            for (int i = 0; i < 4; i++) {
                const double x = -6.0 + i * 3.15;
                Rect(a_clip, x, fingerTop[i], x + 2.6, 0.0);
            }
            Rect(a_clip, -10.0, 0.0, -6.0, 3.0);  // thumb
            Rect(a_clip, -10.0, -3.0, -7.4, 0.5);
        }

        bool Build(RE::GFxMovieView* a_movie, RE::GFxValue& a_root, RE::GFxValue& a_clip)
        {
            RE::GFxValue depth;
            a_root.Invoke("getNextHighestDepth", &depth);
            if (!a_root.CreateEmptyMovieClip(&a_clip, kClipName, depth.IsNumber() ? static_cast<std::int32_t>(depth.GetNumber()) : 9000)) {
                return false;
            }

            RE::GFxValue dot;
            a_clip.CreateEmptyMovieClip(&dot, "dot", 1);
            Circle(dot, 0x000000, 70.0, 3.5);  // dark ring so it shows on snow and sky
            Circle(dot, 0xFFFFFF, 100.0, 2.0);

            RE::GFxValue box;
            a_clip.CreateEmptyMovieClip(&box, "box", 2);

            RE::GFxValue handClip;
            if (a_clip.CreateEmptyMovieClip(&handClip, "hand", 4)) {
                DrawHand(handClip);
                handClip.SetMember("_y", RE::GFxValue(kHandCenter));
                handClip.SetMember("_visible", RE::GFxValue(false));
            }

            Call(a_clip, "createTextField", { RE::GFxValue("text"), RE::GFxValue(3.0), RE::GFxValue(-300.0), RE::GFxValue(kLabelBelow),
                                                RE::GFxValue(600.0), RE::GFxValue(kLabelSize * 1.6) });
            RE::GFxValue text;
            if (!a_clip.GetMember("text", &text) || !text.IsDisplayObject()) {
                SKSE::log::warn("hud: could not create the label text field");
                return true;
            }
            RE::GFxValue format;
            a_movie->CreateObject(&format, "TextFormat");
            format.SetMember("font", RE::GFxValue("$EverywhereMediumFont"));
            format.SetMember("size", RE::GFxValue(kLabelSize));
            format.SetMember("color", RE::GFxValue(static_cast<double>(0xFFFFFF)));
            format.SetMember("align", RE::GFxValue("center"));
            text.SetMember("embedFonts", RE::GFxValue(true));
            text.SetMember("selectable", RE::GFxValue(false));
            text.SetMember("multiline", RE::GFxValue(false));
            text.SetMember("wordWrap", RE::GFxValue(false));
            text.Invoke("setNewTextFormat", nullptr, &format, 1);
            SKSE::log::info("hud: layer created in the HUD movie");
            return true;
        }

        void Apply(bool a_show, const std::string& a_label, bool a_hand)
        {
            auto* ui = RE::UI::GetSingleton();
            auto  menu = ui ? ui->GetMenu(RE::HUDMenu::MENU_NAME) : nullptr;
            auto* movie = menu ? menu->uiMovie.get() : nullptr;
            RE::GFxValue root;
            if (!movie || !movie->GetVariable(&root, "_root")) {
                return;
            }
            RE::GFxValue clip;
            root.GetMember(kClipName, &clip);
            if (!clip.IsDisplayObject()) {
                if (!a_show || !Build(movie, root, clip)) {
                    return;
                }
            }
            clip.SetMember("_visible", RE::GFxValue(a_show));
            if (!a_show) {
                return;
            }

            const auto rect = movie->GetVisibleFrameRect();
            clip.SetMember("_x", RE::GFxValue(static_cast<double>(rect.left + rect.right) * 0.5));
            clip.SetMember("_y", RE::GFxValue(static_cast<double>(rect.top + rect.bottom) * 0.5));

            RE::GFxValue text, box, handClip;
            clip.GetMember("text", &text);
            clip.GetMember("box", &box);
            if (clip.GetMember("hand", &handClip) && handClip.IsDisplayObject()) {
                handClip.SetMember("_visible", RE::GFxValue(a_hand));
            }
            const double top = kLabelBelow + (a_hand ? kHandSpace : 0.0);
            if (!text.IsDisplayObject() || !box.IsDisplayObject()) {
                return;
            }
            const bool hasLabel = !a_label.empty();
            text.SetText(a_label.c_str());
            text.SetMember("_visible", RE::GFxValue(hasLabel));
            text.SetMember("_y", RE::GFxValue(top));
            Call(box, "clear", {});
            if (hasLabel) {
                RE::GFxValue width;
                text.GetMember("textWidth", &width);
                const double w = (width.IsNumber() ? width.GetNumber() : 0.0) + 2.0 * kBoxPadX;
                const double h = kLabelSize * 1.3 + 2.0 * kBoxPadY;
                const double x = -w * 0.5, y = top;
                Call(box, "beginFill", { RE::GFxValue(0.0), RE::GFxValue(55.0) });
                Call(box, "moveTo", { RE::GFxValue(x), RE::GFxValue(y) });
                Call(box, "lineTo", { RE::GFxValue(x + w), RE::GFxValue(y) });
                Call(box, "lineTo", { RE::GFxValue(x + w), RE::GFxValue(y + h) });
                Call(box, "lineTo", { RE::GFxValue(x), RE::GFxValue(y + h) });
                Call(box, "endFill", {});
            }
        }
    }

    void Set(bool a_show, const std::string& a_label, bool a_hand, bool a_refresh)
    {
        const std::string wanted = a_show ? a_label : std::string();
        const bool        wantHand = a_show && a_hand;
        const bool        changed = a_show != shown || wanted != label || wantHand != hand;
        if (!a_refresh && !changed) {
            return;
        }
        if (changed) {
            SKSE::log::info("hud: {} label \"{}\"{}", a_show ? "shown," : "hidden,", wanted, wantHand ? ", hand" : "");
        }
        shown = a_show;
        label = wanted;
        hand = wantHand;
        SKSE::GetTaskInterface()->AddUITask([a_show, wanted, wantHand]() { Apply(a_show, wanted, wantHand); });
    }
}
