#include <doctest/doctest.h>

#include <ostream>
#include <string>

#include "../../cpp/hit_test.h"

using namespace PerkeleRim;

namespace
{
    int Fields(const std::string& a_row)
    {
        int  fields = 1;
        bool quoted = false;
        for (const char c : a_row) {
            if (c == '"') {
                quoted = !quoted;
            } else if (c == ',' && !quoted) {
                fields++;
            }
        }
        return fields;
    }

    HitTest::Hit Punched(float a_unarmed, float a_drunk, float a_before = 100.0f)
    {
        HitTest::Hit hit;
        hit.landed = true;
        hit.unarmed = a_unarmed;
        hit.drunk = a_drunk;
        hit.damage = Damage::Punch(a_unarmed, a_drunk);
        hit.before = a_before;
        hit.after = a_before - hit.damage;
        return hit;
    }
}

TEST_CASE("the hit test's scenarios come from the ini's list")
{
    const auto all = HitTest::Parse("all");
    REQUIRE(all.size() == HitTest::kScenarios.size());
    CHECK(all[0].name == "punch");
    CHECK(all[1].name == "bottle");
    CHECK(all[2].name == "drunk");  // last: MSC's camera sways on after it

    std::vector<std::string> unknown;
    const auto picked = HitTest::Parse(" bottle , punch,,run-over\r", &unknown);
    REQUIRE(picked.size() == 2);
    CHECK(picked[0].name == "bottle");
    CHECK(picked[1].name == "punch");
    REQUIRE(unknown.size() == 1);
    CHECK(unknown[0] == "run-over");

    CHECK(HitTest::Parse("").empty());
    CHECK(HitTest::Parse("Punch").empty());  // names are lower case
}

TEST_CASE("the scenarios cover sober and drunk punches and a throw")
{
    for (const auto& scenario : HitTest::kScenarios) {
        CHECK(scenario.hits > 0);
        CHECK(scenario.gap > 0.0f);
    }
    CHECK(HitTest::kScenarios[0].drunk == 0.0f);
    CHECK(HitTest::kScenarios[2].drunk > 0.0f);
    CHECK(HitTest::kScenarios[2].drunk < 2.5f);  // below where MSC's player can't stand still
    CHECK(HitTest::kScenarios[1].attack == HitTest::Attack::kThrow);
    CHECK(HitTest::kScenarios[1].drunk == 0.0f);
}

TEST_CASE("a punch that landed passes when Skyrim cast and took what damage.h says")
{
    const auto& punch = HitTest::kScenarios[0];
    CHECK(HitTest::Check(punch, Punched(4.0f, 0.0f)).pass);

    auto off = Punched(4.0f, 0.0f);
    off.damage = 12.0f;  // not Punch(4)
    off.after = off.before - off.damage;
    const auto wrong = HitTest::Check(punch, off);
    CHECK_FALSE(wrong.pass);
    CHECK(wrong.note == "cast 12.0, damage.h says 8.0");

    auto resisted = Punched(4.0f, 0.0f);
    resisted.after = resisted.before - 2.0f;
    CHECK(HitTest::Check(punch, resisted).note == "health fell by 2.0, not the 8.0 cast");

    auto regen = Punched(4.0f, 0.0f);
    regen.after += 0.6f;  // a little health came back between the reads
    CHECK(HitTest::Check(punch, regen).pass);

    HitTest::Hit missed;
    CHECK(HitTest::Check(punch, missed).note == "no punch reached Skyrim");
}

TEST_CASE("a drunk punch fails when MSC didn't take the drunk level")
{
    const auto& drunk = HitTest::kScenarios[2];
    CHECK(HitTest::Check(drunk, Punched(4.0f, 2.0f)).pass);
    CHECK(HitTest::Check(drunk, Punched(4.0f, 1.2f)).pass);  // MSC's level moves on its own a little
    CHECK(HitTest::Check(drunk, Punched(4.0f, 0.3f)).note == "MSC's drunk level was 0.3, not 2.0");
}

TEST_CASE("a throw passes by its speed, harmless ones fail")
{
    const auto&  bottle = HitTest::kScenarios[1];
    HitTest::Hit hit;
    hit.landed = true;
    hit.unarmed = 4.0f;
    hit.speed = 20.0f;  // a bottle, past kThrownSpeedFull: twice a sober punch
    hit.damage = 16.0f;
    hit.before = 300.0f;
    hit.after = 284.0f;
    CHECK(HitTest::Expected(bottle, hit) == doctest::Approx(16.0f));
    CHECK(HitTest::Check(bottle, hit).pass);

    hit.sneak = 2.0f;
    CHECK(HitTest::Expected(bottle, hit) == doctest::Approx(32.0f));
    CHECK_FALSE(HitTest::Check(bottle, hit).pass);

    hit.sneak = 1.0f;
    hit.speed = 1.0f;
    CHECK(HitTest::Check(bottle, hit).note == "harmless: thrown at 1.0 m/s");
}

TEST_CASE("drunk punches against sober ones")
{
    CHECK(HitTest::CheckDrunk(8.0f, Damage::Punch(4.0f, 2.0f), 2.0f).pass);
    CHECK(HitTest::CheckDrunk(8.0f, Damage::Punch(4.0f, 2.0f), 2.0f).note == "x1.7 at drunk 2.0");
    CHECK_FALSE(HitTest::CheckDrunk(8.0f, 8.0f, 2.0f).pass);
    CHECK_FALSE(HitTest::CheckDrunk(0.0f, 8.0f, 2.0f).pass);
}

TEST_CASE("the CSV rows match the header")
{
    const int columns = Fields(std::string(HitTest::kCsvHeader));
    CHECK(columns == 15);

    const auto& punch = HitTest::kScenarios[0];
    const auto  hit = Punched(4.0f, 0.0f);
    const auto  row = HitTest::Row(punch, 2, "Giant, \"Big\"", hit, HitTest::Check(punch, hit));
    CHECK(Fields(row) == columns);
    CHECK(row == "punch,2,\"Giant, \"\"Big\"\"\",1,0.0,4.0,0.0,1.0,8.0,8.0,100.0,92.0,8.0,1,");

    const auto summary = HitTest::SummaryRow("drunk vs punch", { false, "needs a landed punch, and a drunk one" });
    CHECK(Fields(summary) == columns);
    CHECK(summary == "drunk vs punch,,,,,,,,,,,,,0,\"needs a landed punch, and a drunk one\"");
}
