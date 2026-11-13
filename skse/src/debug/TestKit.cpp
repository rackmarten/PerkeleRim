// Built only with PERKELERIM_TEST_KIT (dev builds); TestKitOff.cpp stands in otherwise.
#include "debug/TestKit.h"

#include "Ini.h"
#include "debug/HitTest.h"
#include "link/LinkService.h"
#include "look_at.h"
#include "model/ModelView.h"
#include "player/PlayerSync.h"
#include "vehicle/Hitbox.h"
#include "vehicle/HitboxTest.h"
#include "vehicle/VehicleView.h"

#include <skse_test_kit/Kit.h>

#include <array>
#include <cstdlib>
#include <functional>
#include <cmath>
#include <memory>
#include <optional>

namespace PerkeleRim::TestKit
{
    namespace
    {
        using SkseTestKit::Call;
        using SkseTestKit::Error;
        using SkseTestKit::Json;
        using SkseTestKit::JsonArray;
        using SkseTestKit::JsonObject;
        using SkseTestKit::Step;

        constexpr float kModelQuiet = 4.0f;    // seconds without a ModelChunk: MSC has sent its models (a vehicle's freezes it)
        constexpr float kModelWait = 60.0f;    // and ready anyway after this long standing
        constexpr float kHandAgain = 5.0f;     // seconds between handovers asked for while play mode hasn't done it

        bool g_active = false;
        bool g_exiting = false;  // the process is tearing down: the game can't be driven any more

        // Runs once when the last copy of a step goes: a request that ends, times out or whose client goes (the kit
        // drops its step then). For what a task started that would outlive it: a test driver, a held key. Harmless
        // after a normal end, since each of them does nothing then.
        class OnDrop
        {
        public:
            explicit OnDrop(std::function<void()> a_what) : _what(std::move(a_what)) {}
            ~OnDrop()
            {
                if (_what && !g_exiting) {  // not while the kit's own statics go as Skyrim quits
                    _what();
                }
            }
            OnDrop(const OnDrop&) = delete;
            OnDrop& operator=(const OnDrop&) = delete;

        private:
            std::function<void()> _what;
        };

        std::shared_ptr<OnDrop> Dropping(std::function<void()> a_what)
        {
            return std::make_shared<OnDrop>(std::move(a_what));
        }

        // [Test] MSC=1: the session is ready once MSC has the player standing on its island and has gone quiet sending
        // its models (it reads each in one frame, and keys held meanwhile never reach it).
        struct Readiness
        {
            bool          msc = false;
            float         standing = 0.0f;  // seconds MSC has had him standing
            float         quiet = 0.0f;     // seconds since the last model chunk
            float         unowned = 0.0f;   // seconds MSC hasn't had him, for a handover of our own
            std::uint32_t chunks = 0;
            bool          logged = false;
        } g_ready;

        float FrameSeconds()
        {
            const float delta = RE::GetSecondsSinceLastFrame();
            return delta > 0.0f && delta < 1.0f ? delta : 1.0f / 60.0f;
        }

        void Arrived(const SkseTestKit::Settings& a_settings)
        {
            const auto msc = a_settings.values.find("MSC");
            g_ready = {};
            g_ready.msc = msc != a_settings.values.end() && msc->second == "1";
        }

        bool MscReady()
        {
            if (!g_ready.msc) {
                return true;
            }
            const float delta = FrameSeconds();
            if (!PlayerSync::Owned()) {
                // Play mode hands the player over by itself once the save has settled and MSC is in its game; ask
                // again as F10 does should it have given up (MSC came up after Skyrim, say).
                g_ready.standing = 0.0f;
                g_ready.unowned += delta;
                if (g_ready.unowned >= kHandAgain && LinkService::Get().IsUp()) {
                    g_ready.unowned = 0.0f;
                    SKSE::log::info("test kit: asking MSC to take the player ([Test] MSC=1)");
                    PlayerSync::DebugHandOver();
                }
                return false;
            }
            g_ready.unowned = 0.0f;
            if (!PlayerSync::Standing()) {
                return false;
            }
            g_ready.standing += delta;
            if (const auto chunks = ModelView::Chunks(); chunks != g_ready.chunks) {
                g_ready.chunks = chunks;
                g_ready.quiet = 0.0f;
            } else {
                g_ready.quiet += delta;
            }
            if (g_ready.quiet < kModelQuiet && g_ready.standing < kModelWait) {
                return false;
            }
            if (!g_ready.logged) {
                g_ready.logged = true;
                SKSE::log::info("test kit: MSC has the player, {} model chunks from it{}", g_ready.chunks,
                    g_ready.quiet >= kModelQuiet ? "" : ", still coming");
            }
            return true;
        }

