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

        // Needs bars, bottom left above Skyrim's magicka bar, like MSC's own HUD.
        constexpr double kNeedsLeft = 40.0;     // from the visible frame's left edge
        constexpr double kNeedsBottom = 90.0;   // last row's bottom, up from the visible frame's bottom edge
        constexpr double kNeedSize = 14.0;      // label font size
        constexpr double kNeedRow = 18.0;
        constexpr double kNeedLabelWidth = 80.0;
        constexpr double kNeedBarWidth = 120.0, kNeedBarHeight = 8.0;
        constexpr double kNeedHigh = 0.8;       // a bar this full turns red
        constexpr const char* kNeedNames[] = { "Thirst", "Hunger", "Stress", "Urine", "Fatigue", "Dirtiness", "Drunk" };
        // A full bar: needs run 0..100; MSC's drunk level 0..8, but 2.5 is already deadly in water.
        constexpr float kNeedFull[] = { 100.0f, 100.0f, 100.0f, 100.0f, 100.0f, 100.0f, 4.0f };
        constexpr std::size_t kUrine = 3;
        constexpr std::uint32_t kUrineColor = 0xF2D21B, kUrineHighColor = 0xFF7A00;
        constexpr std::uint32_t kNeedColor = 0xFFFFFF, kNeedHighColor = 0xE03C3C;

        // MSC's subtitle line, bottom center above where Skyrim puts its own.
        constexpr double kSubtitleSize = 24.0;
        constexpr double kSubtitleAbove = 200.0;  // text top, up from the visible frame's bottom edge

        View                       current;
        std::array<int, 7>         drawnNeeds{};  // current.needs in 0.5 % steps of a full bar

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

        void Fill(RE::GFxValue& a_clip, std::uint32_t a_color, double a_alpha, double a_x0, double a_y0, double a_x1, double a_y1)
        {
            Call(a_clip, "beginFill", { RE::GFxValue(static_cast<double>(a_color)), RE::GFxValue(a_alpha) });
            Call(a_clip, "moveTo", { RE::GFxValue(a_x0), RE::GFxValue(a_y0) });
            Call(a_clip, "lineTo", { RE::GFxValue(a_x1), RE::GFxValue(a_y0) });
            Call(a_clip, "lineTo", { RE::GFxValue(a_x1), RE::GFxValue(a_y1) });
            Call(a_clip, "lineTo", { RE::GFxValue(a_x0), RE::GFxValue(a_y1) });
            Call(a_clip, "endFill", {});
        }

        void Rect(RE::GFxValue& a_clip, double a_x0, double a_y0, double a_x1, double a_y1)
        {
            Fill(a_clip, 0xFFFFFF, 100.0, a_x0, a_y0, a_x1, a_y1);
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

        // A single-line text field in Skyrim's HUD font, false if Scaleform refused it.
        bool MakeText(RE::GFxMovieView* a_movie, RE::GFxValue& a_parent, const char* a_name, double a_depth, double a_x, double a_y,
            double a_width, double a_size, const char* a_align, std::uint32_t a_color, RE::GFxValue& a_text)
        {
            Call(a_parent, "createTextField", { RE::GFxValue(a_name), RE::GFxValue(a_depth), RE::GFxValue(a_x), RE::GFxValue(a_y),
                                                  RE::GFxValue(a_width), RE::GFxValue(a_size * 1.6) });
            if (!a_parent.GetMember(a_name, &a_text) || !a_text.IsDisplayObject()) {
                SKSE::log::warn("hud: could not create the {} text field", a_name);
                return false;
            }
            RE::GFxValue format;
            a_movie->CreateObject(&format, "TextFormat");
            format.SetMember("font", RE::GFxValue("$EverywhereMediumFont"));
            format.SetMember("size", RE::GFxValue(a_size));
            format.SetMember("color", RE::GFxValue(static_cast<double>(a_color)));
            format.SetMember("align", RE::GFxValue(a_align));
            a_text.SetMember("embedFonts", RE::GFxValue(true));
            a_text.SetMember("selectable", RE::GFxValue(false));
            a_text.SetMember("multiline", RE::GFxValue(false));
            a_text.SetMember("wordWrap", RE::GFxValue(false));
            a_text.Invoke("setNewTextFormat", nullptr, &format, 1);
            return true;
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

            RE::GFxValue text;
            MakeText(a_movie, a_clip, "text", 3.0, -300.0, kLabelBelow, 600.0, kLabelSize, "center", 0xFFFFFF, text);

            RE::GFxValue subBox, subtitle;
            a_clip.CreateEmptyMovieClip(&subBox, "subBox", 5);
            MakeText(a_movie, a_clip, "subtitle", 6.0, -500.0, 0.0, 1000.0, kSubtitleSize, "center", 0xFFFFFF, subtitle);

            RE::GFxValue needs;
            if (a_clip.CreateEmptyMovieClip(&needs, "needs", 7)) {
                for (std::size_t i = 0; i < std::size(kNeedNames); i++) {
                    const double y = static_cast<double>(i) * kNeedRow;
                    RE::GFxValue name, bar;
                    const std::string textName = "name" + std::to_string(i), barName = "bar" + std::to_string(i);
                    if (MakeText(a_movie, needs, textName.c_str(), 10.0 + i, 0.0, y - 3.0, kNeedLabelWidth, kNeedSize, "left",
                            i == kUrine ? kUrineColor : kNeedColor, name)) {
                        name.SetText(kNeedNames[i]);
                    }
                    needs.CreateEmptyMovieClip(&bar, barName.c_str(), 30 + static_cast<std::int32_t>(i));
                }
            }
            SKSE::log::info("hud: layer created in the HUD movie");
            return true;
        }

        // A dark box behind a_text at a_top, sized to the text.
        void DrawBox(RE::GFxValue& a_box, RE::GFxValue& a_text, double a_top, double a_size)
        {
            RE::GFxValue width;
            a_text.GetMember("textWidth", &width);
            const double w = (width.IsNumber() ? width.GetNumber() : 0.0) + 2.0 * kBoxPadX;
            const double h = a_size * 1.3 + 2.0 * kBoxPadY;
            Fill(a_box, 0x000000, 55.0, -w * 0.5, a_top, w * 0.5, a_top + h);
        }

        void DrawNeeds(RE::GFxValue& a_needs, const View& a_view)
        {
            for (std::size_t i = 0; i < std::size(kNeedNames); i++) {
                RE::GFxValue bar;
                const std::string barName = "bar" + std::to_string(i);
                if (!a_needs.GetMember(barName.c_str(), &bar) || !bar.IsDisplayObject()) {
                    continue;
                }
                const double fraction = std::clamp(static_cast<double>(a_view.needs[i] / kNeedFull[i]), 0.0, 1.0);
                const bool   high = fraction >= kNeedHigh;
                const auto   color = i == kUrine ? (high ? kUrineHighColor : kUrineColor) : (high ? kNeedHighColor : kNeedColor);
                const double x0 = kNeedLabelWidth, y0 = static_cast<double>(i) * kNeedRow + 4.0;
                Call(bar, "clear", {});
                Fill(bar, 0x000000, 55.0, x0 - 1.0, y0 - 1.0, x0 + kNeedBarWidth + 1.0, y0 + kNeedBarHeight + 1.0);
                if (fraction > 0.0) {
                    Fill(bar, color, 90.0, x0, y0, x0 + kNeedBarWidth * fraction, y0 + kNeedBarHeight);
                }
            }
        }

        void Apply(const View& a_view)
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
                if (!a_view.show || !Build(movie, root, clip)) {
                    return;
                }
            }
            clip.SetMember("_visible", RE::GFxValue(a_view.show));
            if (!a_view.show) {
                return;
            }

            // The clip sits at the screen center; the needs and subtitle are placed from the edges.
            const auto   rect = movie->GetVisibleFrameRect();
            const double cx = static_cast<double>(rect.left + rect.right) * 0.5;
            const double cy = static_cast<double>(rect.top + rect.bottom) * 0.5;
            clip.SetMember("_x", RE::GFxValue(cx));
            clip.SetMember("_y", RE::GFxValue(cy));

            RE::GFxValue text, box, handClip;
            clip.GetMember("text", &text);
            clip.GetMember("box", &box);
            if (clip.GetMember("hand", &handClip) && handClip.IsDisplayObject()) {
                handClip.SetMember("_visible", RE::GFxValue(a_view.hand));
            }
            if (text.IsDisplayObject() && box.IsDisplayObject()) {
                const double top = kLabelBelow + (a_view.hand ? kHandSpace : 0.0);
                const bool   hasLabel = !a_view.label.empty();
                text.SetText(a_view.label.c_str());
                text.SetMember("_visible", RE::GFxValue(hasLabel));
                text.SetMember("_y", RE::GFxValue(top));
                Call(box, "clear", {});
                if (hasLabel) {
                    DrawBox(box, text, top, kLabelSize);
                }
            }

            RE::GFxValue subtitle, subBox;
            clip.GetMember("subtitle", &subtitle);
            clip.GetMember("subBox", &subBox);
            if (subtitle.IsDisplayObject() && subBox.IsDisplayObject()) {
                const double top = static_cast<double>(rect.bottom) - kSubtitleAbove - cy;
                const bool   has = !a_view.subtitle.empty();
                subtitle.SetText(a_view.subtitle.c_str());
                subtitle.SetMember("_visible", RE::GFxValue(has));
                subtitle.SetMember("_y", RE::GFxValue(top));
                Call(subBox, "clear", {});
                if (has) {
                    DrawBox(subBox, subtitle, top, kSubtitleSize);
                }
            }

            RE::GFxValue needs;
            if (clip.GetMember("needs", &needs) && needs.IsDisplayObject()) {
                needs.SetMember("_visible", RE::GFxValue(a_view.hasNeeds));
                needs.SetMember("_x", RE::GFxValue(static_cast<double>(rect.left) + kNeedsLeft - cx));
                needs.SetMember("_y", RE::GFxValue(static_cast<double>(rect.bottom) - kNeedsBottom - kNeedRow * std::size(kNeedNames) - cy));
                if (a_view.hasNeeds) {
                    DrawNeeds(needs, a_view);
                }
            }
        }

        std::array<int, 7> Quantize(const View& a_view)
        {
            std::array<int, 7> q{};
            for (std::size_t i = 0; i < q.size(); i++) {
                q[i] = static_cast<int>(std::lround(std::clamp(a_view.needs[i] / kNeedFull[i], 0.0f, 1.0f) * 200.0f));
            }
            return q;
        }
    }

    void Set(const View& a_view, bool a_refresh)
    {
        View view = a_view;
        if (!view.show) {
            view = View{};
        }
        const auto needs = Quantize(view);
        const bool textChanged = view.show != current.show || view.label != current.label || view.hand != current.hand ||
                                 view.subtitle != current.subtitle;
        const bool changed = textChanged || view.hasNeeds != current.hasNeeds || (view.hasNeeds && needs != drawnNeeds);
        if (!a_refresh && !changed) {
            return;
        }
        if (textChanged) {
            SKSE::log::info("hud: {} label \"{}\"{}, subtitle \"{}\"", view.show ? "shown," : "hidden,", view.label,
                view.hand ? ", hand" : "", view.subtitle);
        }
        current = view;
        drawnNeeds = needs;
        SKSE::GetTaskInterface()->AddUITask([view]() { Apply(view); });
    }
}
