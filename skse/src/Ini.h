#pragma once

#include <fstream>
#include <string>
#include <string_view>

// Data/SKSE/Plugins/PerkeleRim.ini (Skyrim's folder is the working directory): Key=value lines,
// read where they're needed. Sections don't matter.
namespace PerkeleRim::Ini
{
    inline constexpr auto kPath = "Data/SKSE/Plugins/PerkeleRim.ini";

    inline std::string Trim(std::string a_text)
    {
        const auto first = a_text.find_first_not_of(" \t\r\n\"");
        const auto last = a_text.find_last_not_of(" \t\r\n\"");
        return first == std::string::npos ? std::string() : a_text.substr(first, last - first + 1);
    }

    // The value of the first a_key=<value> (UTF-8), empty when there is none.
    inline std::string Value(std::string_view a_key)
    {
        std::ifstream file(kPath);
        std::string   line;
        while (std::getline(file, line)) {
            const auto equals = line.find('=');
            if (equals != std::string::npos && Trim(line.substr(0, equals)) == a_key) {
                return Trim(line.substr(equals + 1));
            }
        }
        return {};
    }
}
