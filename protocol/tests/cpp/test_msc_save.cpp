#include <doctest/doctest.h>

#include <cstdlib>
#include <filesystem>
#include <fstream>
#include <string>

#include "../../cpp/msc_save.h"

using namespace PerkeleRim;
using namespace PerkeleRim::Protocol;

TEST_CASE("an MSC snapshot request carries its key and folder")
{
    const std::string path = "C:\\Users\\me\\Documents\\My Games\\Skyrim Special Edition\\Saves\\PerkeleRim\\Save 3 - P\xC3\xA4ivi";
    const auto        payload = MscSave::Encode(0x1234'5678'9ABC'DEF0ull, path);
    REQUIRE(payload.has_value());
    CHECK(payload->size() == sizeof(MscSnapshot) + path.size());
    const auto snapshot = MscSave::Decode(payload->data(), (std::uint32_t)payload->size());
    REQUIRE(snapshot.has_value());
    CHECK(snapshot->key == 0x1234'5678'9ABC'DEF0ull);
    CHECK(snapshot->path == path);
}

TEST_CASE("malformed MSC snapshot requests are refused")
{
    CHECK_FALSE(MscSave::Encode(0, "folder").has_value());                                   // key 0
    CHECK_FALSE(MscSave::Encode(1, std::string(kMscSnapshotPathMax + 1, 'a')).has_value());  // path too long
    CHECK(MscSave::Encode(1, std::string(kMscSnapshotPathMax, 'a')).has_value());

    const auto ok = *MscSave::Encode(7, "folder");
    CHECK_FALSE(MscSave::Decode(ok.data(), 8).has_value());                                // no full header
    CHECK_FALSE(MscSave::Decode(ok.data(), (std::uint32_t)ok.size() - 1).has_value());  // path cut short
    auto zero = ok;
    std::memset(zero.data(), 0, 8);
    CHECK_FALSE(MscSave::Decode(zero.data(), (std::uint32_t)zero.size()).has_value());  // key 0
}

TEST_CASE("snapshots of deleted saves are pruned, others kept")
{
    namespace fs = std::filesystem;
    const auto root = fs::temp_directory_path() / ("PerkeleRim.Prune." + std::to_string(::rand()));
    const auto saves = root / "Saves";
    const auto snapshots = saves / "PerkeleRim";
    fs::create_directories(snapshots);
    std::ofstream(saves / "Save 1.ess") << "x";
    fs::create_directories(saves / "Lydia");
    std::ofstream(saves / "Lydia" / "Save 4.ess") << "x";  // a save manager's character folder
    const auto old = fs::file_time_type::clock::now() - std::chrono::hours(1);
    std::ofstream(snapshots / "Save 2.ess") << "x";  // inside the snapshots: not a save
    for (const char* name : { "Save 1", "Save 2", "Save 4", "Quicksave0", "Fresh" }) {
        fs::create_directories(snapshots / name);
        std::ofstream(snapshots / name / "defaultES2File.txt") << "x";
        if (std::string(name) != "Fresh") {
            fs::last_write_time(snapshots / name, old);
        }
    }

    // Save 1 and Save 4 still have their saves, Quicksave0 is being written, Fresh changed just now.
    const auto removed = MscSave::Prune(saves, snapshots, "Quicksave0");
    REQUIRE(removed.size() == 1);
    CHECK(removed[0] == "Save 2");
    CHECK(fs::exists(snapshots / "Save 1"));
    CHECK(fs::exists(snapshots / "Save 4"));
    CHECK(fs::exists(snapshots / "Quicksave0"));
    CHECK(fs::exists(snapshots / "Fresh"));
    CHECK_FALSE(fs::exists(snapshots / "Save 2"));

    CHECK(MscSave::Prune(saves, root / "missing", "").empty());
    fs::remove_all(root);
}
