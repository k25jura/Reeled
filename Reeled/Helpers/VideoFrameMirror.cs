using System;
using System.Runtime.InteropServices;
using Microsoft.UI.Composition;
using Microsoft.Graphics.DirectX;

namespace Reeled.Helpers;

/// <summary>
/// Mirrors the video into a small composition drawing surface for <see cref="VideoGlass"/>.
/// Composition effects can't read a swap chain in-process, so each tick copies the presented frame on the
/// GPU (same D3D11 device LibVLCSharp created), shrinks it with GenerateMips and copies one small mip level
/// into a drawing surface, which effects can blur. All GPU work stays on the GPU; nothing is read back.
/// </summary>
internal sealed unsafe class VideoFrameMirror : IDisposable
{
    // DXGI_FORMAT values this path can copy without conversion
    private const uint DXGI_FORMAT_R8G8B8A8_UNORM = 28;
    private const uint DXGI_FORMAT_B8G8R8A8_UNORM = 87;

    private const uint D3D11_BIND_SHADER_RESOURCE = 0x8;
    private const uint D3D11_BIND_RENDER_TARGET = 0x20;
    private const uint D3D11_RESOURCE_MISC_GENERATE_MIPS = 0x1;

    private static readonly Guid IID_ID3D11Texture2D = new("6F15AAF2-D208-4E89-9AB4-489535D34F9C");
    private static readonly Guid IID_ID3D10Multithread = new("9B7E4E00-342C-4106-A19F-4F2704F689F0");
    private static readonly Guid IID_ICompositorInterop = new("FAB19398-6D19-4D8A-B752-8F096C396069");
    private static readonly Guid IID_ICompositionDrawingSurfaceInterop = new("2D6355C2-AD57-4EAE-92E4-4C3EFF65D578");

    [StructLayout(LayoutKind.Sequential)]
    private struct D3D11_TEXTURE2D_DESC
    {
        public uint Width, Height, MipLevels, ArraySize, Format, SampleCount, SampleQuality, Usage, BindFlags, CPUAccessFlags, MiscFlags;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    private readonly IntPtr _swapChain;  // IDXGISwapChain (AddRef'd)
    private readonly IntPtr _device;     // ID3D11Device
    private readonly IntPtr _context;    // ID3D11DeviceContext
    private readonly IntPtr _multithread; // ID3D10Multithread, may be zero
    private IntPtr _mipTexture;
    private IntPtr _mipView;
    private D3D11_TEXTURE2D_DESC _mipDesc;
    private uint _mipLevel;
    private CompositionGraphicsDevice? _graphicsDevice;
    private bool _disposed;

    public CompositionDrawingSurface? Surface { get; private set; }

    private VideoFrameMirror(IntPtr swapChain, IntPtr device, IntPtr context, IntPtr multithread)
    {
        _swapChain = swapChain;
        _device = device;
        _context = context;
        _multithread = multithread;
    }

    private static void* Slot(IntPtr com, int index) => (*(void***)com)[index];

    private static void Release(ref IntPtr com)
    {
        if (com != IntPtr.Zero)
        {
            Marshal.Release(com);
            com = IntPtr.Zero;
        }
    }

    public static VideoFrameMirror Create(IntPtr swapChain, Compositor compositor)
    {
        Marshal.AddRef(swapChain);
        IntPtr device = IntPtr.Zero, context = IntPtr.Zero, multithread = IntPtr.Zero;
        try
        {
            // IDXGIDeviceSubObject::GetDevice (7)
            Guid deviceIid = new("db6f6ddb-ac77-4e88-8253-819df9bbf140"); // ID3D11Device
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, Guid*, IntPtr*, int>)Slot(swapChain, 7))(swapChain, &deviceIid, &device);
            Marshal.ThrowExceptionForHR(hr);

