// Damage rules while MSC has the player: what an MSC death costs Skyrim's health, Skyrim's own fall
// damage, which doesn't run while MSC places the player, what MSC's attacks cost Skyrim actors, how
// being drunk toughens the player, and what MSC's drinks, cigarettes and food heal. Header-only and
// free of Skyrim types, so the protocol tests cover it.

#pragma once

#include <algorithm>
#include <cmath>
#include <cctype>
#include <cstdint>

#include "../perkelerim_protocol.h"

namespace PerkeleRim::Damage
{
    // Share of Skyrim's max health one MSC death (PlayerDamage severity 1) costs. MSC kills on bumps
    // a Skyrim road is full of, so a whole death would make driving impossible.
    inline constexpr float kCrashShare = 0.5f;

    inline float Crash(float a_maxHealth, float a_severity)
    {
        return a_maxHealth * kCrashShare * (std::max)(0.0f, a_severity);
    }

    // What the player's MSC punch costs Skyrim actors: Skyrim's own unarmed damage, as Skyrim works it
    // out (the race's, Fortify Unarmed, perks such as Fists of Steel, the difficulty; the plugin does
    // that part), times kPunchScale, so fights scale like Skyrim's as the world levels up. Vanilla
    // unarmed is weak (4 for most races, 10 Argonians, 22 Khajiit): scaled, a sober level 1 Nord
    // punches about as hard as an iron sword hits (8), three for a wolf, six or seven for a soldier.
    inline constexpr float kPunchScale = 2.0f;
    // Drinking pays off in a fight (MSC's drunk camera wobble makes aiming hard). MSC's drunk level
    // (0..8, by the player's weight) climbs slowly: five beers are about 0.4, two dozen leave Finn
    // staggering around 2.5 (MSC's drowning risk), a few vodkas more black him out. Everything
    // drunk scales up to kDrunkLevels: the punch to kDrunkPunchMax times the sober one, and the
    // toughness (DrunkBuffs) to its max.
    inline constexpr float kDrunkLevels = 3.0f;
    inline constexpr float kDrunkPunchMax = 2.0f;

    // How far along to full drunk, 0..1.
    inline float DrunkShare(float a_drunk)
    {
        return (std::clamp)(a_drunk / kDrunkLevels, 0.0f, 1.0f);
    }

    inline float DrunkPunch(float a_drunk)
    {
        return 1.0f + (kDrunkPunchMax - 1.0f) * DrunkShare(a_drunk);
    }

    // a_unarmed: the player's unarmed damage as Skyrim works it out.
    inline float Punch(float a_unarmed, float a_drunk = 0.0f)
    {
        return (std::max)(0.0f, a_unarmed) * kPunchScale * DrunkPunch(a_drunk);
    }

    // The drunker, the tougher, while MSC has the player: temporary bonuses to armor rating (Skyrim's
    // DamageResist, so it stacks with worn armor under Skyrim's cap), max stamina and stamina regen
    // (StaminaRateMult, percent), rising with DrunkShare up to these. The shout cooldown shortens the
    // same way, by up to kDrunkShoutRecoveryCut, so a fully drunk Finn shouts three times as often.
    inline constexpr float kDrunkArmorMax = 200.0f;        // about a quarter less physical damage
    inline constexpr float kDrunkStaminaMax = 100.0f;
    inline constexpr float kDrunkStaminaRateMax = 100.0f;  // twice the regen
    inline constexpr float kDrunkShoutRecoveryCut = 2.0f / 3.0f;

    struct DrunkBuffs
    {
        float armor = 0.0f;
        float stamina = 0.0f;
        float staminaRate = 0.0f;
    };

    inline DrunkBuffs Drunk(float a_drunk)
    {
        const float share = DrunkShare(a_drunk);
        return { kDrunkArmorMax * share, kDrunkStaminaMax * share, kDrunkStaminaRateMax * share };
    }

    // A shout's cooldown in seconds at a_drunk: Skyrim's own (a_cooldown, the shout's recovery time
    // times the player's ShoutRecoveryMult) shortened.
    inline float DrunkShoutCooldown(float a_cooldown, float a_drunk)
    {
        return a_cooldown * (1.0f - kDrunkShoutRecoveryCut * DrunkShare(a_drunk));
    }

    // Recast once the drunk level moves this much from what the bonuses were cast for.
    inline constexpr float kDrunkBuffStep = 0.1f;  // drunk levels

