#include <doctest/doctest.h>

#include <cmath>
#include <fstream>
#include <sstream>
#include <string>
#include <vector>

#include "../../cpp/coords.h"

using namespace PerkeleRim::Coords;

namespace
{
    struct Row
    {
        std::string         kind;
        std::vector<double> values;
    };

    std::vector<Row> LoadVectors()
    {
        std::ifstream    file(PROTOCOL_DIR "/tests/coords_vectors.csv");
        std::vector<Row> rows;
        std::string      line;
        while (std::getline(file, line)) {
            if (line.empty() || line[0] == '#') {
                continue;
            }
            std::stringstream stream(line);
            std::string       cell;
            Row               row;
            std::getline(stream, row.kind, ',');
            while (std::getline(stream, cell, ',')) {
                row.values.push_back(std::stod(cell));
            }
            rows.push_back(row);
        }
        return rows;
    }
}

TEST_CASE("coordinate conversion matches the shared vectors")
{
    const auto rows = LoadVectors();
    REQUIRE(rows.size() >= 10);

    for (const auto& row : rows) {
        CAPTURE(row.kind);
        if (row.kind == "pos") {
            const Vec3 skyrim{ (float)row.values[0], (float)row.values[1], (float)row.values[2] };
            const Vec3 unity{ (float)row.values[3], (float)row.values[4], (float)row.values[5] };
            const Vec3 u = SkyrimToUnity(skyrim);
            CHECK(u.x == doctest::Approx(unity.x).epsilon(1e-5));
            CHECK(u.y == doctest::Approx(unity.y).epsilon(1e-5));
            CHECK(u.z == doctest::Approx(unity.z).epsilon(1e-5));
            const Vec3 s = UnityToSkyrim(unity);
            CHECK(s.x == doctest::Approx(skyrim.x).epsilon(1e-5));
            CHECK(s.y == doctest::Approx(skyrim.y).epsilon(1e-5));
            CHECK(s.z == doctest::Approx(skyrim.z).epsilon(1e-5));
        } else if (row.kind == "angle" || row.kind == "angle_wrap") {
            const float radians = (float)row.values[0];
            const float degrees = (float)row.values[1];
            CHECK(std::abs(SkyrimAngleToUnity(radians) - degrees) < 1e-3f);
            if (row.kind == "angle") {
                CHECK(std::abs(UnityAngleToSkyrim(degrees) - radians) < 1e-5f);
            }
        } else if (row.kind == "rot") {
            const Quat unity{ (float)row.values[0], (float)row.values[1], (float)row.values[2], (float)row.values[3] };
            const Quat skyrim{ (float)row.values[4], (float)row.values[5], (float)row.values[6], (float)row.values[7] };
            const Quat s = UnityToSkyrim(unity);
            const Quat u = SkyrimToUnity(skyrim);
            CHECK(s.x == doctest::Approx(skyrim.x));
            CHECK(s.y == doctest::Approx(skyrim.y));
            CHECK(s.z == doctest::Approx(skyrim.z));
            CHECK(s.w == doctest::Approx(skyrim.w));
            CHECK(u.x == doctest::Approx(unity.x));
            CHECK(u.y == doctest::Approx(unity.y));
            CHECK(u.z == doctest::Approx(unity.z));
            CHECK(u.w == doctest::Approx(unity.w));
        } else {
            FAIL("unknown row kind");
        }
    }
}

TEST_CASE("one meter is about 70 units")
{
    CHECK(kUnitsPerMeter == doctest::Approx(69.99125));
}

TEST_CASE("a vehicle turned to face east in Unity faces east in Skyrim")
{
    // Unity yaw 90 degrees (clockwise from north seen from above).
    const Quat unity{ 0.0f, 0.70710678f, 0.0f, 0.70710678f };
    float      m[3][3];
    ToMatrix(UnityToSkyrim(unity), m);
    // Local forward (+Y in Skyrim) times the matrix: the middle column.
    CHECK(m[0][1] == doctest::Approx(1.0f));  // east
    CHECK(m[1][1] == doctest::Approx(0.0f).epsilon(1e-6));
    CHECK(m[2][1] == doctest::Approx(0.0f).epsilon(1e-6));
    // Up stays up.
    CHECK(m[2][2] == doctest::Approx(1.0f));
}
