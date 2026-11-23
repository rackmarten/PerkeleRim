// Where to refuel: the low-fuel line MSC sends (FuelText with kFuelTextLow) gets the nearest dwemer
// oil pump added by Skyrim, its map marker's name with how far and which way ("Whiterun Dwemer Oil
// 2.3 km north-east"), and the first dry tank of a save gets a one-time tip about the tow. Header-only
// and free of Skyrim types, so the protocol tests cover it; Skyrim's half is skse/src/fuel/Fuel.cpp.

#pragma once

#include <cmath>
#include <cstdio>
#include <span>
#include <string>
#include <string_view>

#include "coords.h"

namespace PerkeleRim::FuelHint
{
    inline constexpr std::string_view kNoName = "Dwemer Oil";  // a pump without a map marker name (an older ESP)
    inline constexpr double           kHere = 50.0;            // metres: closer than this the pump is "right here"
    inline constexpr int              kTowFee = 50;            // gold, msc/src/Link/Fuel.cs TowFee

    // Shown once a save, the first time the driven tank is dry (kFuelTextDry).
    inline constexpr std::string_view kDryTip = "Out of oil. Walk to a Dwemer Oil pump and call the vehicle: it's towed there for 50 gold.";

    struct Pump
    {
        float       x = 0.0f;  // Skyrim world space, +Y north, +X east
        float       y = 0.0f;
        std::string name;      // its map marker's ("Whiterun Dwemer Oil"), empty for none
    };

    // The pump nearest a_x, a_y on the ground plane, -1 with none.
    inline int Nearest(std::span<const Pump> a_pumps, float a_x, float a_y)
    {
        int    best = -1;
        double bestSq = 0.0;
        for (std::size_t i = 0; i < a_pumps.size(); ++i) {
            const double dx = a_pumps[i].x - a_x, dy = a_pumps[i].y - a_y;
            const double sq = dx * dx + dy * dy;
            if (best < 0 || sq < bestSq) {
                best = static_cast<int>(i);
                bestSq = sq;
            }
        }
        return best;
    }

    // Skyrim's compass bearing of a_dx, a_dy (from the player to the pump): degrees clockwise from
    // north (+Y), in [0, 360).
    inline double Bearing(double a_dx, double a_dy)
    {
        const double degrees = std::atan2(a_dx, a_dy) * 180.0 / 3.14159265358979323846;
        return degrees < 0.0 ? degrees + 360.0 : degrees;
    }

    // The eight-point compass direction a bearing falls in: "north", "north-east", ...
    inline std::string_view Direction(double a_bearing)
    {
        static constexpr std::string_view kPoints[] = { "north", "north-east", "east", "south-east", "south", "south-west", "west", "north-west" };
        const int at = static_cast<int>(std::floor(a_bearing / 45.0 + 0.5)) % 8;
        return kPoints[at < 0 ? at + 8 : at];
    }

    // A distance in Skyrim units as the HUD says it: "350 m" (to 10 m) under a kilometre, else "2.3 km".
    inline std::string Distance(double a_units)
    {
        const double metres = a_units * Coords::kMetersPerUnit;
        char         text[32];
        if (metres < 995.0) {
            std::snprintf(text, sizeof(text), "%d m", static_cast<int>(std::lround(metres / 10.0)) * 10);
        } else {
            std::snprintf(text, sizeof(text), "%.1f km", metres / 1000.0);
        }
        return text;
    }

    // Where the nearest pump is from a_x, a_y: "Whiterun Dwemer Oil 2.3 km north-east", or
    // "Whiterun Dwemer Oil right here"; empty with no pumps.
    inline std::string Where(std::span<const Pump> a_pumps, float a_x, float a_y)
    {
        const int at = Nearest(a_pumps, a_x, a_y);
        if (at < 0) {
            return {};
        }
        const auto&  pump = a_pumps[static_cast<std::size_t>(at)];
        const double dx = pump.x - a_x, dy = pump.y - a_y;
        const double units = std::sqrt(dx * dx + dy * dy);
        std::string  line(pump.name.empty() ? kNoName : std::string_view(pump.name));
        if (units * Coords::kMetersPerUnit < kHere) {
            return line + " right here";
        }
        return line + " " + Distance(units) + " " + std::string(Direction(Bearing(dx, dy)));
    }

    // MSC's low-fuel line with the nearest pump after it: "Fuel 8.3 / 40 L. Whiterun Dwemer Oil 2.3 km
    // north-east". Either half alone when the other is empty.
    inline std::string LowLine(std::string_view a_msc, std::string_view a_where)
    {
        if (a_where.empty()) {
            return std::string(a_msc);
        }
        if (a_msc.empty()) {
            return std::string(a_where);
        }
        return std::string(a_msc) + ". " + std::string(a_where);
    }
}
