cmake_minimum_required(VERSION 3.21)

project(
    PerkeleRim
    VERSION 0.1.1
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
    SOURCES src/main.cpp src/link/LinkService.cpp src/player/PlayerSync.cpp src/collision/CollisionSampler.cpp src/collision/StaticMeshes.cpp src/combat/Combat.cpp src/vehicle/VehicleView.cpp src/vehicle/Hitbox.cpp src/vehicle/HitboxTest.cpp src/model/ModelView.cpp src/item/ItemView.cpp src/particle/ParticleView.cpp src/hands/HandView.cpp src/death/Death.cpp src/garage/Garage.cpp src/fuel/Fuel.cpp src/intro/Intro.cpp src/hud/Hud.cpp src/drink/Drink.cpp src/pocket/Pocket.cpp src/safe/Safe.cpp src/autoload/Autoload.cpp src/save/Cosave.cpp src/save/SaveSync.cpp src/rest/Rest.cpp src/shot/Shot.cpp src/shot/BackBuffer.cpp src/debug/HitTest.cpp src/menu/MscMenuView.cpp ../protocol/cpp/link.cpp
)

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
