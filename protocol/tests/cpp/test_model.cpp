#include <doctest/doctest.h>

#include <cstring>
#include <vector>

#include "../../cpp/model.h"

using namespace PerkeleRim;

namespace
{
    template <class T>
    void Put(std::vector<std::uint8_t>& a_out, const T& a_value)
    {
        const auto* p = reinterpret_cast<const std::uint8_t*>(&a_value);
        a_out.insert(a_out.end(), p, p + sizeof(T));
    }

    // One 2x1 texture, one cutout material and one triangle using both, the way
    // msc/src/Link/Model.cs writes them.
    std::vector<std::uint8_t> Blob(std::uint16_t a_lastIndex = 2, std::int32_t a_texture = 0, std::int32_t a_material = 0)
    {
        std::vector<std::uint8_t> blob;
        Protocol::ModelHeader     header{};
        std::memcpy(header.name, "beer case", 9);
        header.textureCount = 1;
        header.meshCount = 1;
        header.materialCount = 1;
        Put(blob, header);

        Put(blob, Protocol::ModelTexture{ 2, 1, Protocol::kModelTextureRgba8, 0 });
        for (std::uint8_t b : std::initializer_list<std::uint8_t>{ 255, 0, 0, 255, 0, 255, 0, 255 }) {
            blob.push_back(b);
        }
        Put(blob, Protocol::ModelMaterial{ Protocol::kMaterialCutout, 0.5f, { 0.2f, 0.2f, 0.2f }, 0.7f, { 0, 0, 0 }, 0 });

        Protocol::ModelMesh mesh{};
        std::memcpy(mesh.name, "bottle", 6);
        mesh.texture = a_texture;
        mesh.material = a_material;
        mesh.vertexCount = 3;
        mesh.indexCount = 3;
        Put(blob, mesh);
        Put(blob, Protocol::ModelVertex{ { 0, 0, 0 }, { 0, 0, 1 }, { 0, 0 } });
        Put(blob, Protocol::ModelVertex{ { 10, 0, 0 }, { 0, 0, 1 }, { 1, 0 } });
        Put(blob, Protocol::ModelVertex{ { 0, 10, 0 }, { 0, 0, 1 }, { 0, 1 } });
        for (std::uint16_t i : { std::uint16_t(0), std::uint16_t(1), a_lastIndex }) {
            Put(blob, i);
        }
        return blob;
    }

    std::vector<std::uint8_t> Chunk(std::uint32_t a_model, const std::vector<std::uint8_t>& a_blob, std::size_t a_offset, std::size_t a_size)
    {
        std::vector<std::uint8_t> payload;
        Put(payload, Protocol::ModelChunk{ a_model, static_cast<std::uint32_t>(a_blob.size()), static_cast<std::uint32_t>(a_offset),
                         static_cast<std::uint32_t>(a_size) });
        payload.insert(payload.end(), a_blob.begin() + a_offset, a_blob.begin() + a_offset + a_size);
        return payload;
    }
}

TEST_CASE("a model blob parses into textures and meshes")
{
    const auto blob = Blob();
    const auto model = Model::Parse(blob.data(), blob.size());
    REQUIRE(model);
    CHECK(model->name == "beer case");
    REQUIRE(model->textures.size() == 1);
    CHECK(model->textures[0].width == 2);
    CHECK(model->textures[0].rgba[4 + 1] == 255);  // second pixel green
    REQUIRE(model->meshes.size() == 1);
    const auto& mesh = model->meshes[0];
    CHECK(mesh.name == "bottle");
    CHECK(mesh.texture == 0);
    CHECK(mesh.material == 0);
    REQUIRE(model->materials.size() == 1);
    CHECK(model->materials[0].flags == Protocol::kMaterialCutout);
    CHECK(model->materials[0].glossiness == doctest::Approx(0.7f));
    CHECK(mesh.vertexCount == 3);
    CHECK(mesh.Vertex(1).position[0] == 10.0f);
    CHECK(mesh.Vertex(2).uv[1] == 1.0f);
    CHECK(mesh.Index(2) == 2);
}

TEST_CASE("a malformed model blob is refused")
{
    auto blob = Blob();
    CHECK_FALSE(Model::Parse(blob.data(), blob.size() - 1));  // truncated
    blob.push_back(0);
    CHECK_FALSE(Model::Parse(blob.data(), blob.size()));  // trailing bytes
    const auto badIndex = Blob(3);
    CHECK_FALSE(Model::Parse(badIndex.data(), badIndex.size()));
    const auto badTexture = Blob(2, 1);
    CHECK_FALSE(Model::Parse(badTexture.data(), badTexture.size()));
    const auto badMaterial = Blob(2, 0, 1);
    CHECK_FALSE(Model::Parse(badMaterial.data(), badMaterial.size()));
    const auto noMaterial = Blob(2, 0, -1);
    CHECK(Model::Parse(noMaterial.data(), noMaterial.size()));
}

TEST_CASE("chunks put a model back together in order")
{
    const auto      blob = Blob();
    Model::Assembler assembler;
    const std::size_t half = blob.size() / 2;

    const auto first = Chunk(7, blob, 0, half);
    const auto second = Chunk(7, blob, half, blob.size() - half);
    CHECK(assembler.Add(first.data(), static_cast<std::uint32_t>(first.size())) == Model::Assembler::Result::Partial);
    CHECK(assembler.Add(second.data(), static_cast<std::uint32_t>(second.size())) == Model::Assembler::Result::Complete);
    CHECK(assembler.Id() == 7);
    CHECK(assembler.Blob() == blob);

    // A chunk that doesn't continue the model drops it; the next model starts over at offset 0.
    CHECK(assembler.Add(first.data(), static_cast<std::uint32_t>(first.size())) == Model::Assembler::Result::Partial);
    const auto other = Chunk(8, blob, half, blob.size() - half);
    CHECK(assembler.Add(other.data(), static_cast<std::uint32_t>(other.size())) == Model::Assembler::Result::Dropped);
    CHECK(assembler.Add(second.data(), static_cast<std::uint32_t>(second.size())) == Model::Assembler::Result::Dropped);
    const auto whole = Chunk(9, blob, 0, blob.size());
    CHECK(assembler.Add(whole.data(), static_cast<std::uint32_t>(whole.size())) == Model::Assembler::Result::Complete);
    CHECK(assembler.Blob() == blob);
}
