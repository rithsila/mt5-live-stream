using System.Diagnostics;
using System.Runtime.InteropServices;
using SpikeLogic;

namespace CaptureSpike;

public sealed class CaptureOutcome
{
    public bool Ok;
    public string? Error;
    public BgraFrame? Frame;   // cropped to the chart rect (or full chartshot image)
    public long CaptureMs;
    public bool SourceFresh = true; // chartshot: was the EA log line < 3 s old
    public bool NewFrame = true;   // true for gdi/printwindow, real value for wgc/chartshot
}

/// <summary>One capture method. Instances are reused for a whole run.</summary>
internal interface IFrameSource : IDisposable
{
    string Name { get; }
    /// <summary>Set at init when the method cannot run at all.</summary>
    string? UnsupportedReason { get; }
    CaptureOutcome Capture(Rect chartScreenRect, Rect windowScreenRect);
}

internal abstract class SourceBase : IFrameSource
{
    public abstract string Name { get; }
    public string? UnsupportedReason { get; protected set; }
    protected readonly IntPtr Hwnd;

    /// <summary>False for chartshot — it produces the chart image itself, so it works even with no chart rect.</summary>
    protected virtual bool NeedsChartRect => true;

    protected SourceBase(IntPtr hwnd) => Hwnd = hwnd;

    public virtual CaptureOutcome Capture(Rect chartRect, Rect windowRect)
    {
        var sw = Stopwatch.StartNew();
        var o = new CaptureOutcome { SourceFresh = GetFreshness(), NewFrame = true };
        try
        {
            if (UnsupportedReason is not null)
            {
                o.Error = $"unsupported: {UnsupportedReason}";
            }
            else if (NeedsChartRect && (chartRect.Width <= 0 || chartRect.Height <= 0))
            {
                o.Error = "no-chart";
            }
            else
            {
                o.Frame = Grab(chartRect, windowRect);
                o.Ok = o.Frame is { Width: > 0, Height: > 0 };
                if (!o.Ok) o.Error = "empty frame";
            }
        }
        catch (Exception ex)
        {
            o.Error = ex.Message;
        }
        o.SourceFresh = GetFreshness();
        o.CaptureMs = sw.ElapsedMilliseconds;
        return o;
    }

    protected void SetNewFrame(CaptureOutcome o, bool value) => o.NewFrame = value;
    protected virtual bool GetFreshness() => true;
    protected abstract BgraFrame Grab(Rect chartRect, Rect windowRect);
    public virtual void Dispose() { }
}

/// <summary>BitBlt from the screen DC at the chart rect.</summary>
internal sealed class GdiScreenSource : SourceBase
{
    public override string Name => "gdi-screen";
    public GdiScreenSource(IntPtr hwnd) : base(hwnd) { }

    protected override BgraFrame Grab(Rect chartRect, Rect windowRect)
    {
        using var bmp = BitmapUtils.IntoNewBitmap(chartRect.Width, chartRect.Height, hdc =>
        {
            var screen = Win32.GetDC(IntPtr.Zero);
            try
            {
                bool ok = Win32.BitBlt(hdc, 0, 0, chartRect.Width, chartRect.Height,
                    screen, chartRect.X, chartRect.Y, Win32.SRCCOPY | Win32.CAPTUREBLT);
                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException($"BitBlt failed (Win32 error {err})");
                }
            }
            finally { Win32.ReleaseDC(IntPtr.Zero, screen); }
        });
        return BitmapUtils.BitmapToFrame(bmp);
    }
}

/// <summary>BitBlt from the top-level window DC, cropped to the chart rect.</summary>
internal sealed class GdiWindowSource : SourceBase
{
    public override string Name => "gdi-window";
    public GdiWindowSource(IntPtr hwnd) : base(hwnd) { }

    protected override BgraFrame Grab(Rect chartRect, Rect windowRect)
    {
        using var bmp = BitmapUtils.IntoNewBitmap(chartRect.Width, chartRect.Height, hdc =>
        {
            var win = Win32.GetWindowDC(Hwnd);
            try
            {
                bool ok = Win32.BitBlt(hdc, 0, 0, chartRect.Width, chartRect.Height,
                    win, chartRect.X - windowRect.X, chartRect.Y - windowRect.Y,
                    Win32.SRCCOPY | Win32.CAPTUREBLT);
                if (!ok)
                {
                    int err = Marshal.GetLastWin32Error();
                    throw new InvalidOperationException($"BitBlt failed (Win32 error {err})");
                }
            }
            finally { Win32.ReleaseDC(Hwnd, win); }
        });
        return BitmapUtils.BitmapToFrame(bmp);
    }
}

/// <summary>PrintWindow(PW_RENDERFULLCONTENT) of the top-level window, cropped.</summary>
internal sealed class PrintWindowSource : SourceBase
{
    public override string Name => "printwindow";
    public PrintWindowSource(IntPtr hwnd) : base(hwnd) { }

    protected override BgraFrame Grab(Rect chartRect, Rect windowRect)
    {
        int w = Math.Max(1, windowRect.Width);
        int h = Math.Max(1, windowRect.Height);
        using var bmp = BitmapUtils.IntoNewBitmap(w, h, hdc =>
        {
            if (!Win32.PrintWindow(Hwnd, hdc, Win32.PW_RENDERFULLCONTENT))
                throw new InvalidOperationException($"PrintWindow failed (err {System.Runtime.InteropServices.Marshal.GetLastWin32Error()})");
        });
        var full = BitmapUtils.BitmapToFrame(bmp);
        var crop = new Rect(chartRect.X - windowRect.X, chartRect.Y - windowRect.Y, chartRect.Width, chartRect.Height);
        return ImageOps.Crop(full, crop);
    }
}
