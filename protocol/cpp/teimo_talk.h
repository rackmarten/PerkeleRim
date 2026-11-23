// Teimo at his makeshift stall in Riverwood: which of MSC's own Teimo lines he says to Kumpo and
// when, as My Summer Car's Teimo does in his shop (docs/FSM_MAP.md, "Teimo"), with Skyrim's
// captions for them, and how he turns to face Kumpo. MSC plays the clips from the player's own
// install (TeimoLine); nothing of the game's audio is shipped, only these short English captions.
// Header-only and free of Skyrim types, so the protocol tests cover it; Skyrim's half is
// skse/src/teimo/Teimo.cpp.

#pragma once

#include <algorithm>
#include <array>
#include <cmath>
#include <cstdint>
#include <optional>
#include <string_view>

namespace PerkeleRim::Teimo
{
    enum class Topic : std::uint8_t
    {
        kGreet,   // Kumpo walks up to the counter, or talks to him (E): MSC's Greetings HELLO
        kDrunk,   // after the greeting when Kumpo is drunk, once per greeted visit: Speak's "Is drunk"
        kChat,    // Kumpo hangs about the stall: Speak's random stories
        kBye,     // Kumpo walks off after a visit: Greetings GOODBYE
        kFinger,  // the middle finger at him: Speak's FINGER
        kPissed,  // the piss stream on him: the Reaction FSMs' PISS
        kHit,     // punched, run into, or something thrown at him
    };
    inline constexpr std::size_t kTopicCount = 7;

    struct CaptionPart
    {
        float            at;    // seconds into the clip it starts
        std::string_view text;  // English, without the "Teimo: "
    };

    struct Line
    {
        Topic                      topic;
        std::string_view           clip;     // the MasterAudio variation in MSC's Teimo group (scripts/voice-picker.ps1: Teimo #n)
        float                      seconds;  // the clip's length in MSC
        std::array<CaptionPart, 3> captions;  // in order; unused ones have empty text
    };

