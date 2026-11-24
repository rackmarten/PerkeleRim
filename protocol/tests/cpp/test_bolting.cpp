#include <doctest/doctest.h>

#include "../../cpp/look_at.h"

using namespace PerkeleRim;

TEST_CASE("MSC's assemble icon wins over its hand, then the disassemble icon")
{
    using LookAt::Icon;
    CHECK(LookAt::IconFor(0) == Icon::None);
    CHECK(LookAt::IconFor(Protocol::kLookAtUse) == Icon::Hand);
    CHECK(LookAt::IconFor(Protocol::kLookAtAssemble) == Icon::Assemble);
    CHECK(LookAt::IconFor(Protocol::kLookAtDisassemble) == Icon::Disassemble);
    CHECK(LookAt::IconFor(Protocol::kLookAtUse | Protocol::kLookAtAssemble) == Icon::Assemble);
    CHECK(LookAt::IconFor(Protocol::kLookAtUse | Protocol::kLookAtDisassemble) == Icon::Disassemble);
    CHECK(LookAt::IconFor(Protocol::kLookAtAssemble | Protocol::kLookAtDisassemble) == Icon::Assemble);
    CHECK(LookAt::IconFor(0x80) == Icon::None);  // a flag this side doesn't know shows nothing
}

TEST_CASE("any of MSC's text or icons keeps E and the click with MSC")
{
    CHECK_FALSE(LookAt::Claims("", 0));
    CHECK(LookAt::Claims("IGNITION", 0));
    CHECK(LookAt::Claims("", Protocol::kLookAtUse));
    CHECK(LookAt::Claims("", Protocol::kLookAtAssemble));
    CHECK(LookAt::Claims("", Protocol::kLookAtDisassemble));
    CHECK_FALSE(LookAt::Claims("", 0x80));
}

TEST_CASE("the assemble flags survive the wire")
{
    const auto payload = LookAt::Encode("door left", Protocol::kLookAtDisassemble);
    const auto prompt = LookAt::Decode(payload.data(), static_cast<std::uint32_t>(payload.size()));
    REQUIRE(prompt.has_value());
    CHECK(prompt->text == "door left");
    CHECK(LookAt::IconFor(prompt->flags) == LookAt::Icon::Disassemble);
}
