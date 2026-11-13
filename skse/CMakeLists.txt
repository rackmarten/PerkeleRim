cmake_minimum_required(VERSION 3.21)

project(
    PerkeleRim
    VERSION 0.2.1
    LANGUAGES CXX
)

set(CMAKE_CXX_STANDARD 23)
set(CMAKE_CXX_STANDARD_REQUIRED ON)

# CommonLibSSE-NG is a git submodule pinned to a release tag. On GitHub Actions it links the
# matching prebuilt bundle instead of compiling from source (see extern/CommonLibSSE-NG/PREBUILT.md).
set(BUILD_TESTS OFF CACHE BOOL "Skip CommonLibSSE-NG unit tests" FORCE)
add_subdirectory(extern/CommonLibSSE-NG commonlib EXCLUDE_FROM_ALL)
# Only defined by the prebuilt path upstream, so include the helper explicitly for source builds too.
include(extern/CommonLibSSE-NG/cmake/CommonLibSSE.cmake)

add_commonlibsse_plugin(
    ${PROJECT_NAME}
    NAME "PerkeleRim"
    AUTHOR "Marek Uncik"
    SOURCES src/main.cpp src/link/LinkService.cpp src/player/PlayerSync.cpp src/collision/CollisionSampler.cpp src/collision/StaticMeshes.cpp src/combat/Combat.cpp src/people/People.cpp src/vehicle/VehicleView.cpp src/vehicle/Hitbox.cpp src/vehicle/HitboxTest.cpp src/model/ModelView.cpp src/item/ItemView.cpp src/particle/ParticleView.cpp src/hands/HandView.cpp src/death/Death.cpp src/garage/Garage.cpp src/fuel/Fuel.cpp src/intro/Intro.cpp src/intro/HelgenSkip.cpp src/hud/Hud.cpp src/drink/Drink.cpp src/drink/HeldDrinkView.cpp src/pocket/Pocket.cpp src/junk/ThrowJunk.cpp src/safe/Safe.cpp src/safe/MetalHit.cpp src/autoload/Autoload.cpp src/save/Cosave.cpp src/save/SaveSync.cpp src/rest/Rest.cpp src/shot/Shot.cpp src/shot/BackBuffer.cpp src/debug/HitTest.cpp src/menu/MscMenuView.cpp src/needs/NeedEffects.cpp src/blackout/Blackout.cpp ../protocol/cpp/link.cpp
)

# skse-test-kit (docs/DEV_SETUP.md, "Test sessions"): a submodule at extern/skse-test-kit that plain and recursive
# clones leave empty (update = none). Dev builds link it when it's checked out (scripts/build.ps1 turns this on);
# scripts/package.ps1 and CI build without it, and a release never has it.
option(PERKELERIM_TEST_KIT "Link skse-test-kit for test sessions (dev builds only)" OFF)
set(PERKELERIM_TEST_KIT_DIR "${CMAKE_CURRENT_SOURCE_DIR}/extern/skse-test-kit" CACHE PATH "The skse-test-kit checkout")
set(PERKELERIM_WITH_TEST_KIT OFF)
if(PERKELERIM_TEST_KIT)
    if(EXISTS "${PERKELERIM_TEST_KIT_DIR}/CMakeLists.txt")
        add_subdirectory("${PERKELERIM_TEST_KIT_DIR}" skse-test-kit)
        set(PERKELERIM_WITH_TEST_KIT ON)
    else()
        message(WARNING "PERKELERIM_TEST_KIT is on, but ${PERKELERIM_TEST_KIT_DIR} is empty: building without the test kit. "
            "Fetch it with: git submodule update --init --checkout skse/extern/skse-test-kit")
    endif()
endif()
if(PERKELERIM_WITH_TEST_KIT)
    # Only TestKit.cpp sees the kit's headers and the library file is linked as it is (CommonLib is already linked),
    # so turning the option on or off rebuilds that file and the link, not every source.
    target_sources(${PROJECT_NAME} PRIVATE src/debug/TestKit.cpp)
    set_source_files_properties(src/debug/TestKit.cpp PROPERTIES INCLUDE_DIRECTORIES "${PERKELERIM_TEST_KIT_DIR}/include")
    add_dependencies(${PROJECT_NAME} skse_test_kit)
    target_link_libraries(${PROJECT_NAME} PRIVATE "$<TARGET_FILE:skse_test_kit>")
    message(STATUS "PerkeleRim: with skse-test-kit (${PERKELERIM_TEST_KIT_DIR})")
else()
    target_sources(${PROJECT_NAME} PRIVATE src/debug/TestKitOff.cpp)
endif()

target_precompile_headers(${PROJECT_NAME} PRIVATE src/PCH.h)
target_include_directories(${PROJECT_NAME} PRIVATE src ../protocol/cpp)
# The shared link code includes <Windows.h> itself and must not pick up CommonLib's PCH.
set_source_files_properties(../protocol/cpp/link.cpp PROPERTIES SKIP_PRECOMPILE_HEADERS ON)
# So does the back buffer grab, which needs the real d3d11.h.
set_source_files_properties(src/shot/BackBuffer.cpp PROPERTIES SKIP_PRECOMPILE_HEADERS ON)

# The empty Scaleform movie PerkeleRim's own Skyrim menus load (protocol/cpp/swf.h), next to the DLL;
# deploy-local.ps1 and package.ps1 put it in Data/Interface/PerkeleRim.
add_executable(swfgen tools/swfgen.cpp)
target_include_directories(swfgen PRIVATE ../protocol/cpp)
add_custom_command(TARGET ${PROJECT_NAME} POST_BUILD
    COMMAND swfgen "$<TARGET_FILE_DIR:${PROJECT_NAME}>/MscMenu.swf"
    COMMENT "Writing MscMenu.swf")
add_dependencies(${PROJECT_NAME} swfgen)