    // MSC's own lines that need no shop, picked for the same events MSC's Teimo FSMs pick them for
    // (the phone calls, the pub, the ads, the till, the job offer and the closing time are left out,
    // and the stories that are about his shop). Captions are our own short English versions of the
    // Finnish, as MSC's subtitles are: a long story gets a line or two, not every word.
    inline constexpr std::array kLines{
        Line{ Topic::kGreet, "paivaa1", 3.4f, { { { 0.0f, "Good day, and welcome to Teimo's shop!" } } } },
        Line{ Topic::kGreet, "paivaa2", 2.7f, { { { 0.0f, "Well, good day. So what'll it be?" } } } },
        Line{ Topic::kGreet, "paivaa3", 3.2f, { { { 0.0f, "Well, hi there! Hello to the counter, dishes to the machine!" } } } },
        Line{ Topic::kGreet, "paivaa4", 3.7f, { { { 0.0f, "Well, good day! What blew you into the village?" } } } },
        Line{ Topic::kGreet, "paivaa5", 5.4f, { { { 0.0f, "Five hundred and eight... five hundred and one..." }, { 2.6f, "Oh, good day! What can I get you?" } } } },
        Line{ Topic::kGreet, "paivaa6", 3.1f, { { { 0.0f, "Well, well... good day to you too, you big lump." } } } },
        Line{ Topic::kGreet, "paivaa7", 4.1f, { { { 0.0f, "Welcome t... oh, it's only you." } } } },
        Line{ Topic::kDrunk, "teimo_huomio_kannin1", 5.6f, { { { 0.0f, "Oh ho, even a cuckoo could tell you're drunk now." } } } },
        Line{ Topic::kDrunk, "teimo_huomio_kannin2", 5.4f, { { { 0.0f, "Should I widen the aisles? Your steps are getting pretty wide." } } } },
        Line{ Topic::kDrunk, "teimo_huomio_kannin3", 4.3f, { { { 0.0f, "Well, is it your birthday, or what are you celebrating?" } } } },
        Line{ Topic::kChat, "random1", 24.6f, { { { 0.0f, "Have I ever told you about my wrestling career?" }, { 10.0f, "My moves were famous, but my mic skills weren't much." } } } },
        Line{ Topic::kChat, "random2", 58.5f, { { { 0.0f, "What a strange summer. Sun, then rain, then sun again." }, { 25.0f, "Even at night it rains. Strange summer, I tell you." } } } },
        Line{ Topic::kChat, "random3", 31.6f, { { { 0.0f, "Have you heard that crazy food show on the radio?" }, { 12.0f, "Who listens to that? Someone must, it's on every week." } } } },
        Line{ Topic::kChat, "random4", 47.6f, { { { 0.0f, "I used to be quite the fisherman myself, back in the day." }, { 20.0f, "Irma screamed crocodile once. It was an otter." } } } },
        Line{ Topic::kChat, "random5", 23.1f, { { { 0.0f, "The summer folk only buy sausage and beer." }, { 10.0f, "Everything else goes off on the shelf. Even the milk." } } } },
        Line{ Topic::kChat, "random7", 29.3f, { { { 0.0f, "This youth music on the radio, all thumping and guitars." }, { 14.0f, "Nobody plays my kind of music any more." } } } },
        Line{ Topic::kChat, "random8", 29.6f, { { { 0.0f, "(sings) Marjatta, my dearest, you found another and left me..." } } } },
        Line{ Topic::kChat, "random9", 26.0f, { { { 0.0f, "This recession is hard on my business." }, { 10.0f, "No money for anything, but there's always money for beer." } } } },
        Line{ Topic::kChat, "random10", 4.4f, { { { 0.0f, "Oh, damn these mosquitoes, they're in every bloody place." } } } },
        Line{ Topic::kChat, "random11", 15.0f, { { { 0.0f, "We sure have a lot of bakers in this village." },
                                                  { 4.5f, "The youngsters keep buying sugar and yeast," },
                                                  { 9.0f, "so I bet there'll be some damn fine buns." } } } },
        Line{ Topic::kChat, "random12", 31.8f, { { { 0.0f, "The price of petrol, it just keeps going up." }, { 15.0f, "How do you even afford to drive that thing of yours?" } } } },
        Line{ Topic::kChat, "random13", 42.2f, { { { 0.0f, "Have you seen that car going round the back roads?" }, { 18.0f, "Never stops for petrol. Something fishy there." } } } },
        Line{ Topic::kChat, "random14", 22.7f, { { { 0.0f, "I don't think that mosquito spray works at all." }, { 10.0f, "Now I spray the whole can at once." } } } },
        Line{ Topic::kChat, "random16", 28.8f, { { { 0.0f, "Some idiots keep phoning me in the middle of the night." }, { 15.0f, "Now I pull the phone out of the wall." } } } },
        Line{ Topic::kChat, "random17", 9.6f, { { { 0.0f, "Dammit, my washing machine broke and I'm on my last clean underpants." },
                                                 { 6.0f, "Oops, there go the last ones too." } } } },
        Line{ Topic::kChat, "random18", 7.7f, { { { 0.0f, "I don't touch the booze any more myself." },
                                                 { 3.5f, "My eyes go crossed as soon as I have a drop of the hard stuff." } } } },
        Line{ Topic::kChat, "random19", 44.2f, { { { 0.0f, "Are you entering the rally this weekend?" }, { 12.0f, "I came second once. There were two of us." } } } },
        Line{ Topic::kChat, "random20", 24.6f, { { { 0.0f, "Have you stuck a sticker on your window? Damn expensive things." } } } },
        Line{ Topic::kBye, "kiitos1", 2.3f, { { { 0.0f, "Thank you, and welcome back!" } } } },
        Line{ Topic::kBye, "kiitos2", 2.6f, { { { 0.0f, "Right then, thanks and cheerio!" } } } },
        Line{ Topic::kBye, "kiitos3", 2.6f, { { { 0.0f, "Yeah, see you around, bye!" } } } },
        Line{ Topic::kBye, "kiitos4", 2.5f, { { { 0.0f, "Right, thanks for this time." } } } },
        Line{ Topic::kBye, "kiitos5", 2.6f, { { { 0.0f, "Thank you, and have a nice day!" } } } },
        Line{ Topic::kFinger, "keskari1", 8.2f, { { { 0.0f, "You can do your shopping somewhere else if the service isn't good enough." },
                                                   { 5.0f, "Oh wait, no you can't!" } } } },
        Line{ Topic::kFinger, "keskari2", 4.1f, { { { 0.0f, "Nice finger. Want me to show it the meat cleaver?" } } } },
        Line{ Topic::kFinger, "keskari3", 1.8f, { { { 0.0f, "Up yours, boy!" } } } },
        Line{ Topic::kFinger, "keskari4", 1.9f, { { { 0.0f, "Fuck off!" } } } },
        Line{ Topic::kFinger, "keskari5", 3.2f, { { { 0.0f, "Go jump in a lake, you damn brat!" } } } },
        Line{ Topic::kFinger, "keskari6", 2.8f, { { { 0.0f, "Yeah, yeah, and butter on that too." } } } },
        Line{ Topic::kPissed, "kusta1", 19.2f, { { { 0.0f, "Don't, for Satan's sake, not there! Look at this mess!" }, { 8.0f, "Now I have to get the mop. Damn it!" } } } },
        Line{ Topic::kPissed, "kusta2", 12.4f, { { { 0.0f, "Oh, good grief, did you really have to piss right there?" },
                                                 { 5.0f, "Can't a grown man hold it any longer? Hell!" } } } },
        Line{ Topic::kPissed, "kusta3", 12.7f, { { { 0.0f, "What? Hell, a grown man pissing everywhere!" }, { 6.0f, "Now get the hell out of here. Perkele." } } } },
        Line{ Topic::kPissed, "kustaan_teimon_naamaan", 11.8f, { { { 0.0f, "Satan! Now get the hell out of here, you damn brat," },
                                                                  { 4.5f, "and don't you ever bother coming back! Hell!" } } } },
        Line{ Topic::kHit, "maksa_vahinko2", 5.8f, { { { 0.0f, "Good heavens, boy, I'm not selling you a damn thing" },
                                                       { 3.0f, "until you've paid for that." } } } },
    };

