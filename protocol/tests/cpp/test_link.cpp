#define WIN32_LEAN_AND_MEAN
#define NOMINMAX
#include <Windows.h>

#include <doctest/doctest.h>

#include <atomic>
#include <cstring>
#include <memory>
#include <string>
#include <vector>

#include "../../cpp/link.h"
#include "../../cpp/look_at.h"

using namespace PerkeleRim;
using namespace PerkeleRim::Protocol;

namespace
{
    // Each test gets its own section, so tests never see each other's state.
    std::wstring UniqueName()
    {
        static std::atomic<int> counter{ 0 };
        return L"Local\\PerkeleRim.Test." + std::to_wstring(::GetCurrentProcessId()) + L"." + std::to_wstring(counter++);
    }

    struct Logged
    {
        std::vector<std::string> lines;

        Link::LogFn Fn()
        {
            return [this](std::string_view a_line) { lines.emplace_back(a_line); };
        }

        bool Has(std::string_view a_needle) const
        {
            for (const auto& line : lines) {
                if (line.find(a_needle) != std::string::npos) {
                    return true;
                }
            }
            return false;
        }
    };

    struct Pair
    {
        std::wstring          name = UniqueName();
        Logged                skyrimLog, mscLog;
        std::unique_ptr<Link> skyrim = std::make_unique<Link>(Side::Skyrim, skyrimLog.Fn(), name);
        std::unique_ptr<Link> msc = std::make_unique<Link>(Side::Msc, mscLog.Fn(), name);
        std::uint64_t         now = 1000;

        void Tick(int a_times = 1, std::uint64_t a_stepMs = 50)
        {
            for (int i = 0; i < a_times; ++i) {
                now += a_stepMs;
                if (skyrim) {
                    skyrim->Tick(now);
                }
                if (msc) {
                    msc->Tick(now);
                }
            }
        }
    };

    // Raw view of a section, for faking a peer of another version.
    struct RawSection
    {
        HANDLE        handle = nullptr;
        std::uint8_t* base = nullptr;

        explicit RawSection(const std::wstring& a_name, std::uint32_t a_size = kMappingSize)
        {
            handle = ::CreateFileMappingW(INVALID_HANDLE_VALUE, nullptr, PAGE_READWRITE, 0, a_size, a_name.c_str());
            base = static_cast<std::uint8_t*>(::MapViewOfFile(handle, FILE_MAP_ALL_ACCESS, 0, 0, 0));
        }
        ~RawSection()
        {
            ::UnmapViewOfFile(base);
            ::CloseHandle(handle);
        }
        LinkHeader* Header() { return reinterpret_cast<LinkHeader*>(base); }
    };

    void Ignore(MessageType, const std::uint8_t*, std::uint32_t) {}
}

TEST_CASE("link comes up once both sides tick")
{
    Pair p;
    p.Tick(1);
    CHECK_FALSE(p.skyrim->IsUp());  // first heartbeat read only seeds
    p.Tick(2);
    CHECK(p.skyrim->IsUp());
    CHECK(p.msc->IsUp());
    CHECK(p.skyrimLog.Has("created shared memory"));
    CHECK(p.mscLog.Has("joined shared memory"));
    CHECK(p.skyrimLog.Has("link up: MSC mod pid"));
    CHECK(p.mscLog.Has("link up: Skyrim plugin pid"));
}

