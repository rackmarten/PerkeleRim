// C++ end of the PerkeleRim link (used by the SKSE plugin and the protocol tests).
// Mirrored in msc/src/Link/Link.cs. Windows-only; the implementation lives in link.cpp so this
// header stays free of <Windows.h>.
//
// Threading: Tick() may run on its own thread. The slot and ring calls may run on another thread,
// but each slot has one writer and each ring one producer and one consumer.

#pragma once

#include <atomic>
#include <cstdint>
#include <functional>
#include <string>
#include <string_view>
#include <vector>

#include "../perkelerim_protocol.h"

namespace PerkeleRim
{
    class Link
    {
    public:
        enum class State
        {
            Detached,  // no mapping yet (or mapping failed; retried every tick)
            Waiting,   // mapped, waiting for the creator to finish or the peer to show up
            Up,        // both sides attached, same version, peer heartbeat alive
            Disabled,  // version or layout mismatch; stays disabled until restart
        };

        using LogFn = std::function<void(std::string_view)>;

        Link(Protocol::Side a_side, LogFn a_log, std::wstring a_mappingName = Protocol::kMappingName);
        ~Link();

        Link(const Link&) = delete;
        Link& operator=(const Link&) = delete;

        // Attaches, bumps our heartbeat and checks the peer. Call regularly (10+ times a second).
        void Tick(std::uint64_t a_nowMs);

        State GetState() const { return _state.load(std::memory_order_acquire); }
        bool  IsUp() const { return GetState() == State::Up; }

        // Latest-value slots. Write only the slot your side owns (Skyrim: input, actors; MSC: player, vehicle, parts, items, hand layer).
        // Read returns false while the link is down or the slot was never written.
        void WriteInput(const Protocol::InputState& a_value);
        bool ReadInput(Protocol::InputState& a_out) const;
        void WritePlayer(const Protocol::PlayerState& a_value);
        bool ReadPlayer(Protocol::PlayerState& a_out) const;
        // Vehicles have a slot each: Write picks it by a_value.vehicle, Read by a_vehicle (kVehicle*
        // id); an id outside 1..kVehicleCount writes nothing and reads false.
        void WriteVehicle(const Protocol::VehicleState& a_value);
        bool ReadVehicle(std::uint32_t a_vehicle, Protocol::VehicleState& a_out) const;
        void WriteVehicleParts(const Protocol::VehicleParts& a_value);
        bool ReadVehicleParts(std::uint32_t a_vehicle, Protocol::VehicleParts& a_out) const;
        void WriteItems(const Protocol::ItemState& a_value);
        bool ReadItems(Protocol::ItemState& a_out) const;
        void WriteHandLayer(const Protocol::HandLayer& a_value);
        bool ReadHandLayer(Protocol::HandLayer& a_out) const;
        void WriteActors(const Protocol::ActorList& a_value);
        bool ReadActors(Protocol::ActorList& a_out) const;
        void WriteParticles(const Protocol::ParticleState& a_value);
        bool ReadParticles(Protocol::ParticleState& a_out) const;

        // Outbound ring (Skyrim -> MSC for Skyrim, MSC -> Skyrim for MSC). Returns false when the
        // link is down, the ring is full or the message can never fit.
        bool Send(Protocol::MessageType a_type, const void* a_payload, std::uint32_t a_size);

        // Drains the inbound ring. a_handler(type, payload, size). Returns the message count.
        std::size_t Receive(const std::function<void(Protocol::MessageType, const std::uint8_t*, std::uint32_t)>& a_handler);

    private:
        void TryMap();
        void Detach();
        void Disable(const std::string& a_reason);
        std::uint8_t* Base() const { return _base.load(std::memory_order_acquire); }

        const Protocol::Side _side;
        const LogFn          _log;
        const std::wstring   _name;

        void*                      _mapping = nullptr;
        std::atomic<std::uint8_t*> _base{ nullptr };
        std::atomic<State>         _state{ State::Detached };
        std::atomic<bool>          _attached{ false };  // our PeerInfo is published
        bool                       _mapFailLogged = false;
        bool                       _peerSeeded = false;  // first heartbeat read only seeds, so a dead peer never looks alive
        bool                       _peerSeen = false;
        std::uint64_t              _peerHeartbeat = 0;
        std::uint64_t              _peerSeenMs = 0;
        std::vector<std::uint8_t>  _receiveBuffer;
    };
}
