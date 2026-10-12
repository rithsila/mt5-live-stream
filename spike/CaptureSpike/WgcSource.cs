using System.Runtime.InteropServices;
using SpikeLogic;
using Windows.Graphics.Capture;
using Windows.Graphics.DirectX;
using Windows.Graphics.DirectX.Direct3D11;
using Windows.Graphics.Imaging;

namespace CaptureSpike;

[ComImport]
[Guid("3628E81B-3CAC-4C60-B7F4-23CE0E0C3356")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IGraphicsCaptureItemInterop
{
    IntPtr CreateForWindow([In] IntPtr window, [In] ref Guid iid);

    IntPtr CreateForMonitor([In] IntPtr monitor, [In] ref Guid iid);
}

[ComImport]
[Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal unsafe interface IMemoryBufferByteAccess
{
    void GetBuffer(byte** buffer, uint* capacity);
}

[ComImport]
[Guid("4e2d4781-b344-4b79-8768-92da58034f5c")]
[InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
internal interface IDirect3DDeviceInterop
{
    IntPtr GetWrappedDevice(ref Guid iid);
}

/// <summary>Windows Graphics Capture of the top-level MT5 window.</summary>
internal sealed class WgcSource : SourceBase
{
    public override string Name => "wgc";

    private IDirect3DDevice? _device;
    private Direct3D11CaptureFramePool? _pool;
    private GraphicsCaptureSession? _session;
    private GraphicsCaptureItem? _item;
    private int _lastW, _lastH;
    private BgraFrame? _cachedFrame;
    private DateTime _lastNewFrameTime = DateTime.MinValue;
    private bool _newFrameThisCall;

    public WgcSource(IntPtr hwnd) : base(hwnd)
    {
        try { Init(); }
        catch (Exception ex) { UnsupportedReason = ex.Message; }
    }

    private void Init()
    {
        if (!GraphicsCaptureSession.IsSupported())
        {
            UnsupportedReason = "GraphicsCaptureSession.IsSupported() = false";
            return;
        }

        _item = CreateItemForWindow(Hwnd);
        _device = CreateDevice();
        _lastW = _item.Size.Width;
        _lastH = _item.Size.Height;
        StartPool();
    }

    private void StartPool()
    {
        _session?.Dispose();
        _pool?.Dispose();

        _pool = Direct3D11CaptureFramePool.CreateFreeThreaded(
            _device!,
            DirectXPixelFormat.B8G8R8A8UIntNormalized,
            2,
            new Windows.Graphics.SizeInt32 { Width = _lastW, Height = _lastH });
        _session = _pool.CreateCaptureSession(_item!);
        if (OperatingSystem.IsWindowsVersionAtLeast(22000))
            try { _session.IsBorderRequired = false; } catch { /* optional permission */ }
        _session.StartCapture();
    }

    public override CaptureOutcome Capture(Rect chartRect, Rect windowRect)
    {
        _newFrameThisCall = false;
        var outcome = base.Capture(chartRect, windowRect);
        SetNewFrame(outcome, outcome.Ok && _newFrameThisCall);
        return outcome;
    }

    protected override unsafe BgraFrame Grab(Rect chartRect, Rect windowRect)
    {
        // Drain the pool; keep the newest frame.
        Direct3D11CaptureFrame? latest = null;
        while (_pool!.TryGetNextFrame() is { } f)
        {
            latest?.Dispose();
            latest = f;
        }

        if (latest is null)
        {
            if (_cachedFrame is null)
                throw new InvalidOperationException("no frame yet");
            // Return cached frame if available; caller checks freshness via SourceFresh.
            LastFresh = (DateTime.UtcNow - _lastNewFrameTime) < TimeSpan.FromSeconds(3);
            return _cachedFrame;
        }

        using (latest)
        {
            // If the window was resized the frame size changes; rebuild the pool.
            var size = latest.ContentSize;
            if (size.Width != _lastW || size.Height != _lastH)
            {
                _lastW = size.Width;
                _lastH = size.Height;
                StartPool();
                throw new InvalidOperationException("window resized, pool rebuilt");
            }

            using var bmp = SoftwareBitmap.CreateCopyFromSurfaceAsync(latest.Surface)
                .AsTask().GetAwaiter().GetResult();

            var full = CopyOut(bmp);
            var crop = new Rect(chartRect.X - windowRect.X, chartRect.Y - windowRect.Y,
                chartRect.Width, chartRect.Height);
            _cachedFrame = ImageOps.Crop(full, crop);
            _lastNewFrameTime = DateTime.UtcNow;
            _newFrameThisCall = true;
            LastFresh = true;
            return _cachedFrame;
        }
    }

    protected override bool GetFreshness() => LastFresh;
    private bool LastFresh;

    private static unsafe BgraFrame CopyOut(SoftwareBitmap bmp)
    {
        using var buffer = bmp.LockBuffer(BitmapBufferAccessMode.Read);
        using var reference = buffer.CreateReference();
        var plane = buffer.GetPlaneDescription(0);

        // QI the IMemoryBufferReference for IMemoryBufferByteAccess on its raw
        // IUnknown — casting the CsWinRT object directly does not work.
        var iidByteAccess = new Guid("5B0D3235-4DBA-4D44-865E-8F1D0E4FD04D");
        var thisPtr = ((WinRT.IWinRTObject)reference).NativeObject.ThisPtr;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(thisPtr, in iidByteAccess, out var accessPtr));
        byte* data;
        uint capacity;
        try
        {
            var access = (IMemoryBufferByteAccess)Marshal.GetObjectForIUnknown(accessPtr);
            access.GetBuffer(&data, &capacity);
        }
        finally { Marshal.Release(accessPtr); }

        var pixels = new byte[plane.Width * plane.Height * 4];
        for (int y = 0; y < plane.Height; y++)
        {
            Marshal.Copy((IntPtr)(data + y * plane.Stride + plane.StartIndex),
                pixels, y * plane.Width * 4, plane.Width * 4);
        }
        return new BgraFrame { Pixels = pixels, Width = plane.Width, Height = plane.Height };
    }

    private static GraphicsCaptureItem CreateItemForWindow(IntPtr hwnd)
    {
        var factoryPtr = GetActivationFactory("Windows.Graphics.Capture.GraphicsCaptureItem");
        var interop = (IGraphicsCaptureItemInterop)Marshal.GetObjectForIUnknown(factoryPtr);
        var iid = Win32.IID_IGraphicsCaptureItem;
        var itemPtr = interop.CreateForWindow(hwnd, ref iid);
        Marshal.Release(factoryPtr);

        // CsWinRT projections are not COM RCWs. A direct RCW cast fails at runtime.
        try { return WinRT.MarshalInspectable<GraphicsCaptureItem>.FromAbi(itemPtr); }
        finally { Marshal.Release(itemPtr); }
    }

    private static IntPtr GetActivationFactory(string className)
    {
        Win32.CheckHr(Win32.WindowsCreateString(className, (uint)className.Length, out var hs),
            "WindowsCreateString");
        try
        {
            var iid = Win32.IID_IActivationFactory;
            Win32.CheckHr(Win32.RoGetActivationFactory(hs, in iid, out var factory),
                $"RoGetActivationFactory({className})");
            return factory;
        }
        finally { Win32.WindowsDeleteString(hs); }
    }

    private static IDirect3DDevice CreateDevice()
    {
        IntPtr d3d = IntPtr.Zero;
        int hr = Win32.D3D11CreateDevice(IntPtr.Zero, Win32.D3D_DRIVER_TYPE_HARDWARE, IntPtr.Zero,
            Win32.D3D11_CREATE_DEVICE_BGRA_SUPPORT, null, 0, Win32.D3D11_SDK_VERSION,
            out d3d, out _, out var context);
        if (context != IntPtr.Zero) Marshal.Release(context);
        if (hr < 0 || d3d == IntPtr.Zero)
        {
            // VPS often has no GPU — fall back to the WARP software device.
            Win32.CheckHr(Win32.D3D11CreateDevice(IntPtr.Zero, Win32.D3D_DRIVER_TYPE_WARP, IntPtr.Zero,
                Win32.D3D11_CREATE_DEVICE_BGRA_SUPPORT, null, 0, Win32.D3D11_SDK_VERSION,
                out d3d, out _, out context), "D3D11CreateDevice(WARP)");
            if (context != IntPtr.Zero) Marshal.Release(context);
        }

        var iidDxgi = Win32.IID_IDXGIDevice;
        Marshal.ThrowExceptionForHR(Marshal.QueryInterface(d3d, in iidDxgi, out var dxgi));
        Marshal.Release(d3d);

        Win32.CheckHr(Win32.CreateDirect3D11DeviceFromDXGIDevice(dxgi, out var devicePtr),
            "CreateDirect3D11DeviceFromDXGIDevice");
        Marshal.Release(dxgi);

        try { return WinRT.MarshalInterface<IDirect3DDevice>.FromAbi(devicePtr); }
        finally { Marshal.Release(devicePtr); }
    }

    public override void Dispose()
    {
        try { _session?.Dispose(); } catch { }
        try { _pool?.Dispose(); } catch { }
        _item = null; // released by GC/CsWinRT
    }
}