TEST_CASE("input and player slots carry the latest value")
{
    Pair p;
    p.Tick(3);
    REQUIRE(p.skyrim->IsUp());

    InputState in{};
    CHECK_FALSE(p.msc->ReadInput(in));  // never written

    for (std::uint32_t frame = 1; frame <= 3; ++frame) {
        InputState write{};
        write.frame = frame;
        write.flags = kInputOwnsPlayer;
        write.mouseX = 1.5 * frame;
        write.vertical = 1.0f;
        write.keys[119 / 64] |= 1ull << (119 % 64);  // KeyCode.W
        p.skyrim->WriteInput(write);
    }
    REQUIRE(p.msc->ReadInput(in));
    CHECK(in.frame == 3);
    CHECK(in.mouseX == 4.5);
    CHECK(in.vertical == 1.0f);
    CHECK(((in.keys[1] >> (119 - 64)) & 1) == 1);

    PlayerState player{};
    player.frame = 7;
    player.flags = kPlayerValid;
    player.position[2] = -12.5f;
    player.heading = 1.25f;
    player.stance = 2;
    player.flags |= kPlayerCamera;
    player.eye[2] = 59.5f;
    player.roll = -0.3f;
    player.fov = 42.0f;
    p.msc->WritePlayer(player);
    PlayerState out{};
    REQUIRE(p.skyrim->ReadPlayer(out));
    CHECK(std::memcmp(&out, &player, sizeof(player)) == 0);

    VehicleState vehicle{};
    CHECK_FALSE(p.skyrim->ReadVehicle(kVehicleHayosiko, vehicle));  // never written
    vehicle.frame = 9;
    vehicle.flags = kVehicleValid | kVehicleDriven;
    vehicle.vehicle = kVehicleHayosiko;
    vehicle.rotation[3] = 1.0f;
    vehicle.wheelSpin[3] = 2.5f;
    vehicle.seat[2] = 60.0f;
    p.msc->WriteVehicle(vehicle);
    VehicleState vehicleOut{};
    REQUIRE(p.skyrim->ReadVehicle(kVehicleHayosiko, vehicleOut));
    CHECK(std::memcmp(&vehicleOut, &vehicle, sizeof(vehicle)) == 0);
    // Each vehicle has its own slot; unknown ids are neither written nor read.
    CHECK_FALSE(p.skyrim->ReadVehicle(kVehicleJonnez, vehicleOut));
    VehicleState kekmet{};
    kekmet.frame = 11;
    kekmet.vehicle = kVehicleKekmet;
    kekmet.position[0] = 7.0f;
    p.msc->WriteVehicle(kekmet);
    REQUIRE(p.skyrim->ReadVehicle(kVehicleKekmet, vehicleOut));
    CHECK(std::memcmp(&vehicleOut, &kekmet, sizeof(kekmet)) == 0);
    REQUIRE(p.skyrim->ReadVehicle(kVehicleHayosiko, vehicleOut));
    CHECK(std::memcmp(&vehicleOut, &vehicle, sizeof(vehicle)) == 0);
    VehicleState unknown{};
    unknown.vehicle = kVehicleCount + 1;
    p.msc->WriteVehicle(unknown);
    CHECK_FALSE(p.skyrim->ReadVehicle(0, vehicleOut));
    CHECK_FALSE(p.skyrim->ReadVehicle(kVehicleCount + 1, vehicleOut));

    VehicleParts parts{};
    CHECK_FALSE(p.skyrim->ReadVehicleParts(kVehicleHayosiko, parts));  // never written
    parts.frame = 9;
    parts.vehicle = kVehicleHayosiko;
    parts.count = kVehiclePartCapacity;
    parts.model = 0x373B0001;
    parts.parts[0].rotation[3] = 1.0f;
    parts.parts[0].flags = kPartVisible;
    parts.parts[kVehiclePartCapacity - 1].translation[1] = 42.0f;
    p.msc->WriteVehicleParts(parts);
    VehicleParts partsOut{};
    REQUIRE(p.skyrim->ReadVehicleParts(kVehicleHayosiko, partsOut));
    CHECK(std::memcmp(&partsOut, &parts, sizeof(parts)) == 0);
    CHECK_FALSE(p.skyrim->ReadVehicleParts(kVehicleKekmet, partsOut));
    VehicleParts moped{};
    moped.vehicle = kVehicleJonnez;
    moped.count = 3;
    moped.parts[2].flags = kPartVisible;
    p.msc->WriteVehicleParts(moped);
    REQUIRE(p.skyrim->ReadVehicleParts(kVehicleJonnez, partsOut));
    CHECK(std::memcmp(&partsOut, &moped, sizeof(moped)) == 0);
    REQUIRE(p.skyrim->ReadVehicleParts(kVehicleHayosiko, partsOut));
    CHECK(std::memcmp(&partsOut, &parts, sizeof(parts)) == 0);

    ItemState items{};
    CHECK_FALSE(p.skyrim->ReadItems(items));  // never written
    items.frame = 9;
    items.generation = 3;
    items.count = kItemCapacity;
    items.items[0] = { 7, 2, kItemVisible | kItemHeld, { 1.0f, 2.0f, 3.0f }, { 0.0f, 0.0f, 0.0f, 1.0f } };
    items.items[kItemCapacity - 1].id = 99;
    p.msc->WriteItems(items);
    ItemState itemsOut{};
    REQUIRE(p.skyrim->ReadItems(itemsOut));
    CHECK(std::memcmp(&itemsOut, &items, sizeof(items)) == 0);

    auto particles = std::make_unique<ParticleState>();
    auto particlesOut = std::make_unique<ParticleState>();
    CHECK_FALSE(p.skyrim->ReadParticles(*particlesOut));  // never written
    particles->frame = 10;
    particles->generation = 3;
    particles->count = kParticleCapacity;
    particles->particles[0] = { 5, { 1.0f, 2.0f, 3.0f }, 30.0f, 0.5f, { 255, 128, 0, 64 }, { 4, 4, 15, 0 } };
    particles->particles[kParticleCapacity - 1].model = 6;
    p.msc->WriteParticles(*particles);
    REQUIRE(p.skyrim->ReadParticles(*particlesOut));
    CHECK(std::memcmp(particlesOut.get(), particles.get(), sizeof(ParticleState)) == 0);

    HandLayer hands{};
    CHECK_FALSE(p.skyrim->ReadHandLayer(hands));  // never written
    hands = { 12, kHandLayerValid, 0x40000A42ull, 1920, 1080, 28, 350 };
    p.msc->WriteHandLayer(hands);
    HandLayer handsOut{};
    REQUIRE(p.skyrim->ReadHandLayer(handsOut));
    CHECK(std::memcmp(&handsOut, &hands, sizeof(hands)) == 0);
}

