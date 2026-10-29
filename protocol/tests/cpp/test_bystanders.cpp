#include <doctest/doctest.h>

#include "../../cpp/bystanders.h"

using namespace PerkeleRim;

TEST_CASE("nobody talks to Kumpo in a vehicle, on foot at any speed")
{
    CHECK_FALSE(Bystanders::MayTalk(true));
    CHECK(Bystanders::MayTalk(false));
}

TEST_CASE("civilians flee, nobody else")
{
    CHECK(Bystanders::Flees({}));
    Bystanders::Person guard;
    guard.guard = true;
    CHECK_FALSE(Bystanders::Flees(guard));
    Bystanders::Person follower;
    follower.follower = true;
    CHECK_FALSE(Bystanders::Flees(follower));
    Bystanders::Person bandit;
    bandit.hostile = true;
    CHECK_FALSE(Bystanders::Flees(bandit));
    Bystanders::Person fighting;
    fighting.fighting = true;
    CHECK_FALSE(Bystanders::Flees(fighting));
    Bystanders::Person wolf;
    wolf.npc = false;
    CHECK_FALSE(Bystanders::Flees(wolf));
    Bystanders::Person giant;
    giant.huge = true;
    CHECK_FALSE(Bystanders::Flees(giant));
    Bystanders::Person dead;
    dead.dead = true;
    CHECK_FALSE(Bystanders::Flees(dead));
}

TEST_CASE("a flee starts once, renews now and then and ends after the vehicle stops coming")
{
    Bystanders::Fleeing fleeing;
    CHECK(fleeing.Start(7, 0.0));
    CHECK_FALSE(fleeing.Start(7, 1.0));  // still coming: no restart yet
    CHECK(fleeing.Start(7, Bystanders::kFleeRenew + 0.1));  // started over from where the vehicle is now
    CHECK(fleeing.Has(7));
    CHECK(fleeing.Expired(Bystanders::kFleeRenew + Bystanders::kFleeLasts).empty());
    const auto gone = fleeing.Expired(Bystanders::kFleeRenew + 0.1 + Bystanders::kFleeLasts);
    REQUIRE(gone.size() == 1);
    CHECK(gone[0] == 7);
    CHECK_FALSE(fleeing.Has(7));
    fleeing.Start(1, 0.0);
    fleeing.Start(2, 0.0);
    CHECK(fleeing.All().size() == 2);
    CHECK(fleeing.Count() == 0);
}