        void RequireMsc()
        {
            if (!PlayerSync::Owned()) {
                throw Error("MSC doesn't have the player: pr.msc first");
            }
        }

        // Vehicle names as the kit's commands take them, by kVehicle* id - 1.
        constexpr std::array<std::string_view, Protocol::kVehicleCount> kVehicles{ "van", "moped", "kekmet", "gifu" };

        std::uint32_t VehicleId(std::string_view a_name)
        {
            if (a_name.empty()) {
                return Protocol::kVehicleHayosiko;
            }
            for (std::uint32_t i = 0; i < kVehicles.size(); i++) {
                if (kVehicles[i] == a_name) {
                    return i + 1;
                }
            }
            throw Error(std::format("no vehicle '{}' (van, moped, kekmet, gifu)", a_name));
        }

        // A vehicle's heading as a reference's angle z (0 north, clockwise): its local +Y (forward) turned by its
        // rotation.
        double Heading(const float (&a_q)[4])
        {
            const double x = a_q[0], y = a_q[1], z = a_q[2], w = a_q[3];
            const double fx = 2.0 * (x * y - w * z);
            const double fy = 1.0 - 2.0 * (x * x + z * z);
            return std::atan2(fx, fy);
        }

        Json Point(const float (&a_point)[3])
        {
            return JsonObject{ { "x", a_point[0] }, { "y", a_point[1] }, { "z", a_point[2] } };
        }

        // What the kit says about a vehicle MSC has on the island: where, how fast, drawn or not, which of its
        // model's lights are on.
        Json DescribeVehicle(std::uint32_t a_id, const Protocol::VehicleState& a_state, bool a_parts = true)
        {
            const float speed = std::sqrt(a_state.velocity[0] * a_state.velocity[0] + a_state.velocity[1] * a_state.velocity[1] +
                                          a_state.velocity[2] * a_state.velocity[2]);
            JsonObject out{ { "vehicle", std::string(kVehicles[a_id - 1]) }, { "position", Point(a_state.position) },
                { "heading", Heading(a_state.rotation) * 57.29578 }, { "speed", speed }, { "driven", (a_state.flags & Protocol::kVehicleDriven) != 0 },
                { "drawn", VehicleView::Node(a_id) != nullptr } };
            Protocol::VehicleState box{};
            Hitbox::Box            shape{};
            out.Set("solid", Hitbox::Where(a_id, box, shape));
            auto parts = a_parts ? std::make_unique<Protocol::VehicleParts>() : nullptr;  // 8 KB: not for every frame a camera follows it
            if (parts && LinkService::Get().ReadVehicleParts(a_id, *parts) && parts->vehicle == a_id) {
                JsonArray on;
                if (const auto* lights = ModelView::Lights(parts->model)) {
                    for (std::size_t i = 0; i < lights->size() && i < 32; i++) {
                        if (parts->lights & (1u << i)) {
                            on.emplace_back(static_cast<int>((*lights)[i].role));
                        }
                    }
                }
                out.Set("lightsMask", static_cast<double>(parts->lights));
                out.Set("lightRolesOn", std::move(on));
                out.Set("lowLevel", parts->lowLevel);
                out.Set("highLevel", parts->highLevel);
            }
            return out;
        }

        // A line for MSC's spike commands (msc/src/Spike/SpikeHost.cs: drunk 2, lightson 1, time 6, ...) over the link.
        bool SendToMsc(const std::string& a_line)
        {
            const auto payload = LookAt::Encode(a_line);
            return LinkService::Get().Send(Protocol::MessageType::DebugCommand, payload.data(), static_cast<std::uint32_t>(payload.size()));
        }