TEST_CASE("look-at text travels from MSC to Skyrim")
{
    Pair p;
    p.Tick(3);
    REQUIRE(p.msc->IsUp());

    const std::pair<const char*, std::uint32_t> prompts[] = { { "IGNITION", 0 }, { "", kLookAtUse }, { "", 0 } };
    for (const auto& [text, flags] : prompts) {
        const auto payload = LookAt::Encode(text, flags);
        REQUIRE(p.msc->Send(MessageType::LookAtText, payload.data(), (std::uint32_t)payload.size()));
    }
    std::vector<std::pair<std::string, std::uint32_t>> got;
    p.skyrim->Receive([&](MessageType a_type, const std::uint8_t* a_data, std::uint32_t a_size) {
        CHECK(a_type == MessageType::LookAtText);
        const auto prompt = LookAt::Decode(a_data, a_size);
        REQUIRE(prompt.has_value());
        got.emplace_back(std::string(prompt->text), prompt->flags);
    });
    REQUIRE(got.size() == 3);
    for (std::size_t i = 0; i < got.size(); i++) {
        CHECK(got[i].first == prompts[i].first);
        CHECK(got[i].second == prompts[i].second);
    }
}

TEST_CASE("what MSC's player consumed travels from MSC to Skyrim")
{
    Pair p;
    p.Tick(3);
    REQUIRE(p.msc->IsUp());

    const std::uint32_t kinds[] = { kConsumedBeer, kConsumedCigarette, kConsumedFood };
    for (const auto kind : kinds) {
        const Consumed consumed{ kind, 0 };
        REQUIRE(p.msc->Send(MessageType::Consumed, &consumed, sizeof(consumed)));
    }
    std::vector<std::uint32_t> got;
    p.skyrim->Receive([&](MessageType a_type, const std::uint8_t* a_data, std::uint32_t a_size) {
        CHECK(a_type == MessageType::Consumed);
        REQUIRE(a_size == sizeof(Consumed));
        Consumed consumed{};
        std::memcpy(&consumed, a_data, sizeof(consumed));
        got.push_back(consumed.kind);
    });
    CHECK(got == std::vector<std::uint32_t>(std::begin(kinds), std::end(kinds)));
}

TEST_CASE("the player's shout travels from Skyrim to MSC with its kind")
{
    Pair p;
    p.Tick(3);
    REQUIRE(p.msc->IsUp());

    const PlayerShout shout{ kShoutUnrelentingForce, 0x13E07, 3, 0 };
    REQUIRE(p.skyrim->Send(MessageType::PlayerShout, &shout, sizeof(shout)));
    int count = 0;
    p.msc->Receive([&](MessageType a_type, const std::uint8_t* a_data, std::uint32_t a_size) {
        CHECK(a_type == MessageType::PlayerShout);
        REQUIRE(a_size == sizeof(PlayerShout));
        PlayerShout got{};
        std::memcpy(&got, a_data, sizeof(got));
        CHECK(got.kind == kShoutUnrelentingForce);
        CHECK(got.shout == 0x13E07u);
        CHECK(got.words == 3u);
        count++;
    });
    CHECK(count == 1);
}

