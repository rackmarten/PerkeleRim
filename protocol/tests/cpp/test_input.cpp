#include <doctest/doctest.h>

#include "../../cpp/input.h"

using namespace PerkeleRim;
using namespace PerkeleRim::Input;

TEST_CASE("scan codes map to Unity key codes")
{
    CHECK(KeyFromScanCode(0x11) == KeyCode::W);
    CHECK(KeyFromScanCode(0x1E) == KeyCode::A);
    CHECK(KeyFromScanCode(0x1F) == KeyCode::S);
    CHECK(KeyFromScanCode(0x20) == KeyCode::D);
    CHECK(KeyFromScanCode(0x2E) == 99);   // C (crouch)
    CHECK(KeyFromScanCode(0x21) == 102);  // F (use)
    CHECK(KeyFromScanCode(0x19) == 112);  // P (urinate)
    CHECK(KeyFromScanCode(0x31) == 110);  // N (swear)
    CHECK(KeyFromScanCode(0x2C) == 122);  // Z (finger while handed over)
    CHECK(KeyFromScanCode(0x02) == 49);   // Alpha1
    CHECK(KeyFromScanCode(0x0B) == 48);   // Alpha0
    CHECK(KeyFromScanCode(0x2A) == 304);  // LeftShift (run)
    CHECK(KeyFromScanCode(0x1D) == 306);  // LeftControl (zoom)
    CHECK(KeyFromScanCode(0x39) == KeyCode::Space);
    CHECK(KeyFromScanCode(0x1C) == 13);   // Return (enter/exit vehicle)
    CHECK(KeyFromScanCode(0x3B) == 282);  // F1
    CHECK(KeyFromScanCode(0x42) == 289);  // F8
    CHECK(KeyFromScanCode(0x57) == 292);  // F11
    CHECK(KeyFromScanCode(0xC8) == KeyCode::UpArrow);
}

TEST_CASE("keys that stay with Skyrim never reach MSC")
{
    CHECK(KeyFromScanCode(kDikEscape) == KeyCode::None);
    CHECK(KeyFromScanCode(kDikTab) == KeyCode::None);
    CHECK(KeyFromScanCode(kDikGrave) == KeyCode::None);
    CHECK(KeyFromScanCode(kDikF5) == KeyCode::None);  // quicksave
    CHECK(KeyFromScanCode(kDikF9) == KeyCode::None);  // quickload
    CHECK(KeyFromScanCode(kDikF10) == KeyCode::None);
    CHECK(KeyFromScanCode(kDikM) == KeyCode::None);  // Skyrim's map
}

TEST_CASE("mouse buttons map to Mouse0..Mouse6, the wheel to nothing")
{
    CHECK(KeyFromMouseButton(0) == 323);
    CHECK(KeyFromMouseButton(1) == 324);
    CHECK(KeyFromMouseButton(6) == 329);
    CHECK(KeyFromMouseButton(8) == KeyCode::None);
    CHECK(KeyFromMouseButton(9) == KeyCode::None);
}

TEST_CASE("builder sets key bits like the C# mirror reads them")
{
    Builder b;
    b.SetKey(KeyCode::W, true);
    b.SetKey(323, true);
    auto s = b.Build(7, true, true);
    CHECK(s.frame == 7);
    CHECK(s.flags == Protocol::kInputOwnsPlayer);
    CHECK((s.keys[KeyCode::W >> 6] & (1ull << (KeyCode::W & 63))) != 0);
    CHECK((s.keys[323 >> 6] & (1ull << (323 & 63))) != 0);

    b.SetKey(KeyCode::W, false);
    s = b.Build(8, true, true);
    CHECK((s.keys[KeyCode::W >> 6] & (1ull << (KeyCode::W & 63))) == 0);

    b.SetKey(KeyCode::None, true);  // ignored
    b.SetKey(600, true);            // out of range, ignored
    CHECK(!b.IsKeyDown(KeyCode::None));
}

TEST_CASE("builder derives Horizontal and Vertical from WASD and arrows")
{
    Builder b;
    CHECK(b.Build(0, true, true).vertical == 0.0f);
    b.SetKey(KeyCode::W, true);
    CHECK(b.Build(0, true, true).vertical == 1.0f);
    b.SetKey(KeyCode::DownArrow, true);
    CHECK(b.Build(0, true, true).vertical == 0.0f);  // both directions cancel
    b.SetKey(KeyCode::A, true);
    CHECK(b.Build(0, true, true).horizontal == -1.0f);
}

TEST_CASE("mouse movement accumulates with the axis scale and Unity's direction")
{
    Builder b;
    b.AddMouse(10, 0);
    b.AddMouse(5, -20);
    auto s = b.Build(0, true, true);
    CHECK(s.mouseX == doctest::Approx(15 * kMouseAxisPerCount));
    CHECK(s.mouseY == doctest::Approx(20 * kMouseAxisPerCount));  // moving the mouse up (negative dy) looks up
}

TEST_CASE("wheel buttons become notches that accumulate on the scroll axis")
{
    CHECK(WheelNotches(8) == 1);
    CHECK(WheelNotches(9) == -1);
    CHECK(WheelNotches(0) == 0);
    Builder b;
    b.AddWheel(WheelNotches(8));
    b.AddWheel(WheelNotches(8));
    b.AddWheel(WheelNotches(9));
    CHECK(b.Build(0, true, true).mouseWheel == doctest::Approx(kWheelAxisPerNotch));
    CHECK(b.Build(0, true, false).mouseWheel == doctest::Approx(kWheelAxisPerNotch));  // totals kept while paused
}

TEST_CASE("a paused game sends no keys or axes but keeps the mouse totals")
{
    Builder b;
    b.SetKey(KeyCode::W, true);
    b.AddMouse(10, 0);
    auto s = b.Build(0, true, false);
    CHECK(s.keys[KeyCode::W >> 6] == 0);
    CHECK(s.vertical == 0.0f);
    CHECK(s.mouseX == doctest::Approx(10 * kMouseAxisPerCount));
    CHECK(s.flags == Protocol::kInputOwnsPlayer);

    b.ReleaseAll();
    CHECK(!b.IsKeyDown(KeyCode::W));
    CHECK(b.Build(0, false, true).flags == 0);
}

TEST_CASE("a game paused by a menu tells MSC to freeze, only while MSC owns the player")
{
    Builder b;
    CHECK(b.Build(0, true, false, true).flags == (Protocol::kInputOwnsPlayer | Protocol::kInputPaused));
    CHECK(b.Build(0, true, false, false).flags == Protocol::kInputOwnsPlayer);  // dialogue: no keys, time runs
    CHECK(b.Build(0, false, false, true).flags == 0);
}
