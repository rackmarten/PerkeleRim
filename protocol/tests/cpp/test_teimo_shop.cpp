#include <doctest/doctest.h>

#include "../../cpp/teimo_shop.h"
#include "../../cpp/teimo_talk.h"

#include <cmath>
#include <ostream>
#include <set>
#include <string>

using namespace PerkeleRim;
using Teimo::Talk;
using Teimo::Topic;

TEST_CASE("the stall's frame: +y toward the customers, +x to Teimo's right, as esp-gen places its pieces")
{
    const Teimo::Point at{ 1000.0f, 2000.0f, -100.0f };
    // Facing north (0): forward is +Y, right is +X.
    auto p = Teimo::StallToWorld(at, 0.0f, 10.0f, 20.0f, 30.0f);
    CHECK(p.x == doctest::Approx(1010.0f));
    CHECK(p.y == doctest::Approx(2020.0f));
    CHECK(p.z == doctest::Approx(-70.0f));
    // Facing east (pi/2): forward is +X, right is -Y.
    p = Teimo::StallToWorld(at, 1.5707963f, 10.0f, 20.0f, 0.0f);
    CHECK(p.x == doctest::Approx(1020.0f).epsilon(0.001));
    CHECK(p.y == doctest::Approx(1990.0f).epsilon(0.001));
}

TEST_CASE("the stall's message has every spot in world space, the counter and its box")
{
    const Teimo::Point at{ 24960.0f, -41990.0f, -171.0f };
    const float        heading = 5.06f;
    const auto         stall = Teimo::MakeStall(7, at, heading);
    CHECK(stall.generation == 7);
    CHECK(stall.count == Teimo::kStallSpots.size());
    CHECK(stall.count <= Protocol::kStallGoodsMax);
    CHECK(stall.teimo[0] == at.x);
    CHECK(stall.heading == heading);
    const auto counter = Teimo::StallToWorld(at, heading, Teimo::kCounterTop.x, Teimo::kCounterTop.y, Teimo::kCounterTop.z);
    CHECK(stall.counter[0] == doctest::Approx(counter.x));
    CHECK(stall.counter[1] == doctest::Approx(counter.y));
    CHECK(stall.counterHalf[0] == Teimo::kCounterTop.halfX);
    std::set<std::uint32_t> products;
    for (std::uint32_t i = 0; i < stall.count; i++) {
        const auto& good = stall.goods[i];
        const auto& spot = Teimo::kStallSpots[i];
        products.insert(good.product);
        CHECK(good.product < Protocol::kShopProducts);
        const auto w = Teimo::StallToWorld(at, heading, spot.x, spot.y, spot.z);
        CHECK(good.position[0] == doctest::Approx(w.x));
        CHECK(good.position[2] == doctest::Approx(w.z));
        CHECK(good.heading >= 0.0f);
        CHECK(good.heading < 6.2832f);
        // No good is set out on the counter, where goods are paid for.
        const bool onCounter = std::abs(spot.x - Teimo::kCounterTop.x) <= Teimo::kCounterTop.halfX &&
                               std::abs(spot.y - Teimo::kCounterTop.y) <= Teimo::kCounterTop.halfY &&
                               spot.z >= Teimo::kCounterTop.z - 20.0f && spot.z <= Teimo::kCounterTop.z + Teimo::kCounterTop.halfZ;
        CHECK_FALSE(onCounter);
    }
    CHECK(products.count(Protocol::kShopBeer) == 1);  // beer, the acceptance's case, is on sale
    CHECK(products.size() == Protocol::kShopProducts);  // and every product the stall knows
}

TEST_CASE("walking off with goods costs their price in whole gold, at least 1")
{
    CHECK(Teimo::TheftBounty(149.0f) == 149);
    CHECK(Teimo::TheftBounty(149.2f) == 150);
    CHECK(Teimo::TheftBounty(13.95f + 7.579001f) == 22);
    CHECK(Teimo::TheftBounty(0.3f) == 1);
    CHECK(Teimo::TheftBounty(0.0f) == 1);
}

