#include <cstdint>
#include <vector>

#include <doctest/doctest.h>

#include "../../cpp/swf.h"

using namespace PerkeleRim;

namespace
{
    std::uint32_t U32At(const std::vector<std::uint8_t>& a_data, std::size_t a_at)
    {
        return a_data[a_at] | (a_data[a_at + 1] << 8) | (a_data[a_at + 2] << 16) | (static_cast<std::uint32_t>(a_data[a_at + 3]) << 24);
    }

    // Reads a_bits bits MSB first from a_data starting at bit a_bit.
    std::uint32_t Bits(const std::vector<std::uint8_t>& a_data, std::size_t& a_bit, int a_bits)
    {
        std::uint32_t v = 0;
        for (int i = 0; i < a_bits; ++i, ++a_bit) {
            v = (v << 1) | ((a_data[a_bit / 8] >> (7 - a_bit % 8)) & 1u);
        }
        return v;
    }
}

TEST_CASE("the empty movie is an uncompressed SWF whose length field is its size")
{
    const auto swf = Swf::EmptyMovie();
    REQUIRE(swf.size() > 20);
    CHECK(swf[0] == 'F');
    CHECK(swf[1] == 'W');
    CHECK(swf[2] == 'S');
    CHECK(swf[3] == Swf::kVersion);
    CHECK(U32At(swf, 4) == swf.size());
}

TEST_CASE("the stage is the asked size in twips, then the frame rate and one frame")
{
    const auto  swf = Swf::EmptyMovie(1280, 720, 30);
    std::size_t bit = 8 * 8;
    const int   n = static_cast<int>(Bits(swf, bit, 5));
    CHECK(n == 16);
    CHECK(Bits(swf, bit, n) == 0);
    CHECK(Bits(swf, bit, n) == 1280 * 20);
    CHECK(Bits(swf, bit, n) == 0);
    CHECK(Bits(swf, bit, n) == 720 * 20);
    const std::size_t at = (bit + 7) / 8;
    CHECK(swf[at] == 0);       // frame rate, fraction
    CHECK(swf[at + 1] == 30);  // frame rate, whole
    CHECK(swf[at + 2] == 1);   // frame count
    CHECK(swf[at + 3] == 0);
}

TEST_CASE("the tags start with FileAttributes and end with ShowFrame and End")
{
    const auto  swf = Swf::EmptyMovie();
    std::size_t at = 8 + 9 + 4;  // header, the 69-bit RECT, rate and count
    std::vector<std::uint16_t> codes;
    while (at + 1 < swf.size()) {
        const std::uint32_t head = swf[at] | (swf[at + 1] << 8);
        const std::uint32_t length = head & 0x3F;
        codes.push_back(static_cast<std::uint16_t>(head >> 6));
        at += 2 + length;
        if (head >> 6 == Swf::kTagEnd) {
            break;
        }
    }
    CHECK(at == swf.size());
    REQUIRE(codes.size() == 4);
    CHECK(codes[0] == Swf::kTagFileAttributes);
    CHECK(codes[1] == Swf::kTagSetBackgroundColor);
    CHECK(codes[2] == Swf::kTagShowFrame);
    CHECK(codes[3] == Swf::kTagEnd);
}
