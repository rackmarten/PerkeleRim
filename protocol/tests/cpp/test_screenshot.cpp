#include <doctest/doctest.h>

#include <ostream>
#include <string>

#include "../../cpp/screenshot.h"

using namespace PerkeleRim;
using namespace PerkeleRim::Protocol;

TEST_CASE("a screenshot request carries MSC's file path")
{
    const std::string path = "C:\\Users\\me\\Documents\\My Games\\PerkeleRim\\Screenshots\\2026-10-17_14-03-22_msc_P\xC3\xA4ivi.png";
    const auto        payload = Screenshot::Encode(path);
    REQUIRE(payload.has_value());
    CHECK(payload->size() == sizeof(ScreenshotEvent) + path.size());
    const auto decoded = Screenshot::Decode(payload->data(), (std::uint32_t)payload->size());
    REQUIRE(decoded.has_value());
    CHECK(*decoded == path);
}

TEST_CASE("malformed screenshot requests are refused")
{
    CHECK_FALSE(Screenshot::Encode("").has_value());
    CHECK_FALSE(Screenshot::Encode(std::string(kScreenshotPathMax + 1, 'a')).has_value());
    CHECK(Screenshot::Encode(std::string(kScreenshotPathMax, 'a')).has_value());

    const auto ok = *Screenshot::Encode("shot.png");
    CHECK_FALSE(Screenshot::Decode(ok.data(), 4).has_value());                               // no full header
    CHECK_FALSE(Screenshot::Decode(ok.data(), (std::uint32_t)ok.size() - 1).has_value());  // path cut short
    CHECK_FALSE(Screenshot::Decode(nullptr, 100).has_value());
}

TEST_CASE("a screenshot pair is named by its local time")
{
    std::tm time{};
    time.tm_year = 2026 - 1900;
    time.tm_mon = 9;
    time.tm_mday = 7;
    time.tm_hour = 4;
    time.tm_min = 3;
    time.tm_sec = 2;
    CHECK(Screenshot::Name(time) == "2026-10-07_04-03-02");
    CHECK(Screenshot::Name(time, 2) == "2026-10-07_04-03-02_2");
}
