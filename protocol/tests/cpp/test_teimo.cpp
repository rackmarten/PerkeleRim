#include <doctest/doctest.h>

#include "../../cpp/teimo_talk.h"

#include <ostream>
#include <set>
#include <string>
#include <vector>

using namespace PerkeleRim;
using Teimo::Talk;
using Teimo::Topic;

namespace
{
    constexpr float kFar = 3000.0f;

    // Runs the talk at a_distance from a_from to a_to seconds in 0.1 s frames, saying whatever comes
    // up; the topics said, in order.
    std::vector<Topic> Stay(Talk& a_talk, double a_from, double a_to, float a_distance, float a_drunk = 0.0f)
    {
        std::vector<Topic> said;
        for (double t = a_from; t < a_to; t += 0.1) {
            if (const auto topic = a_talk.Tick(t, a_distance, a_drunk)) {
                REQUIRE(a_talk.Say(*topic, t, 0.5) != nullptr);
                said.push_back(*topic);
            }
        }
        return said;
    }
}

TEST_CASE("every line has a clip, a length that fits and captions in order")
{
    std::set<std::string> clips;
    for (const auto& line : Teimo::kLines) {
        CHECK(!line.clip.empty());
        CHECK(line.clip.size() <= 47);  // TeimoLine's clip room (kTeimoClipMax), ASCII names
        CHECK(clips.insert(std::string(line.clip)).second);
        CHECK(line.seconds > 1.0f);
        CHECK(!line.captions[0].text.empty());
        CHECK(line.captions[0].at == 0.0f);
        float last = -1.0f;
        for (const auto& c : line.captions) {
            if (c.text.empty()) {
                continue;
            }
            CHECK(c.at > last);
            CHECK(c.at < line.seconds);
            CHECK(c.text.size() <= 80);  // one subtitle line
            last = c.at;
        }
    }
    for (std::size_t t = 0; t < Teimo::kTopicCount; t++) {
        CHECK(Talk::Count(static_cast<Topic>(t)) >= 1);
    }
}

TEST_CASE("captions follow the clip, and a long story's last one goes after a while")
{
    const Teimo::Line* five = nullptr;
    const Teimo::Line* story = nullptr;
    for (const auto& line : Teimo::kLines) {
        five = line.clip == "paivaa5" ? &line : five;  // counting the till, then the greeting
        story = line.clip == "random2" ? &line : story;  // 58 s about the weather, two captions
    }
    REQUIRE(five != nullptr);
    REQUIRE(story != nullptr);
    CHECK(Teimo::CaptionAt(*five, 0.0f).starts_with("Five hundred"));
    CHECK(Teimo::CaptionAt(*five, 2.5f).starts_with("Five hundred"));
    CHECK(Teimo::CaptionAt(*five, 2.7f).starts_with("Oh, good day"));
    CHECK(Teimo::CaptionAt(*five, 5.3f).starts_with("Oh, good day"));
    CHECK(Teimo::CaptionAt(*five, 5.4f).empty());
    CHECK(Teimo::CaptionAt(*five, -0.1f).empty());
    CHECK(Teimo::CaptionAt(*story, 9.0f).starts_with("What a strange summer"));
    CHECK(Teimo::CaptionAt(*story, 15.0f).empty());  // kCaptionHold after the first part
    CHECK(Teimo::CaptionAt(*story, 26.0f).starts_with("Even at night"));
    CHECK(Teimo::CaptionAt(*story, 40.0f).empty());
}

TEST_CASE("walking up to the counter greets Kumpo once a visit, drunk he remarks on it after")
{
    Talk talk;
    CHECK(Stay(talk, 0.0, 5.0, kFar).empty());
    CHECK(Stay(talk, 5.0, 6.0, Teimo::kAround).empty());  // near, not at the counter yet
    CHECK((Stay(talk, 6.0, 8.0, 200.0f) == std::vector{ Topic::kGreet }));
    CHECK(talk.Visiting());
    CHECK(Stay(talk, 8.0, 15.0, 200.0f).empty());  // no second greeting, small talk only after a while

    // Off and back within kGreetAgain: no greeting; much later: a new one, then the drunk remark.
    CHECK(Stay(talk, 15.0, 20.0, kFar).empty());
    CHECK_FALSE(talk.Visiting());
    CHECK(Stay(talk, 20.0, 21.0, 200.0f).empty());
    CHECK(Stay(talk, 21.0, 30.0, kFar).empty());
    CHECK((Stay(talk, 200.0, 215.0, 200.0f, 1.2f) == std::vector{ Topic::kGreet, Topic::kDrunk }));
    CHECK((Stay(talk, 215.0, 216.0, 200.0f, 1.2f).empty()));  // once a visit
    CHECK(Stay(talk, 216.0, 220.0, kFar, 1.2f).size() <= 1);     // off (the goodbye)...
    CHECK(Stay(talk, 220.0, 225.0, 200.0f, 1.2f).empty());       // ...and back too soon: no greeting, no second remark
    // Just under MSC's line: no remark.
    Talk sober;
    CHECK((Stay(sober, 0.0, 10.0, 200.0f, Teimo::kDrunkAt - 0.01f) == std::vector{ Topic::kGreet }));
}

