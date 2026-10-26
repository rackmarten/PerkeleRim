#include "autoload/Autoload.h"

#include "Ini.h"

namespace PerkeleRim::Autoload
{
    namespace
    {
        constexpr auto kFlagFile = "PerkeleRim_autoload.txt";

        bool started = false;  // the main menu asked for the load (once per start)
        bool loading = false;  // ...and it hasn't finished yet

        // The newest save in Documents/My Games/Skyrim Special Edition/Saves, or the one AutoloadSave=<name>
        // in PerkeleRim.ini names (its file name, .ess or not; scripts/hit-test.ps1 -Save sets it).
        void LoadNewestSave()
        {
            auto*                 saves = RE::BGSSaveLoadManager::GetSingleton();
            const auto            logs = SKSE::log::log_directory();  // .../Skyrim Special Edition/SKSE
            const auto            folder = logs ? logs->parent_path() / "Saves" : std::filesystem::path();
            std::filesystem::path newest;
            std::error_code       error;
            if (auto named = Ini::Value("AutoloadSave"); !named.empty()) {
                if (named.ends_with(".ess")) {
                    named.resize(named.size() - 4);
                }
                if (std::filesystem::exists(folder / (named + ".ess"), error)) {
                    newest = folder / (named + ".ess");
                } else {
                    SKSE::log::warn("autoload: no save {} in {}, the newest instead", named, folder.string());
                }
            }
            for (const auto& entry : std::filesystem::directory_iterator(newest.empty() ? folder : std::filesystem::path(), error)) {
                if (entry.path().extension() == ".ess" &&
                    (newest.empty() || entry.last_write_time() > std::filesystem::last_write_time(newest))) {
                    newest = entry.path();
                }
            }
            if (!saves || newest.empty()) {
                SKSE::log::warn("autoload: no save in {}", folder.string());
                loading = false;
                return;
            }
            SKSE::log::info("autoload: loading {}", newest.stem().string());
            saves->Load(newest.stem().string().c_str(), false);
        }

        // While our load is under way, a message box (the "mods disable achievements" warning) is
        // answered with its first button that isn't cancel.
        void AnswerMessageBox()
        {
            auto* box = RE::MessageBoxMenu::GetCurrentMessageBoxData();
            if (!loading || !box) {
                return;
            }
            std::int32_t button = 0;
            if (box->cancelButtonIndex == 0 && box->buttonText.size() > 1) {
                button = 1;
            }
            SKSE::log::info("autoload: answering \"{}\" with \"{}\"", box->bodyText.c_str(),
                button < static_cast<std::int32_t>(box->buttonText.size()) ? box->buttonText[button].c_str() : "");
            RE::MessageBoxMenu::SelectOption(button);
        }

        class MenuSink : public RE::BSTEventSink<RE::MenuOpenCloseEvent>
        {
        public:
            RE::BSEventNotifyControl ProcessEvent(const RE::MenuOpenCloseEvent* a_event, RE::BSTEventSource<RE::MenuOpenCloseEvent>*) override
            {
                if (!started && a_event && a_event->opening && a_event->menuName == RE::MainMenu::MENU_NAME &&
                    std::filesystem::exists(kFlagFile)) {
                    started = loading = true;
                    SKSE::log::info("autoload: main menu, loading the newest save");
                    SKSE::GetTaskInterface()->AddTask([]() { LoadNewestSave(); });
                }
                if (loading && a_event && a_event->opening && a_event->menuName == RE::MessageBoxMenu::MENU_NAME) {
                    SKSE::GetTaskInterface()->AddTask([]() { AnswerMessageBox(); });
                }
                return RE::BSEventNotifyControl::kContinue;
            }
        };
    }

    void OnDataLoaded()
    {
        static MenuSink menuSink;
        if (auto* ui = RE::UI::GetSingleton()) {
            ui->AddEventSink<RE::MenuOpenCloseEvent>(&menuSink);
        }
    }

    void OnGameLoaded()
    {
        loading = false;
    }
}
