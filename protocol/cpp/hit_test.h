// The hit test's rules (skse/src/debug/HitTest.cpp drives it in game, scripts/hit-test.ps1 starts
// it): its scenarios, what a hit that landed must have cost by damage.h, and the CSV rows it
// writes. Header-only and free of Skyrim types, so the protocol tests cover it.

#pragma once

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdio>
#include <string>
#include <string_view>
#include <vector>

#include "damage.h"
#include "needs.h"

namespace PerkeleRim::HitTest
{
    enum class Attack
    {
        kPunch,  // MSC's Hit key (H)
        kThrow,  // an empty bottle in the hand, thrown with the right button
        kJunk,   // no empty bottles: key 3 takes junk out of Skyrim's inventory (a cabbage), thrown the same way
    };

    struct Scenario
    {
        std::string_view name;
        Attack           attack;
        float            drunk;  // MSC's drunk level (PlayerDrunk) set before the hits
        int              hits;
        float            gap;    // meters between the player's capsule and the target's
    };

    // Drunk 2, not 3: MSC's player sways so much around 2.5 that the camera is hard to keep level.
    // Drunk goes last: MSC's camera keeps swaying for a while after the level is back at 0 (Drunk
    // Mode eases to random targets), and a throw then misses a capsule 3 m away. A drunk bottle
    // (Damage::DrunkThrow, 192 and 400 for a Nord) may kill the guard at once: the next hit is skipped.
    // Drunk throws go from 1.5 m: at 3 m the camera's sway during the charge missed the capsule.
    inline constexpr std::array kScenarios = {
        Scenario{ "punch", Attack::kPunch, 0.0f, 3, 0.4f },
        Scenario{ "bottle", Attack::kThrow, 0.0f, 3, 3.0f },
        Scenario{ "junk", Attack::kJunk, 0.0f, 3, 3.0f },
        Scenario{ "drunk", Attack::kPunch, 2.0f, 3, 0.4f },
        Scenario{ "drunk-bottle", Attack::kThrow, 1.0f, 2, 1.5f },
        Scenario{ "drunker-bottle", Attack::kThrow, 2.0f, 2, 1.5f },
    };

    // "all", or scenario names separated by commas (spaces ignored, case matters), in the order
    // given. A name that isn't a scenario goes to a_unknown.
    inline std::vector<Scenario> Parse(std::string_view a_list, std::vector<std::string>* a_unknown = nullptr)
    {
        std::vector<Scenario> picked;
        while (!a_list.empty()) {
            const auto comma = a_list.find(',');
            auto       name = a_list.substr(0, comma);
            a_list = comma == std::string_view::npos ? std::string_view() : a_list.substr(comma + 1);
            while (!name.empty() && (name.front() == ' ' || name.front() == '\t')) {
                name.remove_prefix(1);
            }
            while (!name.empty() && (name.back() == ' ' || name.back() == '\t' || name.back() == '\r')) {
                name.remove_suffix(1);
            }
            if (name.empty()) {
                continue;
            }
            if (name == "all") {
                picked.insert(picked.end(), kScenarios.begin(), kScenarios.end());
                continue;
            }
            const auto found = std::find_if(kScenarios.begin(), kScenarios.end(), [&](const Scenario& a_s) { return a_s.name == name; });
            if (found != kScenarios.end()) {
                picked.push_back(*found);
            } else if (a_unknown) {
                a_unknown->emplace_back(name);
            }
        }
        return picked;
    }

    // One attack and what came of it.
    struct Hit
    {
        bool  landed = false;  // a PunchEvent or ImpactEvent on the target reached Skyrim
        float drunk = 0.0f;    // MSC's drunk level Skyrim punched or threw with
        float unarmed = 0.0f;  // the player's unarmed damage as Skyrim worked it out
        float speed = 0.0f;    // a throw's, m/s
        float mass = 0.0f;     // a throw's, kg (0: MSC didn't say)
        float sneak = 1.0f;    // a throw's sneak attack multiplier
        float stress = 0.0f;   // MSC's stress Skyrim punched with (Needs::StressPunch)
        float damage = 0.0f;   // what Skyrim cast on the target
        float before = 0.0f;   // the target's health before the attack
        float after = 0.0f;    // and once the spell had landed
    };

    // What damage.h says the hit should have cost.
    inline float Expected(const Scenario& a_scenario, const Hit& a_hit)
    {
        if (a_scenario.attack == Attack::kPunch) {
            return Damage::Punch(a_hit.unarmed, a_hit.drunk) * Needs::StressPunch(a_hit.stress);
        }
        return Damage::Thrown(Damage::Punch(a_hit.unarmed), a_hit.speed, a_hit.mass, a_hit.drunk) * a_hit.sneak;
    }