TEST_CASE("the bill on the HUD shows MSC's marks as gold")
{
    CHECK(Teimo::Marks(149.0f) == "149");
    CHECK(Teimo::Marks(15.0891f) == "15.09");
    CHECK(Teimo::Marks(20.6011f) == "20.60");
    CHECK(Teimo::Marks(7.05f) == "7.05");
    CHECK(Teimo::BillNote(0, 0.0f).empty());
    CHECK(Teimo::BillNote(1, 149.0f) == "1 thing on Teimo's counter: 149 gold. Talk to him to pay.");
    CHECK(Teimo::BillNote(3, 177.68f) == "3 things on Teimo's counter: 177.68 gold. Talk to him to pay.");
}

TEST_CASE("paying gets his thanks over his small talk, and no goodbye after")
{
    Talk talk;
    REQUIRE(talk.Tick(0.0, 200.0f, 0.0f) == Topic::kGreet);
    talk.Say(Topic::kGreet, 0.0, 0.0);
    // Small talk later, then Kumpo pays in the middle of a story: the thanks cut in.
    double t = 0.1;
    for (; t < 30.0; t += 0.1) {
        if (const auto topic = talk.Tick(t, 200.0f, 0.0f)) {
            CHECK(*topic == Topic::kChat);
            talk.Say(*topic, t, 0.0);
            break;
        }
    }
    REQUIRE(talk.Speaking());
    const auto paid = talk.OnShop(Topic::kPaid, t + 1.0);
    REQUIRE(paid.has_value());
    const auto* line = talk.Say(*paid, t + 1.0, 0.0);
    REQUIRE(line != nullptr);
    CHECK(line->topic == Topic::kPaid);
    // Walking off after: thanked already, no goodbye.
    for (double u = t + 2.0; u < t + 20.0; u += 0.1) {
        if (const auto topic = talk.Tick(u, 700.0f, 0.0f)) {
            CHECK(*topic != Topic::kBye);
        }
    }
}

TEST_CASE("short of money he says so, but not over an insult's reply")
{
    Talk talk;
    REQUIRE(talk.OnInsult(Topic::kFinger, 0.0, 200.0f) == Topic::kFinger);
    talk.Say(Topic::kFinger, 0.0, 0.0);
    CHECK_FALSE(talk.OnShop(Topic::kShort, 0.5).has_value());
    CHECK(talk.OnShop(Topic::kShort, 20.0) == Topic::kShort);
    const auto* line = talk.Say(Topic::kShort, 20.0, 0.0);
    REQUIRE(line != nullptr);
    CHECK(line->clip == "ei_tarpeeksi_rahaa");
}

TEST_CASE("walking off with goods is an insult: he shouts once, over his small talk")
{
    Talk talk;
    talk.Say(Topic::kChat, 0.0, 0.0);
    REQUIRE(talk.Speaking());
    CHECK(Talk::IsInsult(Topic::kStolen));
    CHECK(talk.OnInsult(Topic::kStolen, 1.0, 600.0f) == Topic::kStolen);
    talk.Say(Topic::kStolen, 1.0, 0.0);
    CHECK_FALSE(talk.OnInsult(Topic::kStolen, 2.0, 600.0f).has_value());  // a second good a moment later: one shout
}

TEST_CASE("each shop topic has lines, beer bought drunk has its own")
{
    for (auto topic : { Topic::kPaid, Topic::kDrunkBuy, Topic::kShort, Topic::kStolen }) {
        CHECK(Talk::Count(topic) > 0);
    }
    for (const auto& line : Teimo::kLines) {
        if (line.topic == Topic::kDrunkBuy) {
            CHECK(line.clip.substr(0, 12) == "tilaus_kanni");
        }
    }
}
