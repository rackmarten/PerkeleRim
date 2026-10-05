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
