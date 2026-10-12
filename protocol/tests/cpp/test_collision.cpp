#include <doctest/doctest.h>

#include <cstring>
#include <vector>

#include "../../cpp/collision.h"

using namespace PerkeleRim;
using namespace PerkeleRim::Collision;

namespace
{
    struct Parsed
    {
        Protocol::CollisionChunkHeader header{};
        std::vector<float>             vertices;
        std::vector<std::uint32_t>     indices;
    };

    Parsed Parse(const std::vector<std::uint8_t>& a_payload)
    {
        Parsed p;
        REQUIRE(a_payload.size() >= sizeof(p.header));
        std::memcpy(&p.header, a_payload.data(), sizeof(p.header));
        const std::size_t vertexBytes = p.header.vertexCount * 3 * sizeof(float);
        const std::size_t indexBytes = p.header.indexCount * sizeof(std::uint32_t);
        REQUIRE(a_payload.size() == sizeof(p.header) + vertexBytes + indexBytes);
        p.vertices.resize(p.header.vertexCount * 3);
        p.indices.resize(p.header.indexCount);
        if (vertexBytes) {
            std::memcpy(p.vertices.data(), a_payload.data() + sizeof(p.header), vertexBytes);
        }
        if (indexBytes) {
            std::memcpy(p.indices.data(), a_payload.data() + sizeof(p.header) + vertexBytes, indexBytes);
        }
        return p;
    }

    // Z of cross(b - a, c - a) for triangle t: positive = counterclockwise seen from above.
    float NormalZ(const Parsed& a_p, std::size_t a_t)
    {
        const float* a = &a_p.vertices[a_p.indices[a_t * 3] * 3];
        const float* b = &a_p.vertices[a_p.indices[a_t * 3 + 1] * 3];
        const float* c = &a_p.vertices[a_p.indices[a_t * 3 + 2] * 3];
        return (b[0] - a[0]) * (c[1] - a[1]) - (b[1] - a[1]) * (c[0] - a[0]);
    }

    Heightfield Flat(std::int32_t a_x, std::int32_t a_y, std::uint32_t a_cells, float a_z)
    {
        Heightfield f;
        f.Reset(a_x, a_y, a_cells);
        for (std::uint32_t k = 0; k < f.Samples(); k++) {
            f.heights[k] = a_z;
            f.hit[k] = 1;
        }
        return f;
    }
}

TEST_CASE("chunk index floors toward negative infinity")
{
    CHECK(ChunkIndex(0.0f) == 0);
    CHECK(ChunkIndex(2047.9f) == 0);
    CHECK(ChunkIndex(2048.0f) == 1);
    CHECK(ChunkIndex(-0.5f) == -1);
    CHECK(ChunkIndex(-2048.0f) == -1);
    CHECK(ChunkIndex(-2048.5f) == -2);
}

TEST_CASE("heightfield samples cover the chunk edge to edge")
{
    Heightfield f;
    f.Reset(-2, 3, 32);
    CHECK(f.Samples() == 33 * 33);
    CHECK(f.X(0) == doctest::Approx(-4096.0));
    CHECK(f.X(32) == doctest::Approx(-2048.0));
    CHECK(f.Y(0) == doctest::Approx(6144.0));
    CHECK(f.Y(32) == doctest::Approx(8192.0));
}

TEST_CASE("a fully hit heightfield becomes a closed grid wound counterclockwise")
{
    auto f = Flat(5, -7, 4, 1234.0f);
    f.heights[2 * 5 + 3] = 1300.0f;  // a bump, so not every triangle is flat
    const auto p = Parse(BuildChunkPayload(f, 0x3C, 9));

    CHECK(p.header.chunkX == 5);
    CHECK(p.header.chunkY == -7);
    CHECK(p.header.worldspace == 0x3C);
    CHECK(p.header.generation == 9);
    CHECK(p.header.origin[0] == doctest::Approx(5 * 2048.0));
    CHECK(p.header.origin[1] == doctest::Approx(-7 * 2048.0));
    CHECK(p.header.origin[2] == doctest::Approx(1234.0));
    CHECK(p.header.vertexCount == 25);
    CHECK(p.header.indexCount == 4 * 4 * 2 * 3);
    for (std::size_t t = 0; t < p.header.indexCount / 3; t++) {
        CHECK(NormalZ(p, t) > 0.0f);
    }

    // Vertices are relative to the origin.
    const std::uint32_t bump = 2 * 5 + 3;
    CHECK(p.vertices[bump * 3 + 0] == doctest::Approx(3 * 512.0));
    CHECK(p.vertices[bump * 3 + 1] == doctest::Approx(2 * 512.0));
    CHECK(p.vertices[bump * 3 + 2] == doctest::Approx(66.0));
}