    // A thrown item hits by its speed: nothing at kThrownSpeedMin, twice a sober punch from kThrownSpeedFull
    // on. MSC's own throw lands in between; a bottle, thrown twice as fast (msc/src/Link/Throwing.cs),
    // hits like two punches from range. a_punch: the player's sober Punch.
    inline constexpr float kThrownSpeedMin = 2.0f;    // meters per second
    inline constexpr float kThrownSpeedFull = 15.0f;

    inline constexpr float kThrownShare = 2.0f;  // of the punch at full speed: aiming a throw is hard enough

    // And by its weight (v46): MSC's empty beer bottle (kThrownMassBase) hits as above, a lighter or
    // heavier item by the square root of its mass against the bottle's, kept within kThrownMassMin to
    // kThrownMassMax times: Skyrim's junk weighs its weight in kilograms (junk.h), so a cabbage (0.25)
    // hits 0.7 times as hard, an item of weight 2 twice, a beer case (MSC's, about 10 kg) three times.
    // a_mass 0 (unknown) counts as the bottle.
    inline constexpr float kThrownMassBase = 0.5f;  // kilograms, MSC's BottleBeerFly
    inline constexpr float kThrownMassMin = 0.5f;
    inline constexpr float kThrownMassMax = 3.0f;

    inline float ThrownMass(float a_mass)
    {
        if (!(a_mass > 0.0f)) {
            return 1.0f;
        }
        return (std::clamp)(std::sqrt(a_mass / kThrownMassBase), kThrownMassMin, kThrownMassMax);
    }

    inline float Thrown(float a_punch, float a_speed, float a_mass = 0.0f)
    {
        const float scale = (std::clamp)((a_speed - kThrownSpeedMin) / (kThrownSpeedFull - kThrownSpeedMin), 0.0f, 1.0f);
        return (std::max)(0.0f, a_punch) * kThrownShare * scale * ThrownMass(a_mass);
    }

    // A throw that left the player's hand while he sneaked and hits a target that does not see him
    // (Skyrim's detection level 0 or below, Actor::RequestDetectionLevel) is a sneak attack: kSneakAttack
    // times the damage, on top of the charge and speed (Thrown). Punches are spells in Skyrim, which
    // have no sneak attack, and stay as they are.
    inline constexpr float kSneakAttack = 2.0f;

    inline float SneakAttack(bool a_sneaking, int a_detection)
    {
        return a_sneaking && a_detection <= 0 ? kSneakAttack : 1.0f;
    }

    // Skyrim's destructibles (Combat::OnSwing, Combat::OnThrown): a spider web takes more than any
    // web's health, so one hit cuts it; others (oil pots, barricades) a punch's worth, from a thrown
    // item by its speed like Thrown.
    inline constexpr float kWebDamage = 1000.0f;
    inline constexpr float kObjectDamage = 50.0f;

    inline float ThrownObject(bool a_web, float a_speed)
    {
        if (a_speed < kThrownSpeedMin) {
            return 0.0f;
        }
        return a_web ? kWebDamage : (std::max)(1.0f, Thrown(kObjectDamage, a_speed));
    }

    // Distance from a_point to the segment a_from..a_to (any units).
    inline float SegmentDistance(const float a_from[3], const float a_to[3], const float a_point[3])
    {
        float way[3], to[3];
        float length2 = 0.0f, along = 0.0f;
        for (int i = 0; i < 3; i++) {
            way[i] = a_to[i] - a_from[i];
            to[i] = a_point[i] - a_from[i];
            length2 += way[i] * way[i];
            along += way[i] * to[i];
        }
        const float t = length2 > 1e-12f ? (std::clamp)(along / length2, 0.0f, 1.0f) : 0.0f;
        float d2 = 0.0f;
        for (int i = 0; i < 3; i++) {
            const float d = to[i] - way[i] * t;
            d2 += d * d;
        }
        return std::sqrt(d2);
    }

    // Shares of max health below (running over, piss) leave dragons this fraction.
    inline constexpr float kDragonFactor = 0.25f;

    // MSC's push (J) on an actor shoves it out of the way: Skyrim's stagger animation, no hit, so no
    // damage and no aggression. Skyrim's graph variable staggerDirection is where the shove comes
    // from, around the actor: 0 in front of it, 0.25 its right, 0.5 behind, 0.75 its left. a_heading:
    // the actor's (radians, clockwise from +Y like Skyrim's angle Z); a_x, a_y: the way it is pushed.
    inline constexpr float kPushStagger = 0.3f;  // staggerMagnitude, a small stumble