TEST_CASE("the shelf spots travel from Skyrim to MSC with their anchor generation")
{
    Pair p;
    p.Tick(3);
    REQUIRE(p.msc->IsUp());

    StockShelves stock{};
    stock.generation = 7;
    stock.count = 2;
    stock.spots[0] = { { 15000.0f, -14600.0f, -2900.0f }, 1.0f };
    stock.spots[1] = { { 15010.0f, -14610.0f, -2850.0f }, 2.0f };
    REQUIRE(p.skyrim->Send(MessageType::StockShelves, &stock, sizeof(stock)));
    int count = 0;
    p.msc->Receive([&](MessageType a_type, const std::uint8_t* a_data, std::uint32_t a_size) {
        CHECK(a_type == MessageType::StockShelves);
        REQUIRE(a_size == sizeof(StockShelves));
        StockShelves got{};
        std::memcpy(&got, a_data, sizeof(got));
        CHECK(got.generation == 7u);
        CHECK(got.count == 2u);
        CHECK(got.spots[1].position[2] == -2850.0f);
        CHECK(got.spots[1].heading == 2.0f);
        count++;
    });
    CHECK(count == 1);
}

TEST_CASE("look-at text is cut at a character and malformed payloads are refused")
{
    std::string long_text(kLookAtTextMax - 1, 'a');
    long_text += "\xC3\xA4\xC3\xA4";  // "ää": the first one straddles the limit
    const auto payload = LookAt::Encode(long_text);
    const auto prompt = LookAt::Decode(payload.data(), (std::uint32_t)payload.size());
    REQUIRE(prompt.has_value());
    CHECK(prompt->text.size() == kLookAtTextMax - 1);

    const auto ok = LookAt::Encode("KEYS");
    CHECK_FALSE(LookAt::Decode(ok.data(), 4).has_value());                            // no full header
    CHECK_FALSE(LookAt::Decode(ok.data(), (std::uint32_t)ok.size() - 1).has_value());  // text cut short
    auto huge = ok;
    huge[0] = 0xFF;  // length 511: over the limit
    huge[1] = 0x01;
    CHECK_FALSE(LookAt::Decode(huge.data(), (std::uint32_t)huge.size()).has_value());
}

TEST_CASE("ring delivers messages in order, across the wrap point")
{
    Pair p;
    p.Tick(3);
    REQUIRE(p.msc->IsUp());

    // MSC -> Skyrim ring is 256 KiB; push 3 MiB through it in 40 KB messages.
    std::vector<std::uint8_t> payload(40000);
    std::uint32_t             sent = 0, received = 0;
    for (int round = 0; round < 80; ++round) {
        for (std::size_t i = 0; i < payload.size(); ++i) {
            payload[i] = static_cast<std::uint8_t>(sent + i);
        }
        REQUIRE(p.msc->Send(MessageType::WorldspaceChange, payload.data(), (std::uint32_t)payload.size()));
        ++sent;
        p.skyrim->Receive([&](MessageType a_type, const std::uint8_t* a_data, std::uint32_t a_size) {
            CHECK(a_type == MessageType::WorldspaceChange);
            REQUIRE(a_size == 40000);
            CHECK(a_data[0] == static_cast<std::uint8_t>(received));
            CHECK(a_data[39999] == static_cast<std::uint8_t>(received + 39999));
            ++received;
        });
    }
    CHECK(received == sent);
}

TEST_CASE("ring refuses messages when full or too large")
{
    Pair p;
    p.Tick(3);
    std::vector<std::uint8_t> payload(100000);
    CHECK(p.msc->Send(MessageType::WorldspaceChange, payload.data(), 100000));
    CHECK(p.msc->Send(MessageType::WorldspaceChange, payload.data(), 100000));
    CHECK_FALSE(p.msc->Send(MessageType::WorldspaceChange, payload.data(), 100000));  // 256 KiB ring
    CHECK_FALSE(p.skyrim->Send(MessageType::CollisionChunk, payload.data(), kRingToMscCapacity));
    CHECK(p.skyrim->Receive(Ignore) == 2);
    CHECK(p.msc->Send(MessageType::WorldspaceChange, payload.data(), 100000));
}

TEST_CASE("nothing is sent or read while the link is down")
{
    Pair p;
    p.Tick(1);
    std::uint8_t byte = 1;
    CHECK_FALSE(p.skyrim->Send(MessageType::CollisionChunk, &byte, 1));
    PlayerState out{};
    CHECK_FALSE(p.skyrim->ReadPlayer(out));
}

