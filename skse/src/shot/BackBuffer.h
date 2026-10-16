#pragma once

#include <string>

// Skyrim's frame as a PNG: the next Present of the game's swap chain saves its back buffer first
// (DirectXTK's ScreenGrab, through WIC, without alpha). Compiled without CommonLib's PCH, since it
// needs the real d3d11.h, so it only takes plain pointers.
namespace PerkeleRim::BackBuffer
{
    // Called on the render thread once the file is written (a_result 0) or failed (an HRESULT).
    using Done = void (*)(const std::wstring& a_path, long a_result);

    // Hooks a_swapChain's Present the first time (IDXGISwapChain*); false when there is none or a
    // shot is still waiting for its frame.
    bool Request(void* a_swapChain, const std::wstring& a_path, Done a_done);
}
