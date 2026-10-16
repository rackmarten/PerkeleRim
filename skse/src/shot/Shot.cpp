#include "shot/Shot.h"

#include <chrono>
#include <ctime>
#include <filesystem>
#include <fstream>

#include "link/LinkService.h"
#include "screenshot.h"
#include "shot/BackBuffer.h"

namespace PerkeleRim::Shot
{
    namespace
    {
        constexpr auto kIniPath = "Data/SKSE/Plugins/PerkeleRim.ini";
        constexpr auto kIniKey = "ScreenshotDir";

        std::string Trim(std::string a_text)
        {
            const auto first = a_text.find_first_not_of(" \t\r\n\"");
            const auto last = a_text.find_last_not_of(" \t\r\n\"");
            return first == std::string::npos ? std::string() : a_text.substr(first, last - first + 1);
        }

        // ScreenshotDir=<path> from the ini (UTF-8, any section), empty when there is none.
        std::filesystem::path FromIni()
        {
            std::ifstream file(kIniPath);
            std::string   line;
            while (std::getline(file, line)) {
                const auto equals = line.find('=');
                if (equals == std::string::npos || Trim(line.substr(0, equals)) != kIniKey) {
                    continue;
                }
                const auto value = Trim(line.substr(equals + 1));
                if (!value.empty()) {
                    return std::filesystem::path(std::u8string(value.begin(), value.end()));
                }
            }
            return {};
        }

        // Documents/My Games/PerkeleRim/Screenshots: SKSE logs to Documents/My Games/<Skyrim>/SKSE.
        std::filesystem::path Folder()
        {
            if (auto folder = FromIni(); !folder.empty()) {
                return std::filesystem::absolute(folder);
            }
            const auto logs = SKSE::log::log_directory();
            return logs ? logs->parent_path().parent_path() / "PerkeleRim" / "Screenshots" : std::filesystem::path();
        }

        std::string Utf8(const std::filesystem::path& a_path)
        {
            const auto text = a_path.u8string();
            return std::string(reinterpret_cast<const char*>(text.data()), text.size());
        }

        void Saved(const std::wstring& a_path, long a_result)
        {
            const auto name = Utf8(std::filesystem::path(a_path).filename());
            if (a_result != 0) {
                SKSE::log::warn("shot: could not save Skyrim's frame to {} (HRESULT {:08X})", name, static_cast<std::uint32_t>(a_result));
                return;
            }
            SKSE::log::info("shot: Skyrim's frame saved to {}", name);
            // After the frame is taken, so the message isn't in it.
            SKSE::GetTaskInterface()->AddTask([] { RE::SendHUDMessage::ShowHUDMessage("Screenshot saved"); });
        }
    }

    void Take()
    {
        const auto folder = Folder();
        std::error_code error;
        if (folder.empty() || (std::filesystem::create_directories(folder, error), error)) {
            SKSE::log::warn("shot: no screenshot folder ({})", error ? error.message() : "no log folder");
            return;
        }

        const auto now = std::chrono::system_clock::to_time_t(std::chrono::system_clock::now());
        std::tm    local{};
        localtime_s(&local, &now);
        std::string name;
        for (int take = 1; take < 100; take++) {
            name = Screenshot::Name(local, take);
            if (!std::filesystem::exists(folder / (name + "_skyrim.png"), error)) {
                break;
            }
        }

        const auto skyrim = folder / (name + "_skyrim.png");
        const auto msc = folder / (name + "_msc.png");
        auto*      window = RE::BSGraphics::Renderer::GetCurrentRenderWindow();
        if (!BackBuffer::Request(window ? window->swapChain : nullptr, skyrim.wstring(), &Saved)) {
            SKSE::log::warn("shot: Skyrim's frame not taken (no swap chain, or the last shot is still waiting)");
        }

        auto&      link = LinkService::Get();
        const auto payload = Screenshot::Encode(Utf8(msc));
        if (payload && link.IsUp() && link.Send(Protocol::MessageType::ScreenshotEvent, payload->data(), static_cast<std::uint32_t>(payload->size()))) {
            SKSE::log::info("shot: {} asked of MSC in {}", Utf8(msc.filename()), Utf8(folder));
        } else {
            SKSE::log::warn("shot: MSC's view not asked for (not linked, or the path is too long)");
        }
    }
}
