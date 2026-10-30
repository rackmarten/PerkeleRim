#include <doctest/doctest.h>

#include "../../cpp/msc_menu.h"

using namespace PerkeleRim;
using MscMenu::Phase;
using MscMenu::Say;

namespace
{
    MscMenu::Frame Up(bool a_mscOpen, float a_delta = 0.1f)
    {
        MscMenu::Frame f;
        f.owned = true;
        f.linkUp = true;
        f.mscOpen = a_mscOpen;
        f.delta = a_delta;
        return f;
    }
}

TEST_CASE("F1 opens the menu only while MSC has the player and the link is up")
{
    MscMenu::Machine m;
    MscMenu::Frame   skyrim = Up(false);
    skyrim.owned = false;
    CHECK_FALSE(m.Press(skyrim));
    MscMenu::Frame down = Up(false);
    down.linkUp = false;
    CHECK_FALSE(m.Press(down));
    CHECK(m.Current() == Phase::Closed);
    CHECK_FALSE(m.Requested());

    CHECK(m.Press(Up(false)));
    CHECK(m.Current() == Phase::Opening);
    CHECK(m.Requested());
    CHECK(m.Shown());
    CHECK_FALSE(m.Press(Up(false)));  // a second press while opening doesn't restart it
}

TEST_CASE("the menu is open once MSC says so, and Resume in MSC closes it")
{
    MscMenu::Machine m;
    REQUIRE(m.Press(Up(false)));
    CHECK(m.Tick(Up(false)) == Say::None);
    CHECK(m.Tick(Up(true)) == Say::Opened);
    CHECK(m.Current() == Phase::Open);
    CHECK(m.Tick(Up(true)) == Say::None);
    CHECK(m.Tick(Up(false)) == Say::Closed);
    CHECK(m.Current() == Phase::Closed);
    CHECK_FALSE(m.Shown());
}

TEST_CASE("F1 or Escape closes Skyrim's side at once and waits for MSC's menu to go")
{
    MscMenu::Machine m;
    REQUIRE(m.Press(Up(false)));
    REQUIRE(m.Tick(Up(true)) == Say::Opened);
    m.Close();
    CHECK(m.Current() == Phase::Closing);
    CHECK_FALSE(m.Requested());
    CHECK_FALSE(m.Shown());
    CHECK(m.Tick(Up(true)) == Say::None);
    CHECK(m.Tick(Up(false)) == Say::Closed);
    CHECK(m.Current() == Phase::Closed);
    CHECK(m.Press(Up(false)));  // and opens again
}

TEST_CASE("a menu MSC never closes stops being waited for")
{
    MscMenu::Machine m;
    REQUIRE(m.Press(Up(false)));
    REQUIRE(m.Tick(Up(true)) == Say::Opened);
    m.Close();
    Say said = Say::None;
    for (int i = 0; i < 40 && said == Say::None; i++) {
        said = m.Tick(Up(true));
    }
    CHECK(said == Say::Closed);
    CHECK(m.Current() == Phase::Closed);
}

TEST_CASE("closing while it still opens doesn't wait for a menu that never came")
{
    MscMenu::Machine m;
    REQUIRE(m.Press(Up(false)));
    m.Close();
    CHECK(m.Tick(Up(false)) == Say::Closed);
}

TEST_CASE("MSC not answering gives up after kOpenSeconds")
{
    MscMenu::Machine m;
    REQUIRE(m.Press(Up(false)));
    CHECK(m.Tick(Up(false, MscMenu::kOpenSeconds - 0.5f)) == Say::None);
    CHECK(m.Tick(Up(false, 0.6f)) == Say::NoAnswer);
    CHECK(m.Current() == Phase::Closed);
}

TEST_CASE("handing the player back or the link dropping abandons the menu")
{
    MscMenu::Machine m;
    REQUIRE(m.Press(Up(false)));
    REQUIRE(m.Tick(Up(true)) == Say::Opened);
    MscMenu::Frame back = Up(true);
    back.owned = false;
    CHECK(m.Tick(back) == Say::Abandoned);
    CHECK(m.Current() == Phase::Closed);

    REQUIRE(m.Press(Up(false)));
    MscMenu::Frame down = Up(false);
    down.linkUp = false;
    CHECK(m.Tick(down) == Say::Abandoned);
    CHECK(m.Tick(down) == Say::None);  // said once
}

TEST_CASE("F1 and Escape close the menu, other keys go to MSC")
{
    CHECK(MscMenu::ClosesMenu(MscMenu::kDikF1));
    CHECK(MscMenu::ClosesMenu(Input::kDikEscape));
    CHECK_FALSE(MscMenu::ClosesMenu(Input::kDikTab));
    CHECK_FALSE(MscMenu::ClosesMenu(0x1C));  // Return
}

TEST_CASE("the cursor goes to MSC as 0..1 of Skyrim's screen")
{
    CHECK(MscMenu::Normalize(0.0f, 1920.0f) == 0.0f);
    CHECK(MscMenu::Normalize(960.0f, 1920.0f) == doctest::Approx(0.5f));
    CHECK(MscMenu::Normalize(1080.0f, 1080.0f) == 1.0f);
    CHECK(MscMenu::Normalize(-20.0f, 1080.0f) == 0.0f);
    CHECK(MscMenu::Normalize(5000.0f, 1080.0f) == 1.0f);
    CHECK(MscMenu::Normalize(100.0f, 0.0f) == 0.5f);  // no screen size yet
}

TEST_CASE("the cursor is a share of the back buffer the menu is drawn over")
{
    CHECK(MscMenu::CursorSpan(1920.0f, 1800.0f) == 1920.0f);
    CHECK(MscMenu::CursorSpan(0.0f, 1800.0f) == 1800.0f);  // renderer not ready yet
}