TEST_CASE("a missed sample drops only the triangles touching it")
{
    auto f = Flat(0, 0, 2, 0.0f);
    f.hit[1 * 3 + 1] = 0;  // the middle sample: 6 of the 8 triangles touch it
    auto p = Parse(BuildChunkPayload(f, 0x3C, 1));
    CHECK(p.header.vertexCount == 8);
    CHECK(p.header.indexCount == 2 * 3);

    f = Flat(0, 0, 2, 0.0f);
    f.hit[0] = 0;  // a corner: only the one triangle using it goes
    p = Parse(BuildChunkPayload(f, 0x3C, 1));
    CHECK(p.header.vertexCount == 8);
    CHECK(p.header.indexCount == 7 * 3);
    for (const auto index : p.indices) {
        CHECK(index < p.header.vertexCount);
    }
}

TEST_CASE("an empty heightfield still makes a valid payload")
{
    Heightfield f;
    f.Reset(1, 1, 8);
    const auto p = Parse(BuildChunkPayload(f, 0, 3));
    CHECK(p.header.vertexCount == 0);
    CHECK(p.header.indexCount == 0);
    CHECK(f.Hits() == 0);
}

TEST_CASE("a full-size chunk fits the ring many times over")
{
    const auto payload = BuildChunkPayload(Flat(0, 0, 32, 0.0f), 0x3C, 1);
    CHECK(payload.size() + sizeof(Protocol::MessageHeader) < Protocol::kRingToMscCapacity / 64);
}

TEST_CASE("static triangles follow the heightfield, sharing identical corners")
{
    // Two triangles of a 100 x 100 floor at z = -40 in chunk (1, 0), counterclockwise from above.
    const std::vector<float> statics{
        2100, 100, -40, 2200, 100, -40, 2100, 200, -40,
        2200, 100, -40, 2200, 200, -40, 2100, 200, -40,
    };
    std::uint32_t dropped = 99;
    const auto    p = Parse(BuildChunkPayload(Flat(1, 0, 2, 10.0f), 0x3C, 4, statics, &dropped));
    CHECK(dropped == 0);
    CHECK(p.header.origin[2] == doctest::Approx(-40.0));  // the statics are lower than the terrain
    CHECK(p.header.vertexCount == 9 + 4);
    REQUIRE(p.header.indexCount == 8 * 3 + 2 * 3);
    CHECK(p.vertices[2] == doctest::Approx(50.0));  // terrain, relative to the new origin
    for (std::size_t t = 8; t < 10; t++) {
        CHECK(NormalZ(p, t) > 0.0f);
    }
    const float* first = &p.vertices[p.indices[8 * 3] * 3];
    CHECK(first[0] == doctest::Approx(2100.0 - 2048.0));
    CHECK(first[1] == doctest::Approx(100.0));
    CHECK(first[2] == doctest::Approx(0.0));
}

TEST_CASE("a chunk with no terrain (an interior) is just its statics")
{
    Heightfield f;
    f.Reset(0, 0, 8);
    const std::vector<float> statics{ 10, 10, 500, 60, 10, 500, 10, 60, 520 };
    const auto               p = Parse(BuildChunkPayload(f, 0, 2, statics));
    CHECK(p.header.origin[2] == doctest::Approx(500.0));
    CHECK(p.header.vertexCount == 3);
    CHECK(p.header.indexCount == 3);
}

TEST_CASE("statics past the vertex limit are dropped and counted")
{
    std::vector<float> statics;
    const std::uint32_t triangles = kMaxChunkVertices / 3 + 10;
    for (std::uint32_t t = 0; t < triangles; t++) {
        const float x = static_cast<float>(t % 1000), y = static_cast<float>(t / 1000);
        const float tri[9] = { x, y, 0, x + 0.5f, y, 0, x, y + 0.5f, 0 };
        statics.insert(statics.end(), tri, tri + 9);
    }
    std::uint32_t dropped = 0;
    const auto    p = Parse(BuildChunkPayload(Flat(0, 0, 2, 0.0f), 0, 1, statics, &dropped));
    CHECK(p.header.vertexCount <= kMaxChunkVertices);
    CHECK(dropped > 0);
    CHECK(p.header.indexCount == (8 + triangles - dropped) * 3);
}
