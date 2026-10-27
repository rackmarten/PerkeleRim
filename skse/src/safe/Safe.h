#pragma once

// A safe seat: while the player sits in the Hayosiko van or the Kekmet, or has climbed into one,
// enemies can't hurt it. The vehicles' hitboxes stop what comes through the body, but Skyrim's player
// stands at full height where MSC seats it, its head out of the roof. Dragon fire, bites, arrows, spells and
// melee all do nothing: god mode is on for as long as the seat lasts (and given back as it was),
// and the player's own health damage entry drops hits from attackers. Staggers, recoils, knockdowns
// and ragdolls are dropped at their animation events, so they never pull the player out of
// PlayerSync's seat placement. The moped protects nothing, and MSC's own crash damage still lands
// (Death::Hurt holds OwnDamage while it works). The rules are in protocol/cpp/damage.h. Main thread.
namespace PerkeleRim::Safe
{
    void Install();  // the hooks; call once, with the plugin's other vtable hooks

    // Every frame while MSC has the player: the vehicle id the player sits in or has climbed into
    // (0 = on foot). Off for anything but a safe seat; call with 0 when MSC lets go of the player.
    void Set(RE::PlayerCharacter* a_player, std::uint32_t a_vehicle);

    // The vehicle that keeps the player safe right now (hits on it land on the vehicle), 0 for none.
    std::uint32_t Seat();

    // While alive, damage from MSC itself (a crash) goes through even in a safe seat.
    class OwnDamage
    {
    public:
        OwnDamage();
        ~OwnDamage();
        OwnDamage(const OwnDamage&) = delete;
        OwnDamage& operator=(const OwnDamage&) = delete;

    private:
        bool _lifted = false;
    };
}
