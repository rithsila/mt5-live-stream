using System.Diagnostics;
using SpikeLogic;

namespace CaptureSpike;

/// <summary>
/// An ffmpeg child process: raw BGRA frames go to stdin, "-progress pipe:2"
/// key=value blocks come back on stderr and are parsed continuously.
/// The stream key is in the arguments — never log psi arguments directly.
/// </summary>
internal sealed class FfmpegPipe : IDisposable
{
    private readonly Process _proc;
    private readonly List<string> _progressLines = new();
    private readonly object _lock = new();
    private FfmpegProgress _latest = new(null, null, null, null, null, false);
    private readonly List<string> _stderrTail = new();
    private readonly string[] _secrets;

    public FfmpegProgress Latest { get { lock (_lock) return _latest; } }
    public Process Process => _proc;

    /// <summary>Display command line with the key redacted — safe to print/log.</summary>
    public string DisplayCommand { get; }

    /// <param name="secrets">Strings that must never reach logs (the stream key). Redacted in every stderr line.</param>
    public FfmpegPipe(string ffmpegPath, FfmpegJob job, params string[] secrets)
    {
        _secrets = secrets.Where(s => !string.IsNullOrEmpty(s)).ToArray();
        DisplayCommand = $"\"{ffmpegPath}\" " + FfmpegArgs.BuildDisplay(job);

        var psi = new ProcessStartInfo(ffmpegPath)
        {
            RedirectStandardInput = true,
            RedirectStandardError = true,
            RedirectStandardOutput = false,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var a in FfmpegArgs.Build(job))
            psi.ArgumentList.Add(a);

        _proc = Process.Start(psi) ?? throw new InvalidOperationException("could not start ffmpeg");
        Task.Run(ReadStderr);
    }

    private void ReadStderr()
    {
        try
        {
            var block = new List<string>();
            string? raw;
            while ((raw = _proc.StandardError.ReadLine()) is not null)
            {
                var line = Redact(raw);
                lock (_lock)
                {
                    _stderrTail.Add(line);
                    if (_stderrTail.Count > 40)
                        _stderrTail.RemoveAt(0);
                }
                block.Add(line);
                if (line.StartsWith("progress=", StringComparison.Ordinal))
                {
                    var parsed = FfmpegProgress.Parse(block);
                    lock (_lock)
                    {
                        _latest = parsed;
                        _progressLines.Add(string.Join(" ", block.Where(l =>
                            l.StartsWith("frame=") || l.StartsWith("fps=") ||
                            l.StartsWith("bitrate=") || l.StartsWith("speed=") ||
                            l.StartsWith("progress="))));
                        if (_progressLines.Count > 200)
                            _progressLines.RemoveAt(0);
                    }
                    block.Clear();
                }
            }
        }
        catch { /* ffmpeg exiting kills the pipe */ }
    }

    private string Redact(string line)
    {
        foreach (var s in _secrets)
            line = line.Replace(s, "***", StringComparison.Ordinal);
        return line;
    }

    /// <summary>Progress blocks as one-line summaries, for the report.</summary>
    public IReadOnlyList<string> ProgressLog { get { lock (_lock) return _progressLines.ToList(); } }
    public IReadOnlyList<string> StderrTail { get { lock (_lock) return _stderrTail.ToList(); } }

    public void WriteFrame(BgraFrame frame)
        => WriteFrame(frame.Pixels);

    public void WriteFrame(byte[] pixels)
    {
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(5));
        _proc.StandardInput.BaseStream.WriteAsync(pixels.AsMemory(), timeout.Token).AsTask().GetAwaiter().GetResult();
    }

    /// <summary>Close stdin, wait for exit, return exit code. Returns -1 on timeout.</summary>
    public async Task<int> FinishAsync(int timeoutMs = 15000)
    {
        try { _proc.StandardInput.BaseStream.Flush(); } catch { }
        try { _proc.StandardInput.Close(); } catch { }
        using var cts = new CancellationTokenSource(timeoutMs);
        try
        {
            await _proc.WaitForExitAsync(cts.Token);
            return _proc.ExitCode;
        }
        catch (OperationCanceledException)
        {
            try { _proc.Kill(true); } catch { }
            return -1;
        }
    }

    public void Dispose()
    {
        try { if (!_proc.HasExited) _proc.Kill(true); } catch { }
        _proc.Dispose();
    }
}