    inline constexpr float kCaptionHold = 10.0f;  // a caption part shows at most this long, as MSC's subtitles do for a long story

    // The caption showing a_seconds into a_line, empty after its end (or kCaptionHold after its last part).
    constexpr std::string_view CaptionAt(const Line& a_line, float a_seconds)
    {
        if (a_seconds < 0.0f || a_seconds >= a_line.seconds) {
            return {};
        }
        std::string_view text;
        float            since = 0.0f;
        for (const auto& c : a_line.captions) {
            if (!c.text.empty() && c.at <= a_seconds) {
                text = c.text;
                since = c.at;
            }
        }
        return a_seconds - since < kCaptionHold ? text : std::string_view{};
    }

    // Distances in Skyrim units (70 a meter) from Teimo's feet to Kumpo's, flat. MSC's Teimo talks
    // to the player within 8 m of him.
    inline constexpr float kCounter = 280.0f;  // at the counter: walking up this close greets him (4 m)
    inline constexpr float kAround = 560.0f;   // at the stall: small talk while he stays this close, goodbye once he walks past it (8 m)
    inline constexpr float kGone = 1050.0f;    // walked off: the next walk-up is a new visit (15 m)
    inline constexpr float kHears = 1400.0f;   // further than this he says nothing at all (20 m)

    inline constexpr float  kDrunkAt = 0.7f;     // MSC's PlayerDrunk from which he remarks on it (Speak's "Is drunk")
    inline constexpr double kChatAfter = 10.0;   // seconds after his last line before the first small talk (MSC: 5 to 15)
    inline constexpr double kChatEvery = 135.0;  // from one story's start to the next's while Kumpo stays (MSC: 125 + 5 to 15)
    inline constexpr double kGreetAgain = 90.0;  // a walk-up this soon after the last greeting gets none
    inline constexpr double kReplyGap = 4.0;     // seconds between two replies to the same kind of insult, after the longest of its lines