    inline float StaggerDirection(float a_heading, float a_x, float a_y)
    {
        constexpr float kTwoPi = 6.2831853f;
        if (a_x == 0.0f && a_y == 0.0f) {
            return 0.0f;
        }
        const float from = std::atan2(-a_x, -a_y);  // where the push comes from, as a heading
        float       turn = std::fmod(from - a_heading, kTwoPi) / kTwoPi;
        if (turn < 0.0f) {
            turn += 1.0f;
        }
        return turn >= 1.0f ? 0.0f : turn;
    }

    // Run over by the vehicle MSC's player drives: nothing below kRunOverSpeedMin (a nudge while
    // parking), then a share of max health rising with speed, lethal from kRunOverSpeedLethal
    // (50 km/h) on. Dragons take a fraction, so they survive. Every hit but on a huge one knocks the
    // target over (KnockOver), then its ragdoll is thrown along the vehicle's way at kThrowShare of
    // its speed plus kThrowLift of it upward: the vehicle has no collision in Skyrim, so without the
    // throw the body would just drop and the van pass through it.
    inline constexpr float kRunOverSpeedMin = 2.0f;      // meters per second, about 7 km/h
    inline constexpr float kRunOverSpeedLethal = 50.0f / 3.6f;  // 50 km/h
    inline constexpr float kRunOverLethalShare = 2.0f;   // past fortified health too
    inline constexpr float kKnockPerSpeed = 0.5f;        // knock magnitude per m/s (PushActorAway scale)
    inline constexpr float kKnockMax = 15.0f;
    inline constexpr float kThrowShare = 1.2f;
    inline constexpr float kThrowLift = 0.35f;

    // What a vehicle does to an actor depends on its weight, guessed from its bounds (units, scale
    // applied) with a person's density: light ones (people, wolves) and heavy ones (bears, cows,
    // horses, elk) are run over and flung, the vehicle passes through; huge ones (mammoths, every
    // dragon) stop it like a wall and don't even fall.
    inline constexpr float kMassPerCubicUnit = 80.0f / (44.0f * 28.0f * 128.0f);  // a person's bounds weigh 80 kg
    inline constexpr float kHeavyMass = 250.0f;  // kilograms
    inline constexpr float kHugeMass = 2000.0f;

    enum class Bulk
    {
        kLight,
        kHeavy,
        kHuge
    };

    inline float Mass(float a_x, float a_y, float a_z)
    {
        return (std::max)(0.0f, a_x) * (std::max)(0.0f, a_y) * (std::max)(0.0f, a_z) * kMassPerCubicUnit;
    }

    inline Bulk BulkOf(float a_mass, bool a_dragon)
    {
        return a_dragon || a_mass >= kHugeMass ? Bulk::kHuge : a_mass >= kHeavyMass ? Bulk::kHeavy : Bulk::kLight;
    }

    inline float RunOver(float a_targetMaxHealth, bool a_dragon, float a_speed)
    {
        if (a_speed < kRunOverSpeedMin) {
            return 0.0f;
        }
        const float share = a_speed >= kRunOverSpeedLethal ? kRunOverLethalShare :
                                                             (a_speed - kRunOverSpeedMin) / (kRunOverSpeedLethal - kRunOverSpeedMin);
        return (std::max)(0.0f, a_targetMaxHealth) * share * (a_dragon ? kDragonFactor : 1.0f);
    }

    inline float KnockOver(float a_speed)
    {
        return (std::clamp)(a_speed * kKnockPerSpeed, 1.0f, kKnockMax);
    }

    // The horn (HornEvent): actors in the vehicle's way dive aside (HornDodge), so they stop
    // strolling under its wheels. In its way: within kHornUnits and either closer than kHornNearUnits or ahead of
    // it (a_way, flat) no further than kHornLaneUnits off its line. a_dx, a_dy: actor minus vehicle.
    inline constexpr float kHornUnits = 800.0f;      // about 11 m
    inline constexpr float kHornNearUnits = 350.0f;  // 5 m
    inline constexpr float kHornLaneUnits = 500.0f;  // 7 m either side

