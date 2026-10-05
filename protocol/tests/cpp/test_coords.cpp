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
        } else {
            FAIL("unknown row kind");
        }
    }
}

TEST_CASE("one meter is about 70 units")
{
    CHECK(kUnitsPerMeter == doctest::Approx(69.99125));
}