    // Damage and health are compared within 10%, at least 1 point: health regenerates a little
    // between the reads.
    inline bool Near(float a_value, float a_wanted)
    {
        return std::abs(a_value - a_wanted) <= (std::max)(1.0f, 0.1f * std::abs(a_wanted));
    }

    struct Verdict
    {
        bool        pass = false;
        std::string note;
    };

    inline std::string Number(float a_value)
    {
        char text[32];
        std::snprintf(text, sizeof(text), "%.1f", a_value);
        return text;
    }

    inline Verdict Check(const Scenario& a_scenario, const Hit& a_hit)
    {
        if (!a_hit.landed) {
            return { false, a_scenario.attack == Attack::kPunch ? "no punch reached Skyrim" : "no throw reached Skyrim" };
        }
        if (a_scenario.drunk > 0.0f && a_hit.drunk < 0.5f * a_scenario.drunk) {
            return { false, "MSC's drunk level was " + Number(a_hit.drunk) + ", not " + Number(a_scenario.drunk) };
        }
        const float expected = Expected(a_scenario, a_hit);
        if (expected <= 0.0f) {
            return { false, "harmless: thrown at " + Number(a_hit.speed) + " m/s" };
        }
        if (!Near(a_hit.damage, expected)) {
            return { false, "cast " + Number(a_hit.damage) + ", damage.h says " + Number(expected) };
        }
        const float drop = a_hit.before - a_hit.after;
        if (a_hit.after <= 0.0f && a_hit.before > 0.0f && a_hit.damage >= a_hit.before) {
            return { true, "killed it" };  // health may stop at 0, short of the damage cast
        }
        if (!Near(drop, a_hit.damage)) {
            return { false, "health fell by " + Number(drop) + ", not the " + Number(a_hit.damage) + " cast" };
        }
        return { true, "" };
    }

    // The drunk punches against the sober ones: their average damage in the ratio Damage::DrunkPunch
    // says for the drunk level they were thrown at.
    inline Verdict CheckDrunk(float a_sober, float a_drunkDamage, float a_drunk)
    {
        if (a_sober <= 0.0f || a_drunkDamage <= 0.0f) {
            return { false, "needs a landed punch and a landed drunk punch" };
        }
        const float ratio = a_drunkDamage / a_sober;
        const float wanted = Damage::DrunkPunch(a_drunk);
        if (std::abs(ratio - wanted) > 0.05f * wanted) {
            return { false, "drunk hits x" + Number(ratio) + " the sober ones, damage.h says x" + Number(wanted) };
        }
        return { true, "x" + Number(ratio) + " at drunk " + Number(a_drunk) };
    }

    inline constexpr std::string_view kCsvHeader = "scenario,hit,target,landed,drunk,unarmed,speed,mass,sneak,expected,damage,before,after,drop,pass,note";

    // A CSV field: quoted when it holds a comma, a quote or a line break.
    inline std::string Field(std::string_view a_text)
    {
        if (a_text.find_first_of(",\"\r\n") == std::string_view::npos) {
            return std::string(a_text);
        }
        std::string quoted = "\"";
        for (const char c : a_text) {
            quoted += c;
            if (c == '"') {
                quoted += '"';
            }
        }
        return quoted + '"';
    }

    inline std::string Row(const Scenario& a_scenario, int a_hit, std::string_view a_target, const Hit& a_result, const Verdict& a_verdict)
    {
        std::string row = Field(a_scenario.name) + ',' + std::to_string(a_hit) + ',' + Field(a_target) + ',' + (a_result.landed ? "1" : "0");
        for (const float value : { a_result.drunk, a_result.unarmed, a_result.speed, a_result.mass, a_result.sneak, Expected(a_scenario, a_result), a_result.damage,
                 a_result.before, a_result.after, a_result.before - a_result.after }) {
            row += ',' + Number(value);
        }
        return row + ',' + (a_verdict.pass ? "1" : "0") + ',' + Field(a_verdict.note);
    }

    // A row for a check across scenarios (CheckDrunk): only its name, pass and note.
    inline std::string SummaryRow(std::string_view a_name, const Verdict& a_verdict)
    {
        return Field(a_name) + ",,,,,,,,,,,,,," + (a_verdict.pass ? "1" : "0") + ',' + Field(a_verdict.note);
    }
}