    inline bool InHornPath(float a_dx, float a_dy, float a_wayX, float a_wayY)
    {
        const float distance = std::hypot(a_dx, a_dy);
        if (distance > kHornUnits) {
            return false;
        }
        const float way = std::hypot(a_wayX, a_wayY);
        if (distance < kHornNearUnits || way < 1e-4f) {
            return true;
        }
        const float ahead = (a_dx * a_wayX + a_dy * a_wayY) / way;
        const float aside = std::abs(a_dx * a_wayY - a_dy * a_wayX) / way;
        return ahead > 0.0f && aside <= kHornLaneUnits;
    }

    // An actor the horn reaches dives aside (a Skyrim fear spell moved nobody and was the likely
    // cause of some dying later): knocked down lightly (kHornDiveKnock) and its ragdoll thrown
    // square to the vehicle's way, toward the side it already stands on, plus kHornDiveLift upward. It lands kHornDiveShare of the way to
    // kHornClearUnits past the lane (at least kHornDodgeMinUnits), flying about kHornDiveSeconds.
    // Returns that move, flat, in units; a_dx, a_dy as for InHornPath. No way: away from the vehicle.
    inline constexpr float kHornDiveSeconds = 0.6f;
    inline constexpr float kHornDiveLift = 3.0f;    // meters per second, upward
    inline constexpr float kHornDiveKnock = 1.0f;   // KnockExplosion magnitude, the least there is
    inline constexpr float kHornDiveShare = 1.0f / 3.0f;
    inline constexpr float kHornClearUnits = 100.0f;
    inline constexpr float kHornDodgeMinUnits = 50.0f;

    inline void HornDodge(float a_dx, float a_dy, float a_wayX, float a_wayY, float& a_outX, float& a_outY)
    {
        const float way = std::hypot(a_wayX, a_wayY);
        float       sideX, sideY, aside;
        if (way < 1e-4f) {
            const float distance = std::hypot(a_dx, a_dy);
            sideX = distance > 1e-4f ? a_dx / distance : 1.0f;
            sideY = distance > 1e-4f ? a_dy / distance : 0.0f;
            aside = distance;
        } else {
            // Right of the way is (y, -x); the actor's side by the sign of its cross product.
            const float cross = a_dx * a_wayY - a_dy * a_wayX;
            const float sign = cross >= 0.0f ? 1.0f : -1.0f;
            sideX = sign * a_wayY / way;
            sideY = -sign * a_wayX / way;
            aside = std::abs(cross) / way;
        }
        const float move = (std::max)(kHornDodgeMinUnits, (kHornLaneUnits + kHornClearUnits - aside) * kHornDiveShare);
        a_outX = sideX * move;
        a_outY = sideY * move;
    }

    // Piss is poison: while the stream stays on an actor it gets a damage over time, a share of its
    // max health per second for kPissSeconds after the last hit. Dragons take a fraction of it.
    inline constexpr float kPissSharePerSecond = 0.02f;
    inline constexpr float kPissSeconds = 5.0f;

    inline float PissPerSecond(float a_targetMaxHealth, bool a_dragon)
    {
        return (std::max)(0.0f, a_targetMaxHealth) * kPissSharePerSecond * (a_dragon ? kDragonFactor : 1.0f);
    }

    // What one Consumed heals, as a share of the player's max health. Finn drinks by the dozen, so a
    // drink heals a little and a meal more.
    inline float HealShare(std::uint32_t a_kind)
    {
        switch (a_kind) {
        case Protocol::kConsumedBeer:
            return 0.03f;
        case Protocol::kConsumedShot:
            return 0.03f;
        case Protocol::kConsumedKilju:
            return 0.05f;
        case Protocol::kConsumedBooze:
            return 0.06f;
        case Protocol::kConsumedSpirit:
            return 0.08f;
        case Protocol::kConsumedCigarette:
            return 0.02f;
        case Protocol::kConsumedFood:
            return 0.15f;
        default:
            return 0.0f;
        }
    }

    // Health one Consumed restores, never past the max.
    inline float Heal(std::uint32_t a_kind, float a_maxHealth, float a_health)
    {
        const float max = (std::max)(0.0f, a_maxHealth);
        return (std::max)(0.0f, (std::min)(max * HealShare(a_kind), max - a_health));
    }

    // Skyrim's player fall damage: ((height - fJumpFallHeightMin) * fJumpFallHeightMult) ^
    // fJumpFallHeightExponent, nothing below the minimum. Game units.
    struct FallSettings
    {
        float min = 600.0f;
        float mult = 0.1f;
        float exponent = 1.45f;
    };

