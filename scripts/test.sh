#!/usr/bin/env bash
# Runs the game-free tests on Linux: the C++ protocol tests (the linux-tests preset: Ninja, gcc or
# clang, doctest fetched by CMake) and the C# tests (dotnet test, minus LinkTests). Needs cmake 3.21+,
# ninja, a C++23 compiler and the .NET 8 SDK. The shared memory link's tests (C++ test_link.cpp, C#
# LinkTests) use Windows' API and run only in scripts/test.ps1.
#   scripts/test.sh                  # both
#   scripts/test.sh cpp              # or cs
set -euo pipefail

target="${1:-all}"
root="$(cd "$(dirname "$0")/.." && pwd)"

if [[ "$target" == all || "$target" == cpp ]]; then
    cd "$root/protocol"
    cmake --preset linux-tests
    cmake --build --preset linux-tests
    ctest --preset linux-tests
fi

if [[ "$target" == all || "$target" == cs ]]; then
    dotnet test "$root/msc/tests/PerkeleRim.Tests.csproj" -nologo --filter 'FullyQualifiedName!~PerkeleRim.Tests.LinkTests'
fi