TEST_CASE("he makes small talk while Kumpo hangs about, and none once he walks off")
{
    Talk talk;
    const auto said = Stay(talk, 0.0, 300.0, 200.0f);
    REQUIRE(said.size() >= 3);
    CHECK(said[0] == Topic::kGreet);
    CHECK(said[1] == Topic::kChat);
    CHECK(said[2] == Topic::kChat);
    // The first story kChatAfter after the greeting ended, the next kChatEvery after that one began.
    Talk timing;
    double greetEnd = 0.0, chat1 = -1.0, chat2 = -1.0;
    for (double t = 0.0; t < 300.0; t += 0.1) {
        if (const auto topic = timing.Tick(t, 200.0f, 0.0f)) {
            const auto* line = timing.Say(*topic, t, 0.0);
            if (*topic == Topic::kGreet) {
                greetEnd = t + line->seconds;
            } else if (chat1 < 0.0) {
                chat1 = t;
            } else if (chat2 < 0.0) {
                chat2 = t;
            }
        }
    }
    CHECK(chat1 == doctest::Approx(greetEnd + Teimo::kChatAfter).epsilon(0.01));
    CHECK(chat2 - chat1 >= Teimo::kChatEvery - 0.2);
    CHECK(chat2 - chat1 <= Teimo::kChatEvery + 0.2);
    CHECK(Stay(talk, 300.0, 600.0, Teimo::kGone + 10.0f).empty());  // straight past kGone: no goodbye
}

TEST_CASE("walking off after a visit gets his goodbye, once")
{
    Talk talk;
    REQUIRE((Stay(talk, 0.0, 5.0, 200.0f) == std::vector{ Topic::kGreet }));
    CHECK((Stay(talk, 5.0, 6.0, Teimo::kAround + 50.0f) == std::vector{ Topic::kBye }));
    CHECK(Stay(talk, 6.0, 7.0, 200.0f).empty());                       // back to the counter: the same visit
    CHECK(Stay(talk, 7.0, 9.0, Teimo::kAround + 50.0f).empty());        // no second goodbye
    CHECK(Stay(talk, 9.0, 10.0, kFar).empty());
    // Back too soon: no second greeting, but he greeted him a moment ago, so a goodbye again.
    CHECK(Stay(talk, 10.0, 11.0, 200.0f).empty());
    CHECK((Stay(talk, 11.0, 12.0, Teimo::kAround + 50.0f) == std::vector{ Topic::kBye }));
    // Talked to (E) from the edge of the stall, then a walk-up: one greeting, and the goodbye.
    Talk talked;
    REQUIRE((talked.OnTalk(0.0) == Topic::kGreet));
    talked.Say(Topic::kGreet, 0.0, 0.0);
    CHECK(Stay(talked, 0.0, 10.0, 200.0f).empty());
    CHECK((Stay(talked, 10.0, 11.0, Teimo::kAround + 50.0f) == std::vector{ Topic::kBye }));
    // An insult during the visit: no goodbye after it.
    Talk insulted;
    REQUIRE((Stay(insulted, 0.0, 5.0, 200.0f) == std::vector{ Topic::kGreet }));
    REQUIRE((insulted.OnInsult(Topic::kFinger, 5.0, 200.0f) == Topic::kFinger));
    insulted.Say(Topic::kFinger, 5.0, 0.0);
    CHECK(Stay(insulted, 5.0, 30.0, Teimo::kAround + 50.0f).empty());
    // A visit that ended without walking off (Skyrim took the player back): no goodbye, and the
    // next walk-up is a new visit.
    Talk ended;
    REQUIRE((Stay(ended, 0.0, 5.0, 200.0f) == std::vector{ Topic::kGreet }));
    ended.EndVisit();
    CHECK(Stay(ended, 5.0, 6.0, Teimo::kAround + 50.0f).empty());
    CHECK((Stay(ended, 200.0, 201.0, 200.0f) == std::vector{ Topic::kGreet }));
}

