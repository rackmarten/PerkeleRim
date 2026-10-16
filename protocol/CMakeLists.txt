# Unit tests for the C++ end of the link: layout, coordinate conversion, collision chunks, shared memory link.
# Standalone (no CommonLibSSE-NG), so it builds in seconds. Run with scripts/test.ps1.
cmake_minimum_required(VERSION 3.21)

project(PerkeleRimProtocolTests LANGUAGES CXX)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

find_package(doctest CONFIG REQUIRED)

add_executable(protocol_tests
    cpp/link.cpp
    tests/cpp/main.cpp
    tests/cpp/test_camera.cpp
    tests/cpp/test_collision.cpp
    tests/cpp/test_coords.cpp
    tests/cpp/test_damage.cpp
    tests/cpp/test_input.cpp
    tests/cpp/test_link.cpp
    tests/cpp/test_model.cpp
    tests/cpp/test_msc_save.cpp
    tests/cpp/test_shout.cpp
)
target_link_libraries(protocol_tests PRIVATE doctest::doctest)
target_compile_definitions(protocol_tests PRIVATE PROTOCOL_DIR="${CMAKE_CURRENT_SOURCE_DIR}")
if(MSVC)
    target_compile_options(protocol_tests PRIVATE /W4 /permissive- /utf-8)
endif()

enable_testing()
add_test(NAME protocol_tests COMMAND protocol_tests)