            // ID3D11Device::GetImmediateContext (40)
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr*, void>)Slot(device, 40))(device, &context);
            if (context == IntPtr.Zero) throw new InvalidOperationException("No immediate context");

            // libvlc renders on its own thread with this same context: make every call atomic, and
            // Enter/Leave around our short sequence
            Guid mtIid = IID_ID3D10Multithread;
            if (Marshal.QueryInterface(context, ref mtIid, out multithread) == 0 && multithread != IntPtr.Zero)
            {
                // ID3D10Multithread::SetMultithreadProtected (5)
                ((delegate* unmanaged[Stdcall]<IntPtr, int, int>)Slot(multithread, 5))(multithread, 1);
            }

            var mirror = new VideoFrameMirror(swapChain, device, context, multithread);
            mirror.CreateGraphicsDevice(compositor);
            return mirror;
        }
        catch
        {
            Release(ref multithread);
            Release(ref context);
            Release(ref device);
            Marshal.Release(swapChain);
            throw;
        }
    }

    private void CreateGraphicsDevice(Compositor compositor)
    {
        IntPtr compositorPtr = ((WinRT.IWinRTObject)compositor).NativeObject.ThisPtr;
        Guid iid = IID_ICompositorInterop;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(compositorPtr, ref iid, out IntPtr interop));
        try
        {
            IntPtr graphicsDevicePtr;
            // ICompositorInterop::CreateGraphicsDevice (3)
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr*, int>)Slot(interop, 3))(interop, _device, &graphicsDevicePtr);
            Marshal.ThrowExceptionForHR(hr);
            try
            {
                _graphicsDevice = WinRT.MarshalInspectable<CompositionGraphicsDevice>.FromAbi(graphicsDevicePtr);
            }
            finally
            {
                Marshal.Release(graphicsDevicePtr);
            }
        }
        finally
        {
            Marshal.Release(interop);
        }
    }

    /// <summary>Copies the latest presented frame into <see cref="Surface"/>. Returns false when nothing was copied.</summary>
    public bool Update()
    {
        if (_disposed || _graphicsDevice == null) return false;

        IntPtr backBuffer = IntPtr.Zero;
        bool entered = false;
        try
        {
            if (_multithread != IntPtr.Zero)
            {
                ((delegate* unmanaged[Stdcall]<IntPtr, void>)Slot(_multithread, 3))(_multithread); // Enter
                entered = true;
            }

            // IDXGISwapChain::GetBuffer (9). With a flip-model chain buffer 0 holds the previous frame,
            // which is invisible behind a heavy blur
            Guid texIid = IID_ID3D11Texture2D;
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, uint, Guid*, IntPtr*, int>)Slot(_swapChain, 9))(_swapChain, 0, &texIid, &backBuffer);
            if (hr < 0 || backBuffer == IntPtr.Zero) return false;

            D3D11_TEXTURE2D_DESC desc;
            ((delegate* unmanaged[Stdcall]<IntPtr, D3D11_TEXTURE2D_DESC*, void>)Slot(backBuffer, 10))(backBuffer, &desc); // GetDesc
            if (desc.Width < 16 || desc.Height < 16 || desc.SampleCount != 1) return false;
            if (desc.Format != DXGI_FORMAT_B8G8R8A8_UNORM && desc.Format != DXGI_FORMAT_R8G8B8A8_UNORM) return false;

            if (!EnsureMipTexture(desc)) return false;

            // CopySubresourceRegion (46) full frame into mip 0, then GenerateMips (54)
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, uint, IntPtr, uint, IntPtr, void>)Slot(_context, 46))(
                _context, _mipTexture, 0, 0, 0, 0, backBuffer, 0, IntPtr.Zero);
            ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, void>)Slot(_context, 54))(_context, _mipView);

            return CopyMipToSurface();
        }
        catch (Exception ex)
        {
            Log.Error("VideoFrameMirror.Update", ex);
            return false;
        }
        finally
        {
            Release(ref backBuffer);
            if (entered)
            {
                ((delegate* unmanaged[Stdcall]<IntPtr, void>)Slot(_multithread, 4))(_multithread); // Leave
            }
        }
    }

    private bool EnsureMipTexture(D3D11_TEXTURE2D_DESC source)
    {
        if (_mipTexture != IntPtr.Zero && _mipDesc.Width == source.Width && _mipDesc.Height == source.Height && _mipDesc.Format == source.Format)
        {
            return true;
        }

        Release(ref _mipView);
        Release(ref _mipTexture);

        // Shrink to roughly 200 px wide: the glass only ever shows a heavily blurred copy
        uint level = 1;
        while ((source.Width >> (int)(level + 1)) >= 160 && level < 6) level++;

        var desc = new D3D11_TEXTURE2D_DESC
        {
            Width = source.Width,
            Height = source.Height,
            MipLevels = level + 1,
            ArraySize = 1,
            Format = source.Format,
            SampleCount = 1,
            SampleQuality = 0,
            Usage = 0, // DEFAULT
            BindFlags = D3D11_BIND_SHADER_RESOURCE | D3D11_BIND_RENDER_TARGET,
            CPUAccessFlags = 0,
            MiscFlags = D3D11_RESOURCE_MISC_GENERATE_MIPS
        };

        IntPtr texture;
        int hr = ((delegate* unmanaged[Stdcall]<IntPtr, D3D11_TEXTURE2D_DESC*, IntPtr, IntPtr*, int>)Slot(_device, 5))(_device, &desc, IntPtr.Zero, &texture); // CreateTexture2D
        if (hr < 0) return false;

        IntPtr view;
        hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, IntPtr, IntPtr*, int>)Slot(_device, 7))(_device, texture, IntPtr.Zero, &view); // CreateShaderResourceView
        if (hr < 0)
        {
            Marshal.Release(texture);
            return false;
        }

        _mipTexture = texture;
        _mipView = view;
        _mipDesc = desc;
        _mipLevel = level;

        int w = (int)Math.Max(1, source.Width >> (int)level);
        int h = (int)Math.Max(1, source.Height >> (int)level);
        var format = source.Format == DXGI_FORMAT_R8G8B8A8_UNORM ? DirectXPixelFormat.R8G8B8A8UIntNormalized : DirectXPixelFormat.B8G8R8A8UIntNormalized;
        if (Surface == null)
        {
            // Video alpha is meaningless, so ignore it rather than risk a see-through glass
            Surface = _graphicsDevice!.CreateDrawingSurface(new Windows.Foundation.Size(w, h), format, DirectXAlphaMode.Ignore);
        }
        else
        {
            Surface.Resize(new Windows.Graphics.SizeInt32 { Width = w, Height = h });
        }
        return true;
    }

    private bool CopyMipToSurface()
    {
        if (Surface == null) return false;
        IntPtr surfacePtr = ((WinRT.IWinRTObject)Surface).NativeObject.ThisPtr;
        Guid iid = IID_ICompositionDrawingSurfaceInterop;
        if (Marshal.QueryInterface(surfacePtr, ref iid, out IntPtr interop) != 0) return false;

        IntPtr target = IntPtr.Zero;
        try
        {
            Guid texIid = IID_ID3D11Texture2D;
            POINT offset;
            // ICompositionDrawingSurfaceInterop::BeginDraw (3)
            int hr = ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, Guid*, IntPtr*, POINT*, int>)Slot(interop, 3))(interop, IntPtr.Zero, &texIid, &target, &offset);
            if (hr < 0 || target == IntPtr.Zero) return false;
            try
            {
                ((delegate* unmanaged[Stdcall]<IntPtr, IntPtr, uint, uint, uint, uint, IntPtr, uint, IntPtr, void>)Slot(_context, 46))(
                    _context, target, 0, (uint)offset.X, (uint)offset.Y, 0, _mipTexture, _mipLevel, IntPtr.Zero);
            }
            finally
            {
                ((delegate* unmanaged[Stdcall]<IntPtr, int>)Slot(interop, 4))(interop); // EndDraw
            }
            return true;
        }
        finally
        {
            Release(ref target);
            Marshal.Release(interop);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        Surface?.Dispose();
        Surface = null;
        _graphicsDevice?.Dispose();
        _graphicsDevice = null;
        Release(ref _mipView);
        Release(ref _mipTexture);
        IntPtr mt = _multithread, ctx = _context, dev = _device, sc = _swapChain;
        Release(ref mt);
        Release(ref ctx);
        Release(ref dev);
        Release(ref sc);
    }
}
