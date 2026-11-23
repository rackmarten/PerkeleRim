#pragma once

#include "teimo_talk.h"

#include <optional>
#include <string>
#include <string_view>

// Teimo at his makeshift stall in Riverwood (PerkeleRim.esp, tools/esp-gen): a Skyrim actor
// standing behind the counter, drawn as MSC's own Teimo (TeimoModel, read from the running MSC and
// built in memory, nothing shipped) with his Skyrim body hidden while it is; without MSC's model he
// stays a plain Nord. He turns to Kumpo when he comes close and talks to him as he does in My
// Summer Car: MSC plays its own Teimo clips from where he stands (TeimoLine), and Skyrim captions
// them on the subtitle line. The rules (when he says what) are in protocol/cpp/teimo_talk.h. Main
// thread, except OnTalk (the dialogue hook may run on Skyrim's AI threads).
namespace PerkeleRim::Teimo
{
    void OnDataLoaded();  // finds him in PerkeleRim.esp; call on kDataLoaded
    void OnGameLoaded();  // a load: he forgets the visit; call on kPostLoadGame and kNewGame

    // Every frame, whoever has the player: his talk's clock, and his MSC model on him, turned
    // toward Kumpo or his stall (his Skyrim body instead while he's down, fighting or off his spot).
    void Draw(RE::PlayerCharacter* a_player, float a_delta);

    // Every frame MSC has the player, before that frame's Draw: what Teimo says to him. a_drunk is
    // MSC's drunk level; a_inVehicle: Kumpo sits in a vehicle or has climbed into one (no talk, and
    // the visit ends: driving past isn't a visit).
    void Update(RE::PlayerCharacter* a_player, float a_drunk, bool a_inVehicle);

    bool IsTeimo(const RE::TESObjectREFR* a_ref);

    // Combat's events while MSC has the player: a_target was punched, hit by something thrown or
    // run over (a_piss: the piss stream reached it); the finger pointed along a_dirX, a_dirY.
    void OnHit(RE::PlayerCharacter* a_player, RE::Actor* a_target, bool a_piss);
    void OnFinger(RE::PlayerCharacter* a_player, float a_dirX, float a_dirY);

    // The dialogue hook (People): a_actor starts a dialogue with the player. True for Teimo, who
    // never opens Skyrim's dialogue (his talk is MSC's lines, whoever has the player): while MSC has
    // Kumpo at the stall he greets him instead, next frame.
    bool OnTalk(const RE::Actor* a_actor);

    // His caption still showing ("Teimo: ..."), or empty.
    std::string CurrentCaption();

    // For the test kit (pr.teimo).
    struct Status
    {
        bool          found = false;   // PerkeleRim.esp has him
        bool          loaded = false;  // his 3D is in the world
        bool          drawn = false;   // as MSC's Teimo
        std::uint32_t model = 0;       // MSC's model id for him, 0 = none arrived
        RE::NiPoint3  position;
        float         heading = 0.0f;  // the way the model faces, radians
        float         home = 0.0f;     // the way his stall faces (his reference's angle), radians
        float         distance = -1.0f;  // flat units to the player
        float         offPlayer = 0.0f;  // radians between the way he faces and the way to the player
        bool          visiting = false;  // Kumpo is at the stall (Talk::Visiting)
        std::string   lastClip;          // the last line he said, and when (seconds of his clock)
        std::string   lastTopic;
        double        lastAt = -1.0;
        std::uint32_t lines = 0;  // lines said since the game loaded
        std::string   caption;
    };
    Status       Describe();
    RE::NiNode*  Node();  // what he's drawn with, while he is
    RE::Actor*   Actor();
    // Says a line of a_topic now, as if its event came (the scenario's caption shot). False while
    // he can't (not loaded, MSC not linked).
    bool         DebugSay(RE::PlayerCharacter* a_player, Topic a_topic);
    std::string_view TopicName(Topic a_topic);
    std::optional<Topic> TopicByName(std::string_view a_name);
}