    // How Teimo talks to Kumpo. Tick every frame Kumpo is handed over to MSC; the On* calls are
    // events. Each returns the topic to say now, if any; Say() picks the line and starts it, and
    // Speaking() holds the rest back until it's over. Insults (finger, piss, hit) cut in over
    // anything but another insult's reply.
    class Talk
    {
    public:
        // a_distance: flat units from Teimo to Kumpo. a_drunk: MSC's drunk level.
        std::optional<Topic> Tick(double a_now, float a_distance, float a_drunk)
        {
            now_ = a_now;
            if (a_distance > kGone) {
                visiting_ = false;
                return std::nullopt;
            }
            if (visiting_ && a_distance > kAround) {
                if (!byeSaid_ && greetedVisit_ && !(Speaking() && IsInsult(current_))) {
                    byeSaid_ = true;
                    return Topic::kBye;
                }
                return std::nullopt;
            }
            if (Speaking()) {
                return std::nullopt;
            }
            if (!visiting_) {
                if (a_distance > kCounter) {
                    return std::nullopt;
                }
                visiting_ = true;
                byeSaid_ = false;
                nextChat_ = a_now + kChatAfter;
                if (a_now - greeted_ < kGreetAgain) {
                    greetedVisit_ = true;  // back too soon, or he talked to him first: greeted a moment ago
                    return std::nullopt;
                }
                greeted_ = a_now;
                greetedVisit_ = true;
                drunkSaid_ = false;
                return Topic::kGreet;
            }
            if (!drunkSaid_ && a_drunk >= kDrunkAt) {
                drunkSaid_ = true;
                return Topic::kDrunk;
            }
            if (a_now >= nextChat_) {
                return Topic::kChat;
            }
            return std::nullopt;
        }

        // E on him: his greeting, unless he is speaking. A walk-up after it is the same visit.
        std::optional<Topic> OnTalk(double a_now)
        {
            now_ = a_now;
            if (Speaking()) {
                return std::nullopt;
            }
            greeted_ = a_now;
            if (visiting_) {
                greetedVisit_ = true;
            }
            return Topic::kGreet;
        }

        // The finger, the piss stream or a hit reached him (the caller checks he sees it, or that
        // it hit him), a_distance away.
        std::optional<Topic> OnInsult(Topic a_topic, double a_now, float a_distance)
        {
            now_ = a_now;
            if (a_distance > kHears || (Speaking() && IsInsult(current_))) {
                return std::nullopt;
            }
            auto& last = lastReply_[static_cast<std::size_t>(a_topic)];
            if (a_now - last < kReplyGap + LongestOf(a_topic)) {
                return std::nullopt;  // the piss stream hits him every frame: one reply per outburst
            }
            last = a_now;
            byeSaid_ = true;  // no "thank you, come again" after that
            return a_topic;
        }

        // The line for a_topic at a_random (0..1), not the one said last for that topic when it has
        // others; starts it at a_now. Null for a topic without lines.
        const Line* Say(Topic a_topic, double a_now, double a_random)
        {
            now_ = a_now;
            std::array<const Line*, kLines.size()> pool{};
            std::size_t                            count = 0;
            const auto                             t = static_cast<std::size_t>(a_topic);
            for (const auto& line : kLines) {
                if (line.topic == a_topic && (&line != last_[t] || Count(a_topic) == 1)) {
                    pool[count++] = &line;
                }
            }
            if (count == 0) {
                return nullptr;
            }
            const auto* line = pool[(std::min)(count - 1, static_cast<std::size_t>(std::clamp(a_random, 0.0, 1.0) * static_cast<double>(count)))];
            last_[t] = line;
            current_ = a_topic;
            startedAt_ = a_now;
            endsAt_ = a_now + line->seconds;
            nextChat_ = a_topic == Topic::kChat ? a_now + (std::max)(kChatEvery, static_cast<double>(line->seconds) + kChatAfter) :
                                                  (std::max)(nextChat_, endsAt_ + kChatAfter);
            return line;
        }

