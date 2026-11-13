#pragma once

#include <optional>
#include <string>

// The hitbox test, run as the test kit's pr.hitbox scenario (skse/src/debug/TestKit.cpp, scenarios/hitbox.json,
// scripts/hitbox-test.ps1): with MSC having the player on foot, it calls the Hayosiko van in front of him (], again
// after 30 s while it isn't there) and, once the van has stood solid near him for a few seconds, places a melee bandit
// behind the van (seen from Kumpo) and two archers with their AI off, the shooters of 12 arrows the test fires itself
// at Kumpo: 8 from just behind the van, through it (each also cast as a ray first, logging what it meets), and 4 from
// his side as a control. For a minute it logs every projectile impact near the van (one of its capsules, Kumpo or
// anything else, and the layer), how close the melee bandit comes to Kumpo and how deep he ever stands inside the
// van's box, and the navmesh obstacles of the loaded cells; it keeps Kumpo alive, then takes the bandits away. Every
// log line is also a Note, and the counts are its Result. Main thread only; inert until Begin.
namespace PerkeleRim::HitboxTest
{
    struct Note
    {
        float       seconds = 0.0f;  // since the arrows' minute began (before it: since Begin)
        std::string what;
    };

    struct Result
    {
        bool        done = false;  // the minute ran out
        std::string stopped;       // why it ended early
        int         through = 0, throughStopped = 0, throughOnKumpo = 0;  // arrows fired through the van, and where they ended
        int         side = 0, sideOnKumpo = 0;                              // control arrows from Kumpo's side
        int         vanHits = 0;                                            // projectiles counted on the van's capsules
        float       meleeClosest = 0.0f;  // the melee bandit's least distance to Kumpo
        int         meleeInside = 0;      // samples he stood inside the van's box
        int         samples = 0;          // of these
        float       meleeDeepest = 0.0f;  // the deepest he stood inside it
        int         obstaclesAtStart = 0, obstaclesAtEnd = 0;  // navmesh obstacles in the loaded cells
    };

    // Empty when it starts, else why it can't (MSC hasn't the player, he sits in a vehicle, a test running).
    std::string Begin();
    void        Stop(const char* a_why);
    bool        Running();
    std::optional<Note> NextNote();
    const Result&       Last();

    // Every frame MSC has the player, after the vehicles are drawn.
    void Update(RE::PlayerCharacter* a_player, float a_delta);
}