TEST_CASE("insults cut in, one reply per outburst, never over another insult")
{
    Talk talk;
    REQUIRE((Stay(talk, 0.0, 1.0, 200.0f) == std::vector{ Topic::kGreet }));
    CHECK(talk.Speaking());
    const auto finger = talk.OnInsult(Topic::kFinger, 1.5, 200.0f);
    REQUIRE((finger == Topic::kFinger));  // over the greeting
    const auto* reply = talk.Say(*finger, 1.5, 0.0);
    REQUIRE(reply != nullptr);
    CHECK_FALSE(talk.OnInsult(Topic::kPissed, 1.6, 200.0f));  // not over the finger's reply
    // The piss stream hits him every frame: one reply, then quiet until it's over and a little more.
    Talk piss;
    REQUIRE((piss.OnInsult(Topic::kPissed, 10.0, 100.0f) == Topic::kPissed));
    const auto* line = piss.Say(Topic::kPissed, 10.0, 0.0);
    int replies = 1;
    for (double t = 10.1; t < 10.0 + line->seconds + 1.0; t += 0.1) {
        if (piss.OnInsult(Topic::kPissed, t, 100.0f)) {
            replies++;
        }
    }
    CHECK(replies == 1);
    CHECK((piss.OnInsult(Topic::kPissed, 60.0, 100.0f) == Topic::kPissed));
    CHECK_FALSE(piss.OnInsult(Topic::kHit, 100.0, Teimo::kHears + 1.0f));  // too far to care
}

TEST_CASE("talking to him greets Kumpo unless he is speaking")
{
    Talk talk;
    REQUIRE((talk.OnTalk(0.0) == Topic::kGreet));
    talk.Say(Topic::kGreet, 0.0, 0.0);
    CHECK_FALSE(talk.OnTalk(1.0));
    CHECK((talk.OnTalk(10.0) == Topic::kGreet));
}

TEST_CASE("he never says the same line twice in a row")
{
    Talk talk;
    const Teimo::Line* last = nullptr;
    for (int i = 0; i < 50; i++) {
        const auto* line = talk.Say(Topic::kFinger, i * 100.0, (i % 7) / 7.0);
        REQUIRE(line != nullptr);
        CHECK(line != last);
        CHECK(line->topic == Topic::kFinger);
        last = line;
    }
}

TEST_CASE("he sees the finger in front of him up to 10 m")
{
    CHECK(Teimo::SeesFinger(0.0f, 600.0f, 0.0f, 1.0f));
    CHECK_FALSE(Teimo::SeesFinger(0.0f, 750.0f, 0.0f, 1.0f));
    CHECK_FALSE(Teimo::SeesFinger(0.0f, -300.0f, 0.0f, 1.0f));
    CHECK_FALSE(Teimo::SeesFinger(400.0f, 300.0f, 0.0f, 1.0f));
}

TEST_CASE("he turns the short way round at his own pace")
{
    constexpr float kPi = 3.14159265f;
    CHECK(Teimo::HeadingTo(0.0f, 100.0f) == doctest::Approx(0.0f));
    CHECK(Teimo::HeadingTo(100.0f, 0.0f) == doctest::Approx(kPi / 2));
    CHECK(Teimo::HeadingTo(-100.0f, 0.0f) == doctest::Approx(3 * kPi / 2));
    // From 350 degrees to 10: through north, not back round.
    const float from = 350.0f * kPi / 180.0f, to = 10.0f * kPi / 180.0f;
    const float step = Teimo::TurnToward(from, to, 0.01f);
    CHECK(step == doctest::Approx(from + Teimo::kTurnPerSecond * 0.01f));
    CHECK(Teimo::TurnToward(from, to, 10.0f) == doctest::Approx(to));
    CHECK(Teimo::TurnToward(1.0f, 1.0f, 0.016f) == doctest::Approx(1.0f));
}
