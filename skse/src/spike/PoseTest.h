#pragma once

// Hidden-MSC spike: can an external pose drive the Skyrim player every frame?
// F10 cycles Off -> Place -> PlaceStill -> Off. While on, the player follows a scripted circle
// and the log reports how far Skyrim moved the player away from our pose between frames.
namespace PerkeleRim::Spike::PoseTest
{
    void Install();          // hook; call at SKSE load
    void OnDataLoaded();     // input sink; call on kDataLoaded
}