        Json DescribeHit(const HitTest::AttackRow& a_row)
        {
            const auto& h = a_row.result;
            if (a_row.hit == 0) {  // the check across scenarios
                return JsonObject{ { "scenario", a_row.name }, { "pass", a_row.verdict.pass }, { "note", a_row.verdict.note } };
            }
            return JsonObject{ { "scenario", a_row.name }, { "hit", a_row.hit }, { "target", a_row.target }, { "landed", h.landed }, { "drunk", h.drunk },
                { "unarmed", h.unarmed }, { "speed", h.speed }, { "mass", h.mass }, { "sneak", h.sneak }, { "level", h.level }, { "armor", h.armor },
                { "expected", a_row.expected }, { "damage", h.damage }, { "before", h.before }, { "after", h.after }, { "drop", h.before - h.after },
                { "pass", a_row.verdict.pass }, { "note", a_row.verdict.note } };
        }

        // PerkeleRim's in-game tests (scenarios/README.md), each a scenario whose rows go to skytest run's CSV; the game
        // logic is in skse/src/debug/HitTest.cpp and skse/src/vehicle/HitboxTest.cpp.
        void AddTests()
        {
            SkseTestKit::AddScenario("pr.hits", "[scenarios] [target=<hex form id>]",
                "The hit test: punches and throws at a Whiterun guard (or target=, an actor base) placed just ahead with its AI off, for "
                "protocol/cpp/hit_test.h's scenarios (all, or a list: punch,bottle,junk,drunk,drunk-bottle,drunker-bottle); hands the player "
                "to MSC first if needed. A row per attack (damage cast against damage.h, health dropped, pass), the counts as the answer",
                [](Call& a_call) -> Step {
                    std::uint32_t target = 0;
                    if (const auto text = a_call.Text("target"); !text.empty()) {
                        try {
                            target = static_cast<std::uint32_t>(std::stoul(text, nullptr, 16));
                        } catch (...) {
                            throw Error("target is an actor base's form id in hex");
                        }
                    }
                    if (const auto refusal = HitTest::Begin(a_call.Text("scenarios", "all"), target); !refusal.empty()) {
                        throw Error(refusal);
                    }
                    a_call.DefaultTimeout(600.0);
                    return [drop = Dropping([] { HitTest::Stop("its request went away (a timeout, or the client stopped)"); })](
                               Call& a_call) -> std::optional<Json> {
                        while (const auto row = HitTest::NextRow()) {
                            a_call.Row(DescribeHit(*row));
                        }
                        if (HitTest::Running()) {
                            return std::nullopt;
                        }
                        const auto& r = HitTest::Last();
                        if (!r.done) {
                            throw Error(r.stopped.empty() ? "the hit test was stopped before its end" : r.stopped);
                        }
                        return JsonObject{ { "rows", r.rows }, { "passed", r.passed }, { "failed", r.rows - r.passed } };
                    };
                });

            SkseTestKit::AddScenario("pr.hitbox", "",
                "The hitbox test: calls the van in front of Kumpo (MSC has him, on foot), then for a minute two archers' 12 arrows at him (8 "
                "through the van, 4 from the side) and a melee bandit behind it; a row per log line, the counts as the answer",
                [](Call& a_call) -> Step {
                    if (const auto refusal = HitboxTest::Begin(); !refusal.empty()) {
                        throw Error(refusal);
                    }
                    a_call.DefaultTimeout(300.0);
                    return [drop = Dropping([] { HitboxTest::Stop("its request went away (a timeout, or the client stopped)"); })](
                               Call& a_call) -> std::optional<Json> {
                        while (const auto note = HitboxTest::NextNote()) {
                            a_call.Row(JsonObject{ { "seconds", note->seconds }, { "what", note->what } });
                        }
                        if (HitboxTest::Running()) {
                            if (!PlayerSync::Owned()) {
                                HitboxTest::Stop("Skyrim has the player back");  // its update runs only while MSC has him
                            }
                            return std::nullopt;
                        }
                        const auto& r = HitboxTest::Last();
                        if (!r.done) {
                            throw Error(r.stopped.empty() ? "the hitbox test was stopped before its end" : r.stopped);
                        }
                        return JsonObject{ { "through", r.through }, { "throughStopped", r.throughStopped }, { "throughOnKumpo", r.throughOnKumpo },
                            { "side", r.side }, { "sideOnKumpo", r.sideOnKumpo }, { "vanHits", r.vanHits }, { "meleeClosest", r.meleeClosest },
                            { "meleeInside", r.meleeInside }, { "samples", r.samples }, { "meleeDeepest", r.meleeDeepest },
                            { "obstaclesAtStart", r.obstaclesAtStart }, { "obstaclesAtEnd", r.obstaclesAtEnd } };
                    };
                });
        }

