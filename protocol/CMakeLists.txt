# Unit tests for the C++ end of the link: layout, coordinate conversion, collision chunks, shared memory link.
# Standalone (no CommonLibSSE-NG), so it builds in seconds. Run with scripts/test.ps1 on Windows or
# scripts/test.sh on Linux, where the shared memory link (Windows' API) is left out.
cmake_minimum_required(VERSION 3.21)

project(PerkeleRimProtocolTests LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# vcpkg's doctest on Windows; the Linux preset downloads a pinned release instead, so it needs no vcpkg.
option(PERKELERIM_FETCH_DOCTEST "Download doctest with FetchContent instead of find_package" OFF)
if(PERKELERIM_FETCH_DOCTEST)
    include(FetchContent)
    FetchContent_Declare(doctest
        URL https://github.com/doctest/doctest/archive/refs/tags/v2.4.12.tar.gz
        URL_HASH SHA256=73381c7aa4dee704bd935609668cf41880ea7f19fa0504a200e13b74999c2d70
        DOWNLOAD_EXTRACT_TIMESTAMP TRUE)
    set(DOCTEST_NO_INSTALL ON CACHE BOOL "" FORCE)
    # doctest asks for CMake 3.0, which CMake 4 refuses without this.
    set(CMAKE_POLICY_VERSION_MINIMUM 3.5)
    FetchContent_MakeAvailable(doctest)
else()
    find_package(doctest CONFIG REQUIRED)
endif()

add_executable(protocol_tests
    tests/cpp/main.cpp
    tests/cpp/test_camera.cpp
    tests/cpp/test_collision.cpp
    tests/cpp/test_coords.cpp
    tests/cpp/test_damage.cpp
    tests/cpp/test_finger.cpp
    tests/cpp/test_input.cpp
    tests/cpp/test_model.cpp
    tests/cpp/test_msc_save.cpp
    tests/cpp/test_piss_tap.cpp
    tests/cpp/test_time_skip.cpp
    tests/cpp/test_wading.cpp
    tests/cpp/test_hitbox.cpp
    tests/cpp/test_food.cpp
    tests/cpp/test_hotbar.cpp
    tests/cpp/test_hurt.cpp
    tests/cpp/test_shout.cpp
    tests/cpp/test_stamina.cpp
    tests/cpp/test_screenshot.cpp
    tests/cpp/test_hit_test.cpp
    tests/cpp/test_play_mode.cpp
    tests/cpp/test_intro.cpp
    tests/cpp/test_msc_menu.cpp
    tests/cpp/test_swf.cpp
)
if(WIN32)
    target_sources(protocol_tests PRIVATE cpp/link.cpp tests/cpp/test_link.cpp)
endif()
target_link_libraries(protocol_tests PRIVATE doctest::doctest)
target_compile_definitions(protocol_tests PRIVATE PROTOCOL_DIR="${CMAKE_CURRENT_SOURCE_DIR}")
if(MSVC)
    target_compile_options(protocol_tests PRIVATE /W4 /permissive- /utf-8)
else()
    target_compile_options(protocol_tests PRIVATE -Wall -Wextra)
endif()

enable_testing()
add_test(NAME protocol_tests COMMAND protocol_tests)
