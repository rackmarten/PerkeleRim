// The ModelChunk messages (MSC -> Skyrim): MSC's own meshes and textures as one blob, cut into
// chunks. Header-only: the assembler puts a blob back together, Parse checks it and points into it.
// msc/src/Link/Model.cs writes them; the protocol tests cover both.

#pragma once

#include <cstdint>
#include <cstring>
#include <optional>
#include <string>
#include <string_view>
#include <vector>

#include "../perkelerim_protocol.h"

namespace PerkeleRim::Model
{
    struct Texture
    {
        std::uint32_t       width;
        std::uint32_t       height;
        const std::uint8_t* rgba;  // width * height * 4, top row first
    };

    struct Mesh
    {
        std::string                   name;
        std::int32_t                  texture;  // index into Model::textures, -1 = none
        std::int32_t                  material;  // index into Model::materials, -1 = default
        const Protocol::ModelVertex*  vertices;  // unaligned: copy before use where alignment matters
        std::uint32_t                 vertexCount;
        const std::uint8_t*           indices;   // indexCount little-endian uint16
        std::uint32_t                 indexCount;

        std::uint16_t Index(std::uint32_t a_i) const
        {
            return static_cast<std::uint16_t>(indices[a_i * 2] | (indices[a_i * 2 + 1] << 8));
        }

        Protocol::ModelVertex Vertex(std::uint32_t a_i) const
        {
            Protocol::ModelVertex v;
            std::memcpy(&v, reinterpret_cast<const std::uint8_t*>(vertices) + a_i * sizeof(v), sizeof(v));
            return v;
        }
    };

    struct Model
    {
        std::string          name;
        std::vector<Texture>                 textures;
        std::vector<Protocol::ModelMaterial> materials;
        std::vector<Mesh>                    meshes;
    };

    inline std::string Name(const char (&a_field)[Protocol::kModelNameMax])
    {
        std::size_t length = 0;
        while (length < Protocol::kModelNameMax && a_field[length] != '\0') {
            length++;
        }
        return std::string(a_field, length);
    }

    // The model in a blob, pointing into it; nothing when anything is out of bounds.
    inline std::optional<Model> Parse(const std::uint8_t* a_blob, std::size_t a_size)
    {
        std::size_t at = 0;
        auto take = [&](void* a_out, std::size_t a_bytes) {
            if (a_size - at < a_bytes) {
                return false;
            }
            std::memcpy(a_out, a_blob + at, a_bytes);
            at += a_bytes;
            return true;
        };

        Protocol::ModelHeader header{};
        if (!a_blob || !take(&header, sizeof(header))) {
            return std::nullopt;
        }
        Model model;
        model.name = Name(header.name);
        for (std::uint32_t i = 0; i < header.textureCount; i++) {
            Protocol::ModelTexture texture{};
            if (!take(&texture, sizeof(texture)) || texture.format != Protocol::kModelTextureRgba8 || texture.width == 0 ||
                texture.height == 0 || texture.width > Protocol::kModelTextureSizeMax ||
                texture.height > Protocol::kModelTextureSizeMax) {
                return std::nullopt;
            }
            const std::size_t bytes = static_cast<std::size_t>(texture.width) * texture.height * 4;
            if (a_size - at < bytes) {
                return std::nullopt;
            }
            model.textures.push_back({ texture.width, texture.height, a_blob + at });
            at += bytes;
        }
        for (std::uint32_t i = 0; i < header.materialCount; i++) {
            Protocol::ModelMaterial material{};
            if (!take(&material, sizeof(material))) {
                return std::nullopt;
            }
            model.materials.push_back(material);
        }
        for (std::uint32_t i = 0; i < header.meshCount; i++) {
            Protocol::ModelMesh mesh{};
            if (!take(&mesh, sizeof(mesh)) || mesh.vertexCount > Protocol::kModelMeshVertexMax || mesh.indexCount % 3 != 0 ||
                mesh.texture < -1 || mesh.texture >= static_cast<std::int32_t>(header.textureCount) || mesh.material < -1 ||
                mesh.material >= static_cast<std::int32_t>(header.materialCount)) {
                return std::nullopt;
            }
            const std::size_t vertexBytes = static_cast<std::size_t>(mesh.vertexCount) * sizeof(Protocol::ModelVertex);
            const std::size_t indexBytes = static_cast<std::size_t>(mesh.indexCount) * 2;
            if (a_size - at < vertexBytes + indexBytes) {
                return std::nullopt;
            }
            Mesh out{ Name(mesh.name), mesh.texture, mesh.material, reinterpret_cast<const Protocol::ModelVertex*>(a_blob + at),
                mesh.vertexCount, a_blob + at + vertexBytes, mesh.indexCount };
            at += vertexBytes + indexBytes;
            for (std::uint32_t k = 0; k < out.indexCount; k++) {
                if (out.Index(k) >= out.vertexCount) {
                    return std::nullopt;
                }
            }
            model.meshes.push_back(std::move(out));
        }
        return at == a_size ? std::optional<Model>(std::move(model)) : std::nullopt;
    }

    // Puts blobs back together from ModelChunk payloads.
    class Assembler
    {
    public:
        enum class Result
        {
            Partial,   // more chunks to come
            Complete,  // Blob() holds a whole model until the next Add
            Dropped,   // malformed or out of order; the model being received is gone
        };

        Result Add(const std::uint8_t* a_payload, std::uint32_t a_size)
        {
            Protocol::ModelChunk chunk{};
            if (!a_payload || a_size < sizeof(chunk)) {
                return Drop();
            }
            std::memcpy(&chunk, a_payload, sizeof(chunk));
            if (chunk.size != a_size - sizeof(chunk) || chunk.size > Protocol::kModelChunkMax ||
                chunk.totalSize > Protocol::kModelSizeMax || chunk.offset + static_cast<std::uint64_t>(chunk.size) > chunk.totalSize) {
                return Drop();
            }
            if (chunk.offset == 0) {
                _model = chunk.model;
                _total = chunk.totalSize;
                _blob.clear();
                _blob.reserve(chunk.totalSize);
                _receiving = true;
            } else if (!_receiving || chunk.model != _model || chunk.totalSize != _total || chunk.offset != _blob.size()) {
                return Drop();
            }
            _blob.insert(_blob.end(), a_payload + sizeof(chunk), a_payload + a_size);
            if (_blob.size() < _total) {
                return Result::Partial;
            }
            _receiving = false;
            return Result::Complete;
        }

        std::uint32_t                     Id() const { return _model; }
        const std::vector<std::uint8_t>& Blob() const { return _blob; }

    private:
        Result Drop()
        {
            _receiving = false;
            _blob.clear();
            return Result::Dropped;
        }

        std::vector<std::uint8_t> _blob;
        std::uint32_t             _model = 0;
        std::uint32_t             _total = 0;
        bool                      _receiving = false;
    };
}