TEST_CASE("link goes down when the peer stops ticking or detaches")
{
    Pair p;
    p.Tick(3);
    REQUIRE(p.skyrim->IsUp());

    // MSC stalls (a long scene load, say) past the timeout.
    for (int i = 0; i < 50; ++i) {
        p.now += 50;
        p.skyrim->Tick(p.now);
    }
    CHECK_FALSE(p.skyrim->IsUp());
    CHECK(p.skyrimLog.Has("link down: no heartbeat from the MSC mod"));

    p.Tick(2);
    CHECK(p.skyrim->IsUp());

    p.msc.reset();  // clean detach
    p.Tick(1);
    CHECK_FALSE(p.skyrim->IsUp());
    CHECK(p.skyrimLog.Has("link down: the MSC mod detached"));
}

TEST_CASE("a restarted peer comes back up and drops stale messages")
{
    Pair p;
    p.Tick(3);
    std::uint8_t byte = 42;
    REQUIRE(p.skyrim->Send(MessageType::CollisionChunk, &byte, 1));

    p.msc.reset();
    p.Tick(1);
    p.msc = std::make_unique<Link>(Side::Msc, p.mscLog.Fn(), p.name);
    p.Tick(3);
    REQUIRE(p.msc->IsUp());
    CHECK(p.msc->Receive(Ignore) == 0);
}

TEST_CASE("a peer speaking another protocol version disables the link")
{
    Pair p;
    p.Tick(3);
    REQUIRE(p.skyrim->IsUp());

    // Pretend the MSC side was replaced by one speaking v99.
    p.msc.reset();
    RawSection raw(p.name);
    PeerInfo&  msc = raw.Header()->peers[static_cast<int>(Side::Msc)];
    msc.pid = 1234;
    msc.version = 99;
    p.Tick(1);
    CHECK(p.skyrim->GetState() == Link::State::Disabled);
    CHECK(p.skyrimLog.Has("protocol mismatch: the MSC mod speaks v99, this Skyrim plugin speaks v" + std::to_string(kProtocolVersion)));
}

TEST_CASE("a section created by another protocol version disables the joining side")
{
    const std::wstring name = UniqueName();
    RawSection         raw(name, 4096);
    raw.Header()->headerSize = sizeof(LinkHeader);
    raw.Header()->mappingSize = 4096;
    raw.Header()->creatorVersion = 99;
    raw.Header()->magic = kMagic;

    Logged log;
    Link   msc(Side::Msc, log.Fn(), name);
    msc.Tick(1000);
    CHECK(msc.GetState() == Link::State::Disabled);
    CHECK(log.Has("protocol mismatch: the shared memory was created with protocol v99, this MSC mod speaks v" + std::to_string(kProtocolVersion)));
    // Its version is published anyway, so the creator side can report the mismatch as well.
    CHECK(raw.Header()->peers[static_cast<int>(Side::Msc)].version == kProtocolVersion);
}

TEST_CASE("a foreign section is rejected")
{
    const std::wstring name = UniqueName();
    RawSection         raw(name, 4096);
    raw.Header()->magic = 0xDEADBEEF;

    Logged log;
    Link   skyrim(Side::Skyrim, log.Fn(), name);
    skyrim.Tick(1000);
    CHECK(skyrim.GetState() == Link::State::Disabled);
    CHECK(log.Has("not a PerkeleRim link"));
}

TEST_CASE("a thrown item's way travels from MSC to Skyrim")
{
    Pair p;
    p.Tick(3);
    REQUIRE(p.msc->IsUp());

    const ThrownPath path{ 7, 14.5f, { 1000.0f, -2000.0f, 512.0f }, { 1010.0f, -1990.0f, 509.0f } };
    REQUIRE(p.msc->Send(MessageType::ThrownPath, &path, sizeof(path)));
    int count = 0;
    p.skyrim->Receive([&](MessageType a_type, const std::uint8_t* a_data, std::uint32_t a_size) {
        CHECK(a_type == MessageType::ThrownPath);
        REQUIRE(a_size == sizeof(ThrownPath));
        ThrownPath got{};
        std::memcpy(&got, a_data, sizeof(got));
        CHECK(got.flight == 7u);
        CHECK(got.speed == 14.5f);
        CHECK(got.from[1] == -2000.0f);
        CHECK(got.to[2] == 509.0f);
        count++;
    });
    CHECK(count == 1);
}
