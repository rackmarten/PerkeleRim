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
    CHECK(all[2].name == "junk");
    CHECK(all[3].name == "drunk");  // the drunk ones last: MSC's camera sways on after them
    CHECK(all[4].name == "drunk-bottle");
    CHECK(all[5].name == "drunker-bottle");

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
    CHECK(HitTest::kScenarios[3].drunk > 0.0f);
    CHECK(HitTest::kScenarios[3].drunk < 2.5f);  // below where MSC's player can't stand still
    CHECK(HitTest::kScenarios[1].attack == HitTest::Attack::kThrow);
    CHECK(HitTest::kScenarios[1].drunk == 0.0f);
    CHECK(HitTest::kScenarios[2].attack == HitTest::Attack::kJunk);
    CHECK(HitTest::kScenarios[2].drunk == 0.0f);
    for (const std::size_t i : { 4u, 5u }) {
        CHECK(HitTest::kScenarios[i].attack == HitTest::Attack::kThrow);
        CHECK(HitTest::kScenarios[i].drunk > 0.0f);
        CHECK(HitTest::kScenarios[i].drunk < 2.5f);
    }
}

TEST_CASE("a drunk throw that sways past the target goes again, twice at most")
{
    const auto&  drunkBottle = HitTest::kScenarios[4];
    HitTest::Hit missed;
    CHECK(HitTest::Again(drunkBottle, missed, 0));
    CHECK(HitTest::Again(drunkBottle, missed, 1));
    CHECK_FALSE(HitTest::Again(drunkBottle, missed, 2));
    HitTest::Hit landed;
    landed.landed = true;
    CHECK_FALSE(HitTest::Again(drunkBottle, landed, 0));
    // the sober ones and the punches stay strict
    CHECK_FALSE(HitTest::Again(HitTest::kScenarios[1], missed, 0));
    CHECK_FALSE(HitTest::Again(HitTest::kScenarios[3], missed, 0));
}

TEST_CASE("a drunk throw passes by Damage::DrunkThrow, the level and the armor, a kill counts in full")
{
    const auto&  drunkBottle = HitTest::kScenarios[4];  // drunk 1
    HitTest::Hit hit;
    hit.landed = true;
    hit.unarmed = 4.0f;
    hit.speed = 20.0f;
    hit.drunk = 1.0f;
    hit.level = 10;
    hit.armor = 100.0f;
    hit.damage = 80.3f;  // a level 10 Nord's full-charge bottle at drunk 1: 16 x1.9 x3, 0.88 through the armor
    hit.before = 500.0f;
    hit.after = 419.7f;
    CHECK(HitTest::Expected(drunkBottle, hit) == doctest::Approx(80.256f));
    CHECK(HitTest::Check(drunkBottle, hit).pass);

    hit.before = 70.0f;  // a guard with less health than the hit: it stops at 0
    hit.after = 0.0f;
    const auto killed = HitTest::Check(drunkBottle, hit);
    CHECK(killed.pass);
    CHECK(killed.note == "killed it");

    hit.before = 200.0f;  // but not when the hit was smaller than its health
    hit.after = 0.0f;
    CHECK_FALSE(HitTest::Check(drunkBottle, hit).pass);

    hit.before = 500.0f;
    hit.after = 484.0f;
    hit.damage = 16.0f;  // the sober damage: Combat didn't apply the drunk level
    CHECK(HitTest::Check(drunkBottle, hit).note == "cast 16.0, damage.h says 80.3");

    hit.drunk = 0.2f;  // MSC never took the level
    CHECK(HitTest::Check(drunkBottle, hit).note == "MSC's drunk level was 0.2, not 1.0");
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
    const auto& drunk = HitTest::kScenarios[3];
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

TEST_CASE("thrown junk passes by its speed and weight")
{
    const auto&  junk = HitTest::kScenarios[2];
    HitTest::Hit hit;
    hit.landed = true;
    hit.unarmed = 4.0f;
    hit.speed = 20.0f;
    hit.mass = 2.0f;  // twice the bottle's hit
    hit.damage = 32.0f;
    hit.before = 300.0f;
    hit.after = 268.0f;
    CHECK(HitTest::Expected(junk, hit) == doctest::Approx(32.0f));
    CHECK(HitTest::Check(junk, hit).pass);

    hit.mass = 0.0f;  // MSC didn't say: the bottle's 16
    CHECK(HitTest::Check(junk, hit).note == "cast 32.0, damage.h says 16.0");
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
    CHECK(columns == 18);

    const auto& punch = HitTest::kScenarios[0];
    const auto  hit = Punched(4.0f, 0.0f);
    const auto  row = HitTest::Row(punch, 2, "Giant, \"Big\"", hit, HitTest::Check(punch, hit));
    CHECK(Fields(row) == columns);
    CHECK(row == "punch,2,\"Giant, \"\"Big\"\"\",1,0.0,4.0,0.0,0.0,1.0,1.0,0.0,8.0,8.0,100.0,92.0,8.0,1,");

    const auto summary = HitTest::SummaryRow("drunk vs punch", { false, "needs a landed punch, and a drunk one" });
    CHECK(Fields(summary) == columns);
    CHECK(summary == "drunk vs punch,,,,,,,,,,,,,,,,0,\"needs a landed punch, and a drunk one\"");
}

TEST_CASE("a stressed punch is expected a little harder")
{
    const auto&  sober = HitTest::kScenarios[0];
    HitTest::Hit hit;
    hit.landed = true;
    hit.unarmed = 4.0f;
    CHECK(HitTest::Expected(sober, hit) == doctest::Approx(8.0f));
    hit.stress = 200.0f;
    CHECK(HitTest::Expected(sober, hit) == doctest::Approx(9.6f));
}
