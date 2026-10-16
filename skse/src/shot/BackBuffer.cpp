#include "shot/BackBuffer.h"

#include <Windows.h>
#include <d3d11.h>
#include <dxgi.h>
#include <wincodec.h>

#include <ScreenGrab.h>

#include <mutex>

namespace PerkeleRim::BackBuffer
{
    namespace
    {
        using PresentFn = HRESULT(STDMETHODCALLTYPE*)(IDXGISwapChain*, UINT, UINT);
        constexpr std::size_t kPresentSlot = 8;  // IDXGISwapChain::Present in its vtable

        PresentFn    original = nullptr;
        std::mutex   lock;
        std::wstring pending;  // empty = no shot waiting
        Done         done = nullptr;

        HRESULT Save(IDXGISwapChain* a_swapChain, const std::wstring& a_path)
        {
            ID3D11Device*        device = nullptr;
            ID3D11DeviceContext* context = nullptr;
            ID3D11Texture2D*     frame = nullptr;
            HRESULT              result = a_swapChain->GetDevice(__uuidof(ID3D11Device), reinterpret_cast<void**>(&device));
            if (SUCCEEDED(result)) {
                device->GetImmediateContext(&context);
                result = a_swapChain->GetBuffer(0, __uuidof(ID3D11Texture2D), reinterpret_cast<void**>(&frame));
            }
            if (SUCCEEDED(result)) {
                // WIC wants COM on this thread; the game usually has it already.
                const HRESULT com = CoInitializeEx(nullptr, COINIT_MULTITHREADED);
                result = DirectX::SaveWICTextureToFile(context, frame, GUID_ContainerFormatPng, a_path.c_str(), &GUID_WICPixelFormat24bppBGR);
                if (SUCCEEDED(com)) {
                    CoUninitialize();
                }
            }
            if (frame) frame->Release();
            if (context) context->Release();
            if (device) device->Release();
            return result;
        }

        HRESULT STDMETHODCALLTYPE Present(IDXGISwapChain* a_swapChain, UINT a_syncInterval, UINT a_flags)
        {
            std::wstring path;
            Done         report = nullptr;
            {
                std::scoped_lock guard(lock);
                path.swap(pending);
                report = done;
            }
            if (!path.empty()) {
                const HRESULT result = Save(a_swapChain, path);
                if (report) {
                    report(path, SUCCEEDED(result) ? 0 : result);
                }
            }
            return original(a_swapChain, a_syncInterval, a_flags);
        }

        bool Hook(IDXGISwapChain* a_swapChain)
        {
            if (original) {
                return true;
            }
            auto** vtable = *reinterpret_cast<void***>(a_swapChain);
            DWORD  protection = 0;
            if (!VirtualProtect(&vtable[kPresentSlot], sizeof(void*), PAGE_EXECUTE_READWRITE, &protection)) {
                return false;
            }
            original = reinterpret_cast<PresentFn>(vtable[kPresentSlot]);
            vtable[kPresentSlot] = reinterpret_cast<void*>(&Present);
            VirtualProtect(&vtable[kPresentSlot], sizeof(void*), protection, &protection);
            return true;
        }
    }

    bool Request(void* a_swapChain, const std::wstring& a_path, Done a_done)
    {
        if (!a_swapChain || a_path.empty()) {
            return false;
        }
        std::scoped_lock guard(lock);
        if (!pending.empty() || !Hook(static_cast<IDXGISwapChain*>(a_swapChain))) {
            return false;
        }
        pending = a_path;
        done = a_done;
        return true;
    }
}
