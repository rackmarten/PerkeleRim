#include <doctest/doctest.h>

#include <algorithm>

#include "../../cpp/part_set.h"

using namespace PerkeleRim;

namespace
{
    Protocol::PartSet Set(std::initializer_list<std::pair<std::uint32_t, std::uint32_t>> a_parts)
    {
        Protocol::PartSet set{};
        set.vehicle = Protocol::kVehicleSatsuma;
        set.generation = 3;
        for (const auto& [part, model] : a_parts) {
            set.parts[set.count].part = part;
            set.parts[set.count].model = model;
            set.parts[set.count].flags = Protocol::kPartVisible;
            set.count++;
        }
        return set;
    }

    bool Has(const std::vector<std::uint32_t>& a_list, std::uint32_t a_value)
    {
        return std::find(a_list.begin(), a_list.end(), a_value) != a_list.end();
    }
}

TEST_CASE("a bare body has no parts to build")
{
    const auto changes = PartSets::Diff({}, Set({}));
    CHECK(changes.build.empty());
    CHECK(changes.gone.empty());
}

TEST_CASE("every part of a first set is built")
{
    const auto changes = PartSets::Diff({}, Set({ { 10, 100 }, { 11, 101 }, { 12, 100 } }));
    CHECK(changes.build == std::vector<std::uint32_t>{ 0, 1, 2 });  // two wheels share a model, each its own node
    CHECK(changes.gone.empty());
}

TEST_CASE("a part going on builds only that part")
{
    const PartSets::Built built{ { 10, 100 }, { 11, 101 } };
    const auto            changes = PartSets::Diff(built, Set({ { 10, 100 }, { 11, 101 }, { 12, 102 } }));
    CHECK(changes.build == std::vector<std::uint32_t>{ 2 });
    CHECK(changes.gone.empty());
}

TEST_CASE("a part coming off takes down only that part")
{
    const PartSets::Built built{ { 10, 100 }, { 11, 101 }, { 12, 102 } };
    const auto            changes = PartSets::Diff(built, Set({ { 10, 100 }, { 12, 102 } }));
    CHECK(changes.build.empty());
    CHECK(changes.gone == std::vector<std::uint32_t>{ 11 });
}

TEST_CASE("a part that looks different now is built again")
{
    const PartSets::Built built{ { 10, 100 }, { 11, 101 } };
    const auto            changes = PartSets::Diff(built, Set({ { 10, 100 }, { 11, 105 } }));
    CHECK(changes.build == std::vector<std::uint32_t>{ 1 });
    CHECK(changes.gone.empty());
}

TEST_CASE("a part not read yet has nothing to draw")
{
    CHECK(PartSets::Diff({}, Set({ { 10, 0 } })).build.empty());
    const PartSets::Built built{ { 10, 100 } };
    const auto            changes = PartSets::Diff(built, Set({ { 10, 0 } }));
    CHECK(changes.build.empty());
    CHECK(changes.gone == std::vector<std::uint32_t>{ 10 });
}

TEST_CASE("ids 0 and a part named twice count once")
{
    const auto changes = PartSets::Diff({}, Set({ { 0, 100 }, { 10, 100 }, { 10, 101 } }));
    CHECK(changes.build == std::vector<std::uint32_t>{ 1 });
}

TEST_CASE("a count past the capacity reads only the capacity")
{
    Protocol::PartSet set{};
    set.count = Protocol::kPartSetCapacity + 50;
    for (std::uint32_t i = 0; i < Protocol::kPartSetCapacity; i++) {
        set.parts[i] = { i + 1, 7, Protocol::kPartVisible, { 0, 0, 0, 1 }, { 0, 0, 0 } };
    }
    CHECK(PartSets::Diff({}, set).build.size() == Protocol::kPartSetCapacity);
}

TEST_CASE("all parts gone when the car is stripped")
{
    const PartSets::Built built{ { 10, 100 }, { 11, 101 } };
    const auto            changes = PartSets::Diff(built, Set({}));
    CHECK(changes.build.empty());
    CHECK(changes.gone.size() == 2);
    CHECK(Has(changes.gone, 10));
    CHECK(Has(changes.gone, 11));
}

TEST_CASE("a set is for one vehicle on one island")
{
    const auto set = Set({ { 10, 100 } });
    CHECK(PartSets::For(set, Protocol::kVehicleSatsuma, 3));
    CHECK_FALSE(PartSets::For(set, Protocol::kVehicleSatsuma, 4));
    CHECK_FALSE(PartSets::For(set, Protocol::kVehicleHayosiko, 3));
}
