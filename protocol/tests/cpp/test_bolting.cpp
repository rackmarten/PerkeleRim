#include <ostream>
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
    CHECK_FALSE(LookAt::Claims("spanner 10mm", Protocol::kLookAtPartName));  // MSC's part name line isn't a prompt
    CHECK(LookAt::Claims("door left", Protocol::kLookAtPartName | Protocol::kLookAtDisassemble));
}

TEST_CASE("the assemble flags survive the wire")
{
    const auto payload = LookAt::Encode("door left", Protocol::kLookAtDisassemble);
    const auto prompt = LookAt::Decode(payload.data(), static_cast<std::uint32_t>(payload.size()));
    REQUIRE(prompt.has_value());
    CHECK(prompt->text == "door left");
    CHECK(LookAt::IconFor(prompt->flags) == LookAt::Icon::Disassemble);
}

#include "../../cpp/parts.h"

namespace
{
    Parts::Part Door()
    {
        Parts::Part p;
        p.name = "Door_Left";
        p.flags = Protocol::kPartInstalled | Protocol::kPartPlaced;
        p.generation = 7;
        p.position[0] = 15170.0f;
        p.position[1] = -14516.0f;
        p.position[2] = -4600.0f;
        p.bolts = 4;
        p.boltsTight = 1;
        p.stages = 13;
        p.tightness = 13.0f;
        p.tightnessFull = 28.0f;
        return p;
    }
}

TEST_CASE("a part's state survives the wire")
{
    const auto payload = Parts::Encode(Door());
    CHECK(payload.size() == sizeof(Protocol::PartState));
    const auto p = Parts::Decode(payload.data(), static_cast<std::uint32_t>(payload.size()));
    REQUIRE(p.has_value());
    CHECK(p->name == "Door_Left");
    CHECK(p->Installed());
    CHECK_FALSE(p->Bolted());
    CHECK(p->Placed());
    CHECK(p->generation == 7);
    CHECK(p->position[1] == doctest::Approx(-14516.0f));
    CHECK(p->bolts == 4);
    CHECK(p->boltsTight == 1);
    CHECK(p->stages == 13);
    CHECK(p->tightnessFull == doctest::Approx(28.0f));
    CHECK_FALSE(p->AllTight());
    CHECK_FALSE(p->BoltsOut());
}

TEST_CASE("malformed part states are refused")
{
    auto payload = Parts::Encode(Door());
    CHECK_FALSE(Parts::Decode(payload.data(), static_cast<std::uint32_t>(payload.size()) - 1).has_value());
    CHECK_FALSE(Parts::Decode(nullptr, 80).has_value());

    auto nameless = Door();
    nameless.name.clear();
    auto bytes = Parts::Encode(nameless);
    CHECK_FALSE(Parts::Decode(bytes.data(), static_cast<std::uint32_t>(bytes.size())).has_value());

    auto tooTight = Door();
    tooTight.boltsTight = 5;
    bytes = Parts::Encode(tooTight);
    CHECK_FALSE(Parts::Decode(bytes.data(), static_cast<std::uint32_t>(bytes.size())).has_value());

    auto tooManyStages = Door();
    tooManyStages.stages = 33;  // 4 bolts, 8 stages each
    bytes = Parts::Encode(tooManyStages);
    CHECK_FALSE(Parts::Decode(bytes.data(), static_cast<std::uint32_t>(bytes.size())).has_value());

    Protocol::PartState m{};
    std::memcpy(&m, payload.data(), sizeof(m));
    m.length = Protocol::kPartNameMax + 1;
    std::memcpy(payload.data(), &m, sizeof(m));
    CHECK_FALSE(Parts::Decode(payload.data(), static_cast<std::uint32_t>(payload.size())).has_value());
}

TEST_CASE("a long part name is cut at a character")
{
    auto p = Door();
    p.name = std::string(Protocol::kPartNameMax - 1, 'a') + "\xC3\xA4";  // the last character doesn't fit whole
    const auto payload = Parts::Encode(p);
    const auto back = Parts::Decode(payload.data(), static_cast<std::uint32_t>(payload.size()));
    REQUIRE(back.has_value());
    CHECK(back->name == std::string(Protocol::kPartNameMax - 1, 'a'));
}

TEST_CASE("parts are found by any spelling of their name")
{
    CHECK(Parts::Key("Door_Left") == "doorleft");
    CHECK(Parts::Key("door left") == "doorleft");
    CHECK(Parts::Key(" DOOR-left ") == "doorleft");

    Parts::Table table;
    CHECK(table.Apply(Door()));
    auto right = Door();
    right.name = "Door_Right";
    right.flags = Protocol::kPartPlaced;
    CHECK(table.Apply(right));
    auto radiator = Door();
    radiator.name = "Radiator";
    CHECK(table.Apply(radiator));

    REQUIRE(table.Find("door left") != nullptr);
    CHECK(table.Find("door left")->name == "Door_Left");
    CHECK(table.Find("DOOR_RIGHT")->name == "Door_Right");
    CHECK(table.Find("radi")->name == "Radiator");  // the only one starting so
    CHECK(table.Find("door") == nullptr);           // two do: say which
    CHECK(table.Find("hood") == nullptr);
    CHECK(table.Find("") == nullptr);
    CHECK(table.Size() == 3);
    CHECK(table.Installed() == 2);
}

TEST_CASE("the table says when a part changed, not when it only moved, and forgets gone ones")
{
    Parts::Table table;
    auto door = Door();
    CHECK(table.Apply(door));
    door.position[0] += 100.0f;
    CHECK_FALSE(table.Apply(door));
    CHECK(table.Find("Door_Left")->position[0] == doctest::Approx(15270.0f));  // kept anyway
    door.boltsTight = 4;
    door.stages = 32;
    door.flags |= Protocol::kPartBolted;
    CHECK(table.Apply(door));
    CHECK(table.Find("Door_Left")->AllTight());
    door.flags = Protocol::kPartGone;
    CHECK(table.Apply(door));
    CHECK(table.Find("Door_Left") == nullptr);
    CHECK_FALSE(table.Apply(door));  // already gone
}

TEST_CASE("a part with no bolts is never all tight, and has its bolts out")
{
    Parts::Part p;
    p.name = "Hubcap1";
    CHECK_FALSE(p.AllTight());
    CHECK(p.BoltsOut());
}
