#include <doctest/doctest.h>

#include "../../cpp/piss_tap.h"

using namespace PerkeleRim;

TEST_CASE("a clean Alt tap holds Alt for MSC for the pulse, then lets go")
{
    PissTap::Tap tap;
    CHECK_FALSE(tap.Tick(0.016f));
    tap.Press();
    CHECK_FALSE(tap.Tick(0.016f));  // nothing while Alt is down
    CHECK(tap.Release(0.15f, true));
    CHECK(tap.Tick(0.1f));
    CHECK(tap.Tick(0.1f));
    CHECK(tap.Tick(0.1f));  // 0.05 s were still left
    CHECK_FALSE(tap.Tick(0.1f));
}

TEST_CASE("Alt+Tab, a long hold or losing the window is no tap")
{
    PissTap::Tap tap;
    tap.Press();
    tap.OtherKey();  // Tab
    CHECK_FALSE(tap.Release(0.1f, true));
    CHECK_FALSE(tap.Tick(0.016f));

    tap.Press();
    CHECK_FALSE(tap.Release(PissTap::kMaxTap + 0.01f, true));

    tap.Press();
    CHECK_FALSE(tap.Release(0.1f, false));  // released after Windows switched away
    CHECK_FALSE(tap.Tick(0.016f));
}

TEST_CASE("keys pressed before Alt or with Alt up don't spoil the next tap")
{
    PissTap::Tap tap;
    tap.OtherKey();  // W, already walking
    tap.Press();
    CHECK(tap.Release(0.1f, true));

    tap.Reset();
    CHECK_FALSE(tap.Tick(0.016f));
    CHECK_FALSE(tap.Release(0.1f, true));  // a release without a press (the press came before a load)
}
