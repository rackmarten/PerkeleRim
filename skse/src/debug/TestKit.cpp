// Built only with PERKELERIM_TEST_KIT (dev builds); TestKitOff.cpp stands in otherwise.
#include "debug/TestKit.h"

#include "Ini.h"
#include "coords.h"
#include "debug/HitTest.h"
#include "fuel/Fuel.h"
#include "link/LinkService.h"
#include "look_at.h"
#include "model/ModelView.h"
#include "player/PlayerSync.h"
#include "shout.h"
#include "teimo/Teimo.h"
#include "vehicle/Hitbox.h"
#include "vehicle/HitboxTest.h"
#include "vehicle/VehicleView.h"

#include <skse_test_kit/Kit.h>

#include <array>
#include <cstdlib>
#include <functional>
#include <cmath>
#include <filesystem>
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

        constexpr float kModelQuiet = 8.0f;    // seconds without a ModelChunk: MSC has sent its models (gaps measured up to 4.7 s)
        constexpr float kModelWait = 60.0f;    // and ready anyway after this long standing
        constexpr float kHandAgain = 5.0f;     // seconds between handovers asked for while play mode hasn't done it
        constexpr std::uint32_t kWhirlwindSprint = 0x2F7BA;  // Skyrim.esm's shout (pr.shout's default)

        bool g_active = false;
        bool g_exiting = false;  // the process is tearing down: the game can't be driven any more
        std::uint32_t g_loads = 0;  // kPostLoadGame messages so far, for pr.load
        bool          g_loaded = false;  // ...and whether the last one loaded

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
        // its models (about 190 MB at a link-up: tests start on a quiet link and a steady frame rate).
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
        constexpr std::array<std::string_view, Protocol::kVehicleCount> kVehicles{ "van", "moped", "kekmet", "gifu", "satsuma" };

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
            throw Error(std::format("no vehicle '{}' (van, moped, kekmet, gifu, satsuma)", a_name));
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
            if (a_id == Protocol::kVehicleSatsuma) {
                auto set = std::make_unique<Protocol::PartSet>();  // 10 KB, like the parts above
                if (LinkService::Get().ReadPartSet(*set) && set->vehicle == a_id) {
                    std::uint32_t pending = 0, visible = 0;
                    for (std::uint32_t i = 0; i < set->count && i < Protocol::kPartSetCapacity; i++) {
                        pending += set->parts[i].model == 0 ? 1 : 0;
                        visible += (set->parts[i].flags & Protocol::kPartVisible) != 0 ? 1 : 0;
                    }
                    out.Set("attached", static_cast<double>(set->count));
                    out.Set("unread", static_cast<double>(pending));
                    out.Set("waiting", static_cast<double>(VehicleView::PartsWaiting(a_id)));
                    out.Set("allDrawn", pending == 0 && VehicleView::PartsDrawn(a_id) == visible);  // every part MSC shows
                }
                out.Set("partsDrawn", static_cast<double>(VehicleView::PartsDrawn(a_id)));
            }
            return out;
        }

        Json DescribeTeimo()
        {
            const auto s = Teimo::Describe();
            return JsonObject{ { "found", s.found }, { "loaded", s.loaded }, { "drawn", s.drawn }, { "model", static_cast<double>(s.model) },
                { "x", s.position.x }, { "y", s.position.y }, { "z", s.position.z }, { "heading", s.heading * 57.29578 },
                { "distance", s.distance }, { "offPlayer", s.offPlayer * 57.29578 }, { "visiting", s.visiting }, { "lines", static_cast<double>(s.lines) }, { "lastClip", s.lastClip },
                { "lastTopic", s.lastTopic }, { "lastAt", s.lastAt }, { "caption", s.caption } };
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

            SkseTestKit::AddTask("pr.models", "[quiet=<s>]",
                "Waits for MSC's models: answers once model chunks have come and then none for quiet seconds (8: MSC logs its stretch of frames after 5), with how many came and "
                "the seconds from the request to the last (after pr.send models again, say)",
                [](Call& a_call) -> Step {
                    const double quiet = a_call.Number("quiet", 8.0);
                    if (quiet <= 0.0 || quiet > 60.0) {
                        throw Error("quiet is over 0, up to 60 seconds");
                    }
                    a_call.DefaultTimeout(300.0);
                    struct Seen
                    {
                        std::uint32_t start = ModelView::Chunks();
                        std::uint32_t last = start;
                        double        lastAt = 0.0;
                    };
                    return [quiet, seen = std::make_shared<Seen>()](Call& a_call) -> std::optional<Json> {
                        if (const auto chunks = ModelView::Chunks(); chunks != seen->last) {
                            seen->last = chunks;
                            seen->lastAt = a_call.Seconds();
                        }
                        if (seen->last == seen->start || a_call.Seconds() - seen->lastAt < quiet) {
                            return std::nullopt;
                        }
                        return JsonObject{ { "chunks", static_cast<double>(seen->last - seen->start) }, { "seconds", seen->lastAt } };
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

            SkseTestKit::AddTask("pr.shout", "[words] [shout=<hex form id>] [seconds=<s>]",
                "Tells MSC the player shouted, as Combat does when a shout is released (PlayerShout: Whirlwind Sprint, 0002F7BA, unless "
                "shout=; words 1 to 3, 3 by default), while MSC has the player; answers seconds (2) later with how far the player went, "
                "flat, in metres (moved)",
                [](Call& a_call) -> Step {
                    RequireMsc();
                    const double words = a_call.Number("words", 3.0);
                    const double seconds = a_call.Number("seconds", 2.0);
                    if (words < 1 || words > 3 || words != std::floor(words)) {
                        throw Error("words is 1, 2 or 3");
                    }
                    if (seconds < 0.0 || seconds > 30.0) {
                        throw Error("seconds is 0 to 30");
                    }
                    Protocol::PlayerShout message{};
                    message.shout = kWhirlwindSprint;
                    if (const auto text = a_call.Text("shout"); !text.empty()) {
                        try {
                            message.shout = static_cast<std::uint32_t>(std::stoul(text, nullptr, 16));
                        } catch (...) {
                            throw Error("shout is a shout's form id in hex");
                        }
                    }
                    message.kind = Shout::KindOf(message.shout);
                    message.words = static_cast<std::uint32_t>(words);
                    auto* player = RE::PlayerCharacter::GetSingleton();
                    if (!player) {
                        throw Error("no player");
                    }
                    a_call.DefaultTimeout(seconds + 10.0);
                    auto sentAt = std::make_shared<double>(-1.0);
                    return [message, seconds, sentAt, from = player->GetPosition()](Call& a_call) -> std::optional<Json> {
                        if (*sentAt < 0.0) {
                            if (!LinkService::Get().Send(Protocol::MessageType::PlayerShout, &message, sizeof(message))) {
                                return std::nullopt;  // the ring is full: next frame
                            }
                            *sentAt = a_call.Seconds();
                            SKSE::log::info("test kit: told MSC the player shouted {:08X}, {} words", message.shout, message.words);
                        }
                        if (a_call.Seconds() - *sentAt < seconds) {
                            return std::nullopt;
                        }
                        const auto  to = RE::PlayerCharacter::GetSingleton()->GetPosition();
                        const float moved = static_cast<float>(std::hypot(to.x - from.x, to.y - from.y) * Coords::kMetersPerUnit);
                        return JsonObject{ { "shout", std::format("{:08X}", message.shout) }, { "words", static_cast<int>(message.words) },
                            { "moved", moved } };
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

            SkseTestKit::AddTask("pr.satsuma", "[what]",
                "The Satsuma (MSC has it on foot): here brings it in front of Kumpo as MSC has it built, half puts half its parts on through "
                "MSC's own assembly (body panels, front seats, dashboard, radiator), full every part MSC has loose, bare takes every part with a Removal FSM off; answers once "
                "it is drawn and its parts have settled (every part on it drawn from its model, nothing changed for 2 s), as pr.vehicle satsuma",
                [](Call& a_call) -> Step {
                    RequireMsc();
                    auto what = a_call.Text("what");
                    if (what.empty()) {
                        what = "here";
                    }
                    if (what != "here" && what != "half" && what != "full" && what != "bare") {
                        throw Error("what is here, half, full or bare");
                    }
                    a_call.DefaultTimeout(what == "full" ? 900.0 : 180.0);
                    struct Seen
                    {
                        bool          sent = false;
                        double        sentAt = 0.0;
                        double        steadySince = -1.0;
                        std::uint32_t attached = ~0u;
                        std::uint32_t drawn = ~0u;
                    };
                    return [line = "satsuma " + what, seen = std::make_shared<Seen>()](Call& a_call) -> std::optional<Json> {
                        if (!seen->sent) {
                            if (!SendToMsc(line)) {
                                return std::nullopt;  // the ring is full: next frame
                            }
                            seen->sent = true;
                            seen->sentAt = a_call.Seconds();
                            SKSE::log::info("test kit: sent MSC '{}'", line);
                        }
                        Protocol::VehicleState state{};
                        auto                   set = std::make_unique<Protocol::PartSet>();
                        if (a_call.Seconds() - seen->sentAt < 1.0 || !LinkService::Get().ReadVehicle(Protocol::kVehicleSatsuma, state) ||
                            !(state.flags & Protocol::kVehicleValid) || !VehicleView::Node(Protocol::kVehicleSatsuma) ||
                            !LinkService::Get().ReadPartSet(*set) || set->vehicle != Protocol::kVehicleSatsuma) {
                            seen->steadySince = -1.0;
                            return std::nullopt;
                        }
                        const auto drawn = VehicleView::PartsDrawn(Protocol::kVehicleSatsuma);
                        bool       unread = false;
                        for (std::uint32_t i = 0; i < set->count && i < Protocol::kPartSetCapacity; i++) {
                            unread = unread || set->parts[i].model == 0;
                        }
                        // Every part's model read and arrived (a door is 20 MB of chunks), and nothing changed for 2 s.
                        // MSC still putting parts on (kPartSetBuilding): a run of parts that don't go on, or the
                        // engine built on its loose block, changes nothing on the car for a while.
                        if (unread || (set->flags & Protocol::kPartSetBuilding) != 0 || VehicleView::PartsWaiting(Protocol::kVehicleSatsuma) > 0 ||
                            set->count != seen->attached || drawn != seen->drawn) {
                            seen->attached = set->count;
                            seen->drawn = drawn;
                            seen->steadySince = a_call.Seconds();
                            return std::nullopt;
                        }
                        if (a_call.Seconds() - seen->steadySince < 2.0) {
                            return std::nullopt;
                        }
                        return DescribeVehicle(Protocol::kVehicleSatsuma, state);
                    };
                });

            SkseTestKit::AddCommand("pr.fuel", "", "The fuel line on the HUD as Skyrim shows it (MSC's with the nearest pump on a low tank), MSC's flags for it (low, dry) and whether this save had the tow tip",
                [](Call&) -> Json {
                    const auto flags = Fuel::LastFlags();
                    return JsonObject{ { "line", Fuel::LastLine() }, { "low", (flags & Protocol::kFuelTextLow) != 0 },
                        { "dry", (flags & Protocol::kFuelTextDry) != 0 }, { "tipped", Fuel::Tipped() } };
                });

            SkseTestKit::AddCommand("pr.vehicle", "[vehicle]", "A vehicle MSC has on the island (van, moped, kekmet, gifu, satsuma): where, how fast, drawn, solid, its lights; the Satsuma's parts on it (attached, unread: no model yet) and drawn (partsDrawn)",
                [](Call& a_call) -> Json {
                    const auto             id = VehicleId(a_call.Text("vehicle"));
                    Protocol::VehicleState state{};
                    if (!LinkService::Get().ReadVehicle(id, state) || !(state.flags & Protocol::kVehicleValid)) {
                        throw Error(std::format("MSC has no {} on the island now", kVehicles[id - 1]));
                    }
                    return DescribeVehicle(id, state);
                });

            SkseTestKit::AddTask("pr.load", "<save>",
                "Loads a save in the running game, as a quickload does (pr.load PerkeleRim-Kumpo, the starter save); answers once it's "
                "loaded. Play mode hands the player to MSC by itself a little later: pr.msc waits for that",
                [](Call& a_call) -> Step {
                    auto name = a_call.Text("save");
                    if (name.size() > 4 && _stricmp(name.c_str() + name.size() - 4, ".ess") == 0) {
                        name.resize(name.size() - 4);
                    }
                    if (name.empty()) {
                        throw Error("no save to load");
                    }
                    auto* saves = RE::BGSSaveLoadManager::GetSingleton();
                    if (!saves) {
                        throw Error("no save manager");
                    }
                    // A save that isn't there never sends kPostLoadGame: say so now rather than time out.
                    std::string folder = "Saves";
                    auto*       ini = RE::INISettingCollection::GetSingleton();
                    if (auto* setting = ini ? ini->GetSetting("sLocalSavePath:General") : nullptr; setting && setting->GetString() && *setting->GetString()) {
                        folder = setting->GetString();
                    }
                    if (const auto logs = SKSE::log::log_directory(); logs && !std::filesystem::exists(logs->parent_path() / folder / (name + ".ess"))) {
                        throw Error(std::format("no save {} in {}", name, (logs->parent_path() / folder).string()));
                    }
                    a_call.DefaultTimeout(90.0);
                    // A failed kPostLoadGame is provisional, as the kit's own start load treats it: with Creations
                    // installed, Skyrim may report a failure and ask before loading, then load once it's answered.
                    struct Pending
                    {
                        std::uint32_t loads;
                        bool          answered = false;
                        int           failures = 0;
                        double        failedAt = -1.0;  // a_call.Seconds() of a failure no answer explains yet
                    };
                    auto pending = std::make_shared<Pending>(Pending{ .loads = g_loads });  // before Load: SKSE may send kPostLoadGame inside it
                    SKSE::log::info("test kit: loading the save {} (pr.load)", name);
                    saves->Load(name.c_str(), false);  // returns nothing: kPostLoadGame says how it went
                    return [pending, name](Call& a_call) -> std::optional<Json> {
                        if (g_loads != pending->loads) {
                            pending->loads = g_loads;
                            if (g_loaded) {
                                return JsonObject{ { "loaded", name } };
                            }
                            if (++pending->failures > 1) {  // twice: it really failed
                                throw Error(std::format("the save {} didn't load", name));
                            }
                            if (!pending->answered) {
                                pending->failedAt = a_call.Seconds();  // a question has 5 s to explain it
                            }  // after the answer it was the question's: the load goes on (or times out)
                        }
                        // Skyrim's question about installed Creations (the kit answers that only for its start). Matched
                        // by the English game's text, as the kit's own answer is; answered once.
                        if (auto* box = RE::MessageBoxMenu::GetCurrentMessageBoxData(); box && !pending->answered) {
                            const std::string_view body = box->bodyText.c_str();
                            if (body.find("continue loading this save") != std::string_view::npos) {
                                for (std::int32_t i = 0; i < static_cast<std::int32_t>(box->buttonText.size()); i++) {
                                    if (_stricmp(box->buttonText[i].c_str(), "Yes") == 0) {
                                        SKSE::log::info("test kit: answering \"{}\" with \"Yes\" (pr.load)", body);
                                        RE::MessageBoxMenu::SelectOption(i);
                                        pending->answered = true;
                                        pending->failedAt = -1.0;  // a failure before it was this question
                                        break;
                                    }
                                }
                            }
                        }
                        if (pending->failedAt >= 0.0 && a_call.Seconds() - pending->failedAt > 5.0) {  // and no question came
                            throw Error(std::format("the save {} didn't load", name));
                        }
                        return std::nullopt;
                    };
                });

            SkseTestKit::AddCommand("pr.lookat", "", "MSC's look-at prompt as Skyrim last got it: whether MSC shows its hand (doors, switches, knobs) and its text",
                [](Call&) -> Json {
                    return JsonObject{ { "msc", PlayerSync::Owned() }, { "hand", PlayerSync::PromptHand() }, { "text", PlayerSync::PromptText() } };
                });

            SkseTestKit::AddCommand("pr.teimo", "", "Teimo at his stall in Riverwood: found, loaded, drawn as MSC's Teimo, where, which way he faces, how far Kumpo is, the last line he said and his caption now",
                [](Call&) -> Json { return DescribeTeimo(); });

            SkseTestKit::AddCommand("pr.teimo.reset", "", "Teimo forgets Kumpo's visit and his line count, as after a load (a scenario's fresh start)",
                [](Call&) -> Json {
                    Teimo::OnGameLoaded();
                    return DescribeTeimo();
                });

            SkseTestKit::AddTask("pr.teimo.say", "<topic>",
                "Teimo says a line of a topic now (greet, drunk, chat, bye, finger, pissed, hit), as if its event came; answers with the line and its caption",
                [](Call& a_call) -> Step {
                    const auto topic = Teimo::TopicByName(a_call.Text("topic"));
                    if (!topic) {
                        throw Error("topic is greet, drunk, chat, bye, finger, pissed or hit");
                    }
                    auto* player = RE::PlayerCharacter::GetSingleton();
                    if (!player || !Teimo::DebugSay(player, *topic)) {
                        throw Error("Teimo isn't loaded near the player, or MSC isn't linked");
                    }
                    a_call.DefaultTimeout(5.0);
                    return [](Call&) -> std::optional<Json> { return DescribeTeimo(); };
                });

            SkseTestKit::AddCommand("pr.teimo.front", "[distance=<units>]",
                "Moves the player (Skyrim has it: pr.msc on=false first) in front of Teimo's stall, distance (900) units out along the way it faces, "
                "facing Teimo; answers with where",
                [](Call& a_call) -> Json {
                    if (PlayerSync::Owned()) {
                        throw Error("MSC has the player: take it back first (pr.msc on=false)");
                    }
                    auto* teimo = Teimo::Actor();
                    auto* player = RE::PlayerCharacter::GetSingleton();
                    if (!teimo || !player) {
                        throw Error("PerkeleRim.esp has no Teimo");
                    }
                    const auto  s = Teimo::Describe();
                    const float distance = static_cast<float>(a_call.Number("distance", 900.0));
                    const auto  at = teimo->GetPosition();
                    const RE::NiPoint3 to{ at.x + distance * std::sin(s.home), at.y + distance * std::cos(s.home), at.z + 60.0f };
                    const float facing = std::fmod(s.home + 3.14159265f, 6.2831853f);
                    player->SetPosition(to, true);
                    player->SetAngle(RE::NiPoint3(0.0f, 0.0f, facing));
                    return JsonObject{ { "x", to.x }, { "y", to.y }, { "z", to.z }, { "heading", facing * 57.29578 } };
                });

            // pr.teimo: Teimo's feet, facing the way his model does, his drawn model as the root.
            SkseTestKit::AddTarget("pr.teimo", "", "Teimo at his stall: his feet, facing the way he looks, MSC's Teimo inside",
                [](std::string_view) {
                    const auto s = Teimo::Describe();
                    if (!s.loaded) {
                        throw Error("Teimo isn't loaded (Riverwood isn't near, or PerkeleRim.esp has no Teimo)");
                    }
                    return SkseTestKit::Place{ .x = s.position.x, .y = s.position.y, .z = s.position.z + 90.0f, .heading = s.heading,
                        .root = Teimo::Node(), .details = DescribeTeimo() };
                });

            // The cameras' and the probes' pr.vehicle:<name>: the vehicle's origin facing its way, its drawn model as the
            // root (its lamps' lights hang under it, so probe lights finds them).
            SkseTestKit::AddTarget("pr.vehicle", "[vehicle]", "A vehicle PerkeleRim draws from MSC (van, the default; moped, kekmet, gifu, satsuma): its origin, facing its way, its model and lamps inside",
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
        if (a_message->type == SKSE::MessagingInterface::kPostLoadGame) {
            g_loaded = a_message->data != nullptr;
            g_loads++;
        }
        SkseTestKit::OnMessage(a_message);
    }

    bool Active()
    {
        return g_active;
    }
}
