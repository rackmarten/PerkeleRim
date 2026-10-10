using System;
using System.Runtime.InteropServices;

namespace PerkeleRim.Game
{
    // A D3D11 texture another process can open (legacy DXGI shared handle), kept a copy of one of
    // Unity's textures. Plain COM calls through vtable slots, so MSC needs no native DLL. Call only
    // where Unity's renderer isn't using the device context at the same time: MSC runs with
    // -force-gfx-direct (no render thread), so the main thread is fine.
    public sealed class SharedTexture : IDisposable
    {
        // Vtable slots (d3d11.h / dxgi.h declaration order, IUnknown's three first).
        const int SlotQueryInterface = 0, SlotRelease = 2;
        const int SlotChildGetDevice = 3;            // ID3D11DeviceChild::GetDevice
        const int SlotTextureGetDesc = 10;           // ID3D11Texture2D::GetDesc
        const int SlotDeviceCreateTexture2D = 5;     // ID3D11Device::CreateTexture2D
        const int SlotDeviceGetImmediateContext = 40;
        const int SlotContextCopyResource = 47;      // ID3D11DeviceContext::CopyResource
        const int SlotContextFlush = 111;
        const int SlotResourceGetSharedHandle = 8;   // IDXGIResource::GetSharedHandle

        const uint BindShaderResource = 0x8, BindRenderTarget = 0x20;
        const uint MiscShared = 0x2;

        static Guid IidDxgiResource = new Guid("035f3ab4-482e-4e50-b41f-8a7f8bd8960b");

        [StructLayout(LayoutKind.Sequential)]
        struct Texture2DDesc
        {
            public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CpuAccessFlags, MiscFlags;
        }

        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int QueryInterfaceFn(IntPtr self, ref Guid iid, out IntPtr result);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint ReleaseFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void GetDeviceFn(IntPtr self, out IntPtr device);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void GetDescFn(IntPtr self, out Texture2DDesc desc);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int CreateTexture2DFn(IntPtr self, ref Texture2DDesc desc, IntPtr initialData, out IntPtr texture);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void GetImmediateContextFn(IntPtr self, out IntPtr context);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void CopyResourceFn(IntPtr self, IntPtr destination, IntPtr source);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void FlushFn(IntPtr self);
        [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int GetSharedHandleFn(IntPtr self, out IntPtr handle);

        IntPtr context, texture;
        CopyResourceFn copy;
        FlushFn flush;

        public IntPtr Source { get; private set; }  // the Unity texture this copies
        public ulong Handle { get; private set; }
        public uint Width { get; private set; }
        public uint Height { get; private set; }
        public uint Format { get; private set; }    // DXGI_FORMAT, typed

        // a_source: Texture.GetNativeTexturePtr() of a single-sample 2D texture (ID3D11Texture2D).
        public static SharedTexture Create(IntPtr a_source, Action<string> log)
        {
            IntPtr device = IntPtr.Zero;
            var t = new SharedTexture { Source = a_source };
            try
            {
                Call<GetDeviceFn>(a_source, SlotChildGetDevice)(a_source, out device);
                Texture2DDesc desc;
                Call<GetDescFn>(a_source, SlotTextureGetDesc)(a_source, out desc);
                desc.Format = Typed(desc.Format);
                desc.MipLevels = 1;
                desc.ArraySize = 1;
                desc.SampleCount = 1;
                desc.SampleQuality = 0;
                desc.Usage = 0;  // default
                desc.BindFlags = BindShaderResource | BindRenderTarget;
                desc.CpuAccessFlags = 0;
                desc.MiscFlags = MiscShared;
                int hr = Call<CreateTexture2DFn>(device, SlotDeviceCreateTexture2D)(device, ref desc, IntPtr.Zero, out t.texture);
                if (hr < 0 || t.texture == IntPtr.Zero) throw new Exception($"CreateTexture2D {desc.Width}x{desc.Height} format {desc.Format} failed 0x{hr:X8}");

                IntPtr resource;
                hr = Call<QueryInterfaceFn>(t.texture, SlotQueryInterface)(t.texture, ref IidDxgiResource, out resource);
                if (hr < 0) throw new Exception($"QueryInterface(IDXGIResource) failed 0x{hr:X8}");
                IntPtr handle;
                hr = Call<GetSharedHandleFn>(resource, SlotResourceGetSharedHandle)(resource, out handle);
                Release(resource);
                if (hr < 0 || handle == IntPtr.Zero) throw new Exception($"GetSharedHandle failed 0x{hr:X8}");

                Call<GetImmediateContextFn>(device, SlotDeviceGetImmediateContext)(device, out t.context);
                t.copy = Call<CopyResourceFn>(t.context, SlotContextCopyResource);
                t.flush = Call<FlushFn>(t.context, SlotContextFlush);
                t.Handle = (ulong)handle.ToInt64();
                t.Width = desc.Width;
                t.Height = desc.Height;
                t.Format = desc.Format;
                return t;
            }
            catch (Exception e)
            {
                log("hand layer: shared texture FAILED: " + e.Message);
                t.Dispose();
                return null;
            }
            finally
            {
                if (device != IntPtr.Zero) Release(device);
            }
        }

        // Copies the source in on the GPU and submits it, so the other process sees it.
        public void Update()
        {
            copy(context, texture, Source);
            flush(context);
        }

        public void Dispose()
        {
            if (texture != IntPtr.Zero) { Release(texture); texture = IntPtr.Zero; }
            if (context != IntPtr.Zero) { Release(context); context = IntPtr.Zero; }
            Handle = 0;
        }

        // A typeless format can't be opened for sampling without naming a view format, so the copy
        // gets the plain one (CopyResource accepts any format of the same family).
        static uint Typed(uint format)
        {
            switch (format)
            {
                case 27: return 28;  // R8G8B8A8_TYPELESS -> R8G8B8A8_UNORM
                case 90: return 87;  // B8G8R8A8_TYPELESS -> B8G8R8A8_UNORM
                case 23: return 24;  // R10G10B10A2_TYPELESS -> R10G10B10A2_UNORM
                case 9: return 10;   // R16G16B16A16_TYPELESS -> R16G16B16A16_FLOAT
                default: return format;
            }
        }

        static void Release(IntPtr obj) => Call<ReleaseFn>(obj, SlotRelease)(obj);

        static T Call<T>(IntPtr obj, int slot) where T : class
        {
            IntPtr vtable = Marshal.ReadIntPtr(obj);
            IntPtr fn = Marshal.ReadIntPtr(vtable, slot * IntPtr.Size);
            return (T)(object)Marshal.GetDelegateForFunctionPointer(fn, typeof(T));
        }
    }
}
