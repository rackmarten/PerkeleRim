# protocol

The shared memory link between the two halves. Explained in [docs/PROTOCOL.md](../docs/PROTOCOL.md).

- `perkelerim_protocol.h` layout source of truth (structs, offsets, constants), mirrored in `msc/src/Link/Protocol.cs`
- `cpp/link.h`, `cpp/link.cpp` C++ end of the link, compiled into the SKSE plugin
- `cpp/coords.h` Skyrim <-> Unity coordinate conversion
- `tests/` C++ unit tests (doctest) and `coords_vectors.csv`, shared with the C# tests in `msc/tests`

Run all tests with `scripts/test.ps1` (`-Target cpp` for just these). The test project is standalone CMake + vcpkg (`CMakePresets.json` preset `tests`), so it builds in seconds without CommonLibSSE-NG. On Linux, `scripts/test.sh cpp` uses the `linux-tests` preset instead (Ninja, GCC or Clang, doctest downloaded by CMake, no vcpkg) and leaves out the shared memory link (`cpp/link.cpp`, `tests/cpp/test_link.cpp`), which needs Windows.