        // Kumpo is gone without walking off (Skyrim took the player back, Teimo unloaded): the
        // next walk-up is a new visit, and this one gets no goodbye.
        void EndVisit() { visiting_ = false; }

        bool   Speaking() const { return now_ < endsAt_; }
        double StartedAt() const { return startedAt_; }
        bool   Visiting() const { return visiting_; }
        void   Reset() { *this = Talk{}; }  // a load, a new game

        static constexpr bool IsInsult(Topic a_topic)
        {
            return a_topic == Topic::kFinger || a_topic == Topic::kPissed || a_topic == Topic::kHit;
        }

        static constexpr std::size_t Count(Topic a_topic)
        {
            std::size_t n = 0;
            for (const auto& line : kLines) {
                n += line.topic == a_topic ? 1 : 0;
            }
            return n;
        }

    private:
        static constexpr double LongestOf(Topic a_topic)
        {
            double longest = 0.0;
            for (const auto& line : kLines) {
                if (line.topic == a_topic) {
                    longest = (std::max)(longest, static_cast<double>(line.seconds));
                }
            }
            return longest;
        }

        double                               now_ = 0.0;
        bool                                 visiting_ = false;
        bool                                 greetedVisit_ = false;  // this visit got its greeting (so it gets a goodbye)
        bool                                 byeSaid_ = false;
        bool                                 drunkSaid_ = false;
        double                               greeted_ = -1e9;
        double                               nextChat_ = 1e18;
        double                               startedAt_ = -1e9;
        double                               endsAt_ = -1e9;
        Topic                                current_ = Topic::kGreet;
        std::array<const Line*, kTopicCount> last_{};
        std::array<double, kTopicCount>      lastReply_{ -1e9, -1e9, -1e9, -1e9, -1e9, -1e9, -1e9 };
    };

    // Whether Teimo, a_dx, a_dy units from Kumpo (Skyrim space), sees the finger Kumpo points along
    // a_dirX, a_dirY (the camera's forward, flattened): within kFingerRange and 45 degrees of it.
    inline constexpr float kFingerRange = 700.0f;  // 10 m: further than the law minds it, he's watching his customer
    inline bool SeesFinger(float a_dx, float a_dy, float a_dirX, float a_dirY)
    {
        const float distance = std::sqrt(a_dx * a_dx + a_dy * a_dy);
        const float way = std::sqrt(a_dirX * a_dirX + a_dirY * a_dirY);
        if (distance > kFingerRange || way <= 0.0f) {
            return false;
        }
        return distance < 1.0f || (a_dx * a_dirX + a_dy * a_dirY) / (distance * way) >= 0.7071068f;
    }

    inline constexpr float kTurnRange = 840.0f;     // he turns to Kumpo within 12 m, else back to his stall's heading
    inline constexpr float kTurnPerSecond = 2.5f;   // radians a second

    // Teimo's heading (radians, clockwise from north, as Skyrim's angle z) a_delta seconds on from
    // a_heading, turning toward a_target by the shorter way at kTurnPerSecond.
    inline float TurnToward(float a_heading, float a_target, float a_delta)
    {
        constexpr float kPi = 3.14159265f;
        float           diff = std::remainder(a_target - a_heading, 2.0f * kPi);
        const float     step = kTurnPerSecond * (std::max)(0.0f, a_delta);
        diff = std::clamp(diff, -step, step);
        float out = std::remainder(a_heading + diff, 2.0f * kPi);
        return out < 0.0f ? out + 2.0f * kPi : out;
    }

    // The heading from Teimo toward something a_dx, a_dy units away (Skyrim space).
    inline float HeadingTo(float a_dx, float a_dy)
    {
        constexpr float kPi = 3.14159265f;
        const float     h = std::atan2(a_dx, a_dy);
        return h < 0.0f ? h + 2.0f * kPi : h;
    }
}