        // PerkeleRim's commands on the kit's command pipe (docs/DEV_SETUP.md, "Test sessions").
        void AddCommands()
        {
            SkseTestKit::AddTask("pr.msc", "[on]",
                "Hands the player to MSC (on=true, the default) or back to Skyrim, as F10 does; answers once it's done (for MSC: once it "
                "stands him on its island)",
                [](Call& a_call) -> Step {
                    const bool want = a_call.Flag("on", true);
                    if (want && !PlayerSync::Owned() && !LinkService::Get().IsUp()) {
                        throw Error("MSC isn't linked (not running, or still starting): nothing to hand the player to");
                    }
                    a_call.DefaultTimeout(120.0);  // a save's first handover walks Kumpo to the garage
                    auto asked = std::make_shared<double>(-1e9);  // a_call.Seconds() when it last asked
                    return [want, asked](Call& a_call) -> std::optional<Json> {
                        if (PlayerSync::Owned() == want && (!want || PlayerSync::Standing())) {
                            return JsonObject{ { "msc", want }, { "seated", PlayerSync::Seated() } };
                        }
                        if (PlayerSync::Owned() == want || a_call.Seconds() - *asked < 5.0) {
                            return std::nullopt;  // handed over, MSC's first pose still to come; or a moment after asking
                        }
                        *asked = a_call.Seconds();  // a handover takes a moment, and play mode may be about to do it itself
                        if (want) {
                            PlayerSync::DebugHandOver();
                        } else {
                            PlayerSync::DebugTakeBack();
                        }
                        return std::nullopt;
                    };
                });

            SkseTestKit::AddTask("pr.send", "<line...>",
                "Sends a line to MSC's debug commands over the link (msc/src/Spike/SpikeHost.cs: drunk 2, lightson 1, time 6, goto <name>, "
                "hold 0, ...); answers once it went (what MSC did is in its PerkeleRim_spike.log)",
                [](Call& a_call) -> Step {
                    auto line = a_call.Text("line");
                    if (line.empty()) {
                        throw Error("no line to send");
                    }
                    if (!LinkService::Get().IsUp()) {
                        throw Error("MSC isn't linked");
                    }
                    a_call.DefaultTimeout(5.0);
                    return [line](Call&) -> std::optional<Json> {
                        if (!SendToMsc(line)) {
                            return std::nullopt;  // the ring is full: next frame
                        }
                        SKSE::log::info("test kit: sent MSC '{}'", line);
                        return JsonObject{ { "sent", line } };
                    };
                });

            SkseTestKit::AddTask("pr.key", "<key> [seconds=<s>]",
                "Holds one of MSC's keys (a Unity KeyCode number: 93 is ] to call the van, 91 [ the moped, 104 H to punch) for seconds (0.3) "
                "on top of what the keyboard holds, while MSC has the player; answers once it's let go",
                [](Call& a_call) -> Step {
                    RequireMsc();
                    const double key = a_call.Number("key");
                    const double seconds = a_call.Number("seconds", 0.3);
                    if (key < 0 || key >= 512 || key != std::floor(key)) {
                        throw Error("key is a Unity KeyCode number, 0 to 511");
                    }
                    if (seconds <= 0.0 || seconds > 30.0) {
                        throw Error("seconds is over 0, up to 30");
                    }
                    const auto code = static_cast<std::uint16_t>(key);
                    PlayerSync::DebugHold(code, true);
                    a_call.DefaultTimeout(seconds + 10.0);
                    return [code, seconds, drop = Dropping([code] { PlayerSync::DebugHold(code, false); })](Call& a_call) -> std::optional<Json> {
                        if (a_call.Seconds() < seconds) {
                            return std::nullopt;
                        }
                        PlayerSync::DebugHold(code, false);
                        return JsonObject{ { "key", static_cast<int>(code) }, { "held", a_call.Seconds() } };
                    };
                });

            SkseTestKit::AddTask("pr.call", "[vehicle]",
                "Calls a vehicle to Kumpo as its summon key does (van ], moped [; MSC has the player, on foot) and answers once it stands drawn "
                "and solid in Skyrim's world, with where it is",
                [](Call& a_call) -> Step {
                    RequireMsc();
                    const auto id = VehicleId(a_call.Text("vehicle"));
                    if (id != Protocol::kVehicleHayosiko && id != Protocol::kVehicleJonnez) {
                        throw Error("only the van and the moped have a summon key");
                    }
                    const std::uint16_t key = id == Protocol::kVehicleHayosiko ? 93 : 91;
                    PlayerSync::DebugHold(key, true);
                    a_call.DefaultTimeout(60.0);
                    auto still = std::make_shared<float>(0.0f);
                    return [id, key, still, drop = Dropping([key] { PlayerSync::DebugHold(key, false); })](Call& a_call) -> std::optional<Json> {
                        if (a_call.Seconds() >= 0.3) {
                            PlayerSync::DebugHold(key, false);
                        }
                        Protocol::VehicleState state{};
                        Hitbox::Box            box{};
                        if (a_call.Seconds() < 0.3 || !Hitbox::Where(id, state, box) || !VehicleView::Node(id)) {
                            *still = 0.0f;
                            return std::nullopt;
                        }
                        const float speed = std::hypot(state.velocity[0], state.velocity[1]);
                        *still = speed < 20.0f ? *still + FrameSeconds() : 0.0f;
                        if (*still < 1.0f) {
                            return std::nullopt;  // a called vehicle drops in and settles first
                        }
                        return DescribeVehicle(id, state);
                    };
                });

            SkseTestKit::AddCommand("pr.vehicle", "[vehicle]", "A vehicle MSC has on the island (van, moped, kekmet, gifu): where, how fast, drawn, solid, its lights",
                [](Call& a_call) -> Json {
                    const auto             id = VehicleId(a_call.Text("vehicle"));
                    Protocol::VehicleState state{};
                    if (!LinkService::Get().ReadVehicle(id, state) || !(state.flags & Protocol::kVehicleValid)) {
                        throw Error(std::format("MSC has no {} on the island now", kVehicles[id - 1]));
                    }
                    return DescribeVehicle(id, state);
                });

            // The cameras' and the probes' pr.vehicle:<name>: the vehicle's origin facing its way, its drawn model as the
            // root (its lamps' lights hang under it, so probe lights finds them).
            SkseTestKit::AddTarget("pr.vehicle", "[vehicle]", "A vehicle PerkeleRim draws from MSC (van, the default; moped, kekmet, gifu): its origin, facing its way, its model and lamps inside",
                [](std::string_view a_name) {
                    const auto             id = VehicleId(a_name);
                    Protocol::VehicleState state{};
                    if (!LinkService::Get().ReadVehicle(id, state) || !(state.flags & Protocol::kVehicleValid)) {
                        throw Error(std::format("MSC has no {} on the island now", kVehicles[id - 1]));
                    }
                    auto* node = VehicleView::Node(id);
                    if (!node) {
                        throw Error(std::format("the {} isn't drawn in Skyrim now", kVehicles[id - 1]));
                    }
                    return SkseTestKit::Place{ .x = state.position[0], .y = state.position[1], .z = state.position[2], .heading = Heading(state.rotation),
                        .root = node, .details = DescribeVehicle(id, state, false) };  // pr.vehicle has its lights
                });
        }
    }

    void Load()
    {
        const SkseTestKit::Hooks hooks{ .arrived = &Arrived, .ready = &MscReady };
        g_active = SkseTestKit::Load({ .name = "PerkeleRim", .ini = Ini::kPath, .hooks = hooks });
        if (g_active) {
            std::atexit([] { g_exiting = true; });  // registered after the kit's statics, so it runs before they go
            AddCommands();
            AddTests();
        }
    }

    void OnMessage(SKSE::MessagingInterface::Message* a_message)
    {
        SkseTestKit::OnMessage(a_message);
    }

    bool Active()
    {
        return g_active;
    }
}
