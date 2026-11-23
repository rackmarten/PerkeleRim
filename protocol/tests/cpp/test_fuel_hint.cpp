#include <doctest/doctest.h>

#include <ostream>  // doctest prints string_views through it
#include <string>
#include <vector>

#include "../../cpp/fuel_hint.h"

using namespace PerkeleRim;

namespace
{
    // The four stations PerkeleRim.esp places (tools/esp-gen), near enough.
    const std::vector<FuelHint::Pump> kPumps = {
        { 13177.0f, -13572.0f, "Whiterun Dwemer Oil" },
        { -97272.0f, 76515.0f, "Dragon Bridge Dwemer Oil" },
        { 138738.0f, 26398.0f, "Windhelm Dwemer Oil" },
        { 82640.0f, -74393.0f, "Ivarstead Dwemer Oil" },
    };

    constexpr float kKm = 1000.0f / 0.0142875f;  // units in a kilometre
}

TEST_CASE("the nearest pump on the ground plane")
{
    CHECK(FuelHint::Nearest(kPumps, 20000.0f, -5000.0f) == 0);    // Whiterun, from the garage road
    CHECK(FuelHint::Nearest(kPumps, -80000.0f, 60000.0f) == 1);   // Dragon Bridge, from Solitude's side
    CHECK(FuelHint::Nearest(kPumps, 120000.0f, 40000.0f) == 2);   // Windhelm
    CHECK(FuelHint::Nearest(kPumps, 90000.0f, -90000.0f) == 3);   // Ivarstead, from Riften
    CHECK(FuelHint::Nearest(kPumps, 13177.0f, -13572.0f) == 0);   // standing on it
    CHECK(FuelHint::Nearest({}, 0.0f, 0.0f) == -1);
}

TEST_CASE("bearings are Skyrim's compass: north is +Y, clockwise")
{
    CHECK(FuelHint::Bearing(0.0, 1.0) == doctest::Approx(0.0));
    CHECK(FuelHint::Bearing(1.0, 1.0) == doctest::Approx(45.0));
    CHECK(FuelHint::Bearing(1.0, 0.0) == doctest::Approx(90.0));
    CHECK(FuelHint::Bearing(0.0, -1.0) == doctest::Approx(180.0));
    CHECK(FuelHint::Bearing(-1.0, 0.0) == doctest::Approx(270.0));
    CHECK(FuelHint::Bearing(-1.0, 1.0) == doctest::Approx(315.0));

    CHECK(FuelHint::Direction(0.0) == "north");
    CHECK(FuelHint::Direction(22.4) == "north");
    CHECK(FuelHint::Direction(22.6) == "north-east");
    CHECK(FuelHint::Direction(90.0) == "east");
    CHECK(FuelHint::Direction(135.0) == "south-east");
    CHECK(FuelHint::Direction(180.0) == "south");
    CHECK(FuelHint::Direction(225.0) == "south-west");
    CHECK(FuelHint::Direction(270.0) == "west");
    CHECK(FuelHint::Direction(315.0) == "north-west");
    CHECK(FuelHint::Direction(350.0) == "north");
    CHECK(FuelHint::Direction(359.9) == "north");
}

TEST_CASE("distances read in metres, then kilometres")
{
    CHECK(FuelHint::Distance(0.35f * kKm) == "350 m");
    CHECK(FuelHint::Distance(0.123f * kKm) == "120 m");
    CHECK(FuelHint::Distance(0.994f * kKm) == "990 m");
    CHECK(FuelHint::Distance(0.996f * kKm) == "1.0 km");
    CHECK(FuelHint::Distance(2.34f * kKm) == "2.3 km");
    CHECK(FuelHint::Distance(12.0f * kKm) == "12.0 km");
    CHECK(FuelHint::Distance(1.96f * kKm) == "2.0 km");  // the tenths carry over
}

TEST_CASE("where the nearest pump is: name, distance and direction")
{
    // 0.9 km west and 0.9 km south of the Whiterun pump: it's 1.3 km north-east.
    const float d = 0.9f * kKm;
    CHECK(FuelHint::Where(kPumps, 13177.0f - d, -13572.0f - d) == "Whiterun Dwemer Oil 1.3 km north-east");
    // 500 m south of Windhelm's: north.
    CHECK(FuelHint::Where(kPumps, 138738.0f, 26398.0f - 0.5f * kKm) == "Windhelm Dwemer Oil 500 m north");
    // Within 50 m it's right here; 5000 units (71 m) isn't.
    CHECK(FuelHint::Where(kPumps, 13177.0f - 3000.0f, -13572.0f) == "Whiterun Dwemer Oil right here");
    CHECK(FuelHint::Where(kPumps, 13177.0f - 5000.0f, -13572.0f) == "Whiterun Dwemer Oil 70 m east");
    // No names (an ESP without the marker list) and no pumps.
    const std::vector<FuelHint::Pump> unnamed = { { 0.0f, 0.0f, "" } };
    CHECK(FuelHint::Where(unnamed, 0.0f, -kKm) == "Dwemer Oil 1.0 km north");
    CHECK(FuelHint::Where({}, 0.0f, 0.0f).empty());
}

TEST_CASE("the low-fuel line with the pump after it")
{
    CHECK(FuelHint::LowLine("Fuel 8.3 / 40 L", "Whiterun Dwemer Oil 2.3 km north-east") == "Fuel 8.3 / 40 L. Whiterun Dwemer Oil 2.3 km north-east");
    CHECK(FuelHint::LowLine("Fuel 8.3 / 40 L", "") == "Fuel 8.3 / 40 L");
    CHECK(FuelHint::LowLine("", "Whiterun Dwemer Oil right here") == "Whiterun Dwemer Oil right here");
}

TEST_CASE("the dry tip names the tow fee")
{
    CHECK(FuelHint::DryTip() == "Out of oil. Walk to a Dwemer Oil pump and call the vehicle: it's towed there for 50 gold.");
}
