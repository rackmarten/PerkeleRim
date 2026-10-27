#pragma once

#include "link.h"

// Fighting while MSC has the player. Skyrim's actors near the player go to MSC as capsules
// (ActorList) so MSC's player bumps into them, and MSC's punches that land on one (PunchEvent) hit
// it as the player: PerkeleRim.esp's punch spells, Skyrim's unarmed damage scaled
// (protocol/cpp/damage.h) and a stagger. Being the player's hostile spells, the target fights back
// and an attack on someone peaceful is assault. Also logs who attacks and hits the player, to see
// why enemies don't. Main thread only.
namespace PerkeleRim::Combat
{
    void OnDataLoaded();

    // A save loaded: Skyrim's saves keep globals, so one taken while MSC had the player would load with
    // the player's shouts and grunts still muted. Shouts mutes it again if the handover resumes.
    void OnGameLoaded();

    // Every frame MSC has the player: writes the ActorList for the current anchor.
    void Update(RE::PlayerCharacter* a_player, std::uint32_t a_generation, float a_delta);

    // A PunchEvent message. a_drunk: MSC's drunk level (0..8), drunk punches hit harder.
    void OnPunch(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size, float a_drunk);

    // An ImpactEvent message: a thrown item (hits like a punch, by its speed), the piss stream
    // (PerkeleRim.esp's poison) or the driven vehicle (knocked over, damage by speed) reached an
    // actor, or MSC's push (staggered aside, no hit).
    void OnImpact(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size);

    // The latest punch or throw Skyrim cast on an actor, for the hit test (debug/HitTest.h); count goes
    // up with each one.
    struct Hit
    {
        std::uint32_t count = 0;
        RE::FormID    actor = 0;
        bool          thrown = false;
        float         damage = 0.0f;
        float         unarmed = 0.0f;
        float         drunk = 0.0f;
        float         speed = 0.0f;
        float         sneak = 1.0f;
        float         mass = 0.0f;  // a throw's, kg (0: MSC didn't say)
    };
    const Hit& LastHit();

    // MSC's player swung (left click or H while MSC has it): a destructible within reach in front
    // takes a hit, a spider web enough to cut it (Arvel's in Bleak Falls Barrow has to be), an oil pot
    // or barricade a punch's worth. MSC's punch only lands on actors. Oil puddles on the floor are
    // left alone (a hit lights them under the player's feet).
    void OnSwing(RE::PlayerCharacter* a_player);

    // A ThrownPath message: a destructible whose bounds the thrown item's way passed through takes a
    // hit by the item's speed (Damage::ThrownObject), once per throw; a web is cut.
    void OnThrown(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size);

    // A HornEvent message: the actors in the driven vehicle's way (Damage::InHornPath, all but huge
    // ones) dive aside out of its lane as ragdolls (Damage::HornDodge).
    void OnHorn(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size);

    // A MiddleFinger message: each guard or Jarl standing within a few meters in front of the player
    // (Finger::InView) with it in sight adds a 5 gold bounty in its own hold (its crime faction),
    // once per person per minute.
    void OnMiddleFinger(RE::PlayerCharacter* a_player, const std::uint8_t* a_data, std::uint32_t a_size);

    // Every frame MSC has the player: Skyrim's shouting (Z) works as in vanilla, aimed along MSC's
    // camera, but PerkeleRim.esp's shout and grunt lines aren't the player's while its
    // PerkeleRimMuteVoice global is 1, so Skyrim's shout voice and grunts are muted; each shout released goes to MSC (PlayerShout)
    // with its words (seen while it charged, else by its cooldown), and MSC yells instead. Drunk, the
    // cooldown it started is shortened (Damage::DrunkShoutCooldown).
    void Shouts(RE::PlayerCharacter* a_player);

    // Every frame MSC has the player: keeps its hands drawn (Skyrim only blocks with them out; fists
    // unless something is equipped) and puts it in Skyrim's own block while a_block (MSC's guard,
    // kPlayerBlocking), so the Block skill, stagger and power attacks work as in vanilla. a_draw
    // false (a menu such as dialogue has the input) leaves sheathed hands sheathed. Skyrim's
    // first-person model is hidden meanwhile: MSC's hands are the ones on screen.
    void Guard(RE::PlayerCharacter* a_player, bool a_block, bool a_draw);

    // Every frame MSC has the player: the drunker (a_drunk, MSC's level 0..8), the tougher. PerkeleRim.esp's
    // drunk spells on the player, armor, max stamina and stamina regen by Damage::Drunk, recast as the
    // level moves and before they run out (10 s, so a save taken meanwhile doesn't keep them). Shouts
    // reads the level for the shout cooldown.
    void Drunk(RE::PlayerCharacter* a_player, float a_drunk, float a_delta);

    // Every frame MSC has the player: Finn fights with his fists. A weapon, spell or scroll in either
    // hand is unequipped at once, with a short notification.
    void Disarm(RE::PlayerCharacter* a_player);

    // Skyrim has the player back: MSC's capsules go, the block ends, hands this drew are sheathed, the
    // drunk bonuses go, the first-person model shows again and Skyrim's shouts and grunts are the
    // player's again.
    void Stop();
}