    inline float Fall(float a_height, const FallSettings& a_settings = {})
    {
        if (a_height <= a_settings.min) {
            return 0.0f;
        }
        return std::pow((a_height - a_settings.min) * a_settings.mult, a_settings.exponent);
    }

    // Seated in the Hayosiko van, the Kekmet or the Gifu the player is safe from enemies (a stopgap until the
    // vehicles get a real hitbox in Skyrim): dragon fire, bites, arrows, spells, melee do nothing.
    // The moped doesn't protect, and MSC's own crash damage still lands (Death::Hurt).
    inline bool SafeSeat(std::uint32_t a_vehicle)
    {
        return a_vehicle == Protocol::kVehicleHayosiko || a_vehicle == Protocol::kVehicleKekmet || a_vehicle == Protocol::kVehicleGifu;
    }

    // What a hit that Skyrim deals the player costs while it sits in a_vehicle (0 = on foot).
    inline float Incoming(std::uint32_t a_vehicle, float a_damage)
    {
        return SafeSeat(a_vehicle) ? 0.0f : a_damage;
    }

    // When god mode is on for a safe seat. God mode is the game's own global byte, so the player's might
    // already have it on (the console's tgm): that is never touched. What this turned on, it turns off
    // again, and keeps turning it off until the byte reads off, so a flicker out of the seat and back
    // (seated for one frame less) can't leave it on for good, which made every enemy harmless on foot.
    class SeatGod
    {
    public:
        enum class Action { None, Enable, Disable };

        // a_vehicle: what the player sits in (0 = on foot); a_godNow: the byte as it reads now.
        Action Step(std::uint32_t a_vehicle, bool a_godNow)
        {
            if (SafeSeat(a_vehicle)) {
                if (_ours || _clearing) {
                    _ours = true;
                    _clearing = false;
                    return a_godNow ? Action::None : Action::Enable;
                }
                if (a_godNow) {
                    return Action::None;  // not ours
                }
                _ours = true;
                return Action::Enable;
            }
            if (_ours) {
                _ours = false;
                _clearing = true;
                return Action::Disable;
            }
            if (_clearing) {
                if (a_godNow) {
                    return Action::Disable;
                }
                _clearing = false;
            }
            return Action::None;
        }

        // God mode is on because of the seat (OwnDamage lifts it for a moment).
        bool Ours() const { return _ours; }

    private:
        bool _ours = false;
        bool _clearing = false;
    };

    // Animation events that throw a seated player out of PlayerSync's seat placement: staggers,
    // recoils, knockdowns and ragdolls start from these. Matched case-insensitively by prefix.
    inline bool PullsOutOfSeat(const char* a_event)
    {
        if (!a_event) {
            return false;
        }
        constexpr const char* kPrefixes[] = { "stagger", "recoil", "knock", "ragdoll", "pushactor", "bleedout" };
        for (const char* prefix : kPrefixes) {
            const char* e = a_event;
            const char* p = prefix;
            while (*p && *e && std::tolower(static_cast<unsigned char>(*e)) == *p) {
                e++;
                p++;
            }
            if (!*p) {
                return true;
            }
        }
        return false;
    }

    // Follows MSC's player off the ground and back: the height fallen is from the highest point
    // while airborne to where it lands. Reset whenever the pose can't be trusted for a fall (a new
    // anchor, seated, no pose).
    class FallTracker
    {
    public:
        // One pose: its feet height and whether MSC says it stands on something. Returns the
        // height fallen when this pose lands, else 0.
        float Update(float a_z, bool a_grounded)
        {
            if (!_known) {
                _known = true;
                _airborne = !a_grounded;
                _top = _last = a_z;
                return 0.0f;
            }
            if (!a_grounded) {
                // Leaving the ground starts from where the player last stood.
                _top = (std::max)(_airborne ? _top : _last, a_z);
                _airborne = true;
                _last = a_z;
                return 0.0f;
            }
            const float fallen = _airborne ? _top - a_z : 0.0f;
            _airborne = false;
            _last = a_z;
            return (std::max)(0.0f, fallen);
        }

        void Reset() { _known = false; }

    private:
        bool  _known = false;
        bool  _airborne = false;
        float _top = 0.0f;
        float _last = 0.0f;
    };
}
