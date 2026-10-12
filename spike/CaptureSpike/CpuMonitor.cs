using System.Diagnostics;
using SpikeLogic;

namespace CaptureSpike;

public sealed record CpuSample(
    double MachineBusyPct,
    double SpikePct,
    double FfmpegPct,
    double Mt5Pct);

/// <summary>
/// Samples CPU once per second: machine total via GetSystemTimes deltas,
/// per-process via TotalProcessorTime deltas (spike, ffmpeg, all terminal64).
/// </summary>
internal sealed class CpuMonitor
{
    private readonly int _cores = Environment.ProcessorCount;
    private ulong _idle, _kernel, _user;
    private DateTime _t;
    private TimeSpan _spikeCpu, _ffmpegCpu, _mt5Cpu;
    private Process? _ffmpeg;
    private bool _first = true;

    public void SetFfmpeg(Process? p) => _ffmpeg = p;

    public CpuSample Sample()
    {
        var now = DateTime.UtcNow;

        ulong idle = 0, kernel = 0, user = 0;
        try
        {
            if (Win32.GetSystemTimes(out var i, out var k, out var u))
            {
                idle = i.ToUInt64();
                kernel = k.ToUInt64();
                user = u.ToUInt64();
            }
        }
        catch { }

        var spike = SafeCpu(Process.GetCurrentProcess());
        var ff = SafeCpu(_ffmpeg);
        var mt5 = TimeSpan.Zero;
        foreach (var p in Process.GetProcessesByName("terminal64"))
        {
            try { mt5 += p.TotalProcessorTime; } catch { }
            finally { p.Dispose(); }
        }

        var wall = now - _t;
        var sample = new CpuSample(
            _first ? 0 : CpuMath.MachineBusyPercent(idle - _idle, kernel - _kernel, user - _user),
            _first ? 0 : CpuMath.ProcessCpuPercent(spike - _spikeCpu, wall, _cores),
            _first || _ffmpeg is null ? 0 : CpuMath.ProcessCpuPercent(ff - _ffmpegCpu, wall, _cores),
            _first ? 0 : CpuMath.ProcessCpuPercent(mt5 - _mt5Cpu, wall, _cores));

        _idle = idle; _kernel = kernel; _user = user; _t = now;
        _spikeCpu = spike; _ffmpegCpu = ff; _mt5Cpu = mt5;
        _first = false;
        return sample;
    }

    private static TimeSpan SafeCpu(Process? p)
    {
        try { return p?.TotalProcessorTime ?? TimeSpan.Zero; }
        catch { return TimeSpan.Zero; }
    }
}
