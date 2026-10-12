using SpikeLogic;

namespace CaptureSpike;

internal static class Program
{
    private static int Main(string[] args)
    {
        try { Win32.SetProcessDpiAwarenessContext(Win32.DPI_AWARENESS_CONTEXT_PER_MONITOR_AWARE_V2); }
        catch { /* manifest already sets PerMonitorV2 */ }

        if (args.Length == 0 || args[0] is "-h" or "--help" or "help")
        {
            Usage();
            return args.Length == 0 ? 1 : 0;
        }

        var command = args[0].ToLowerInvariant();
        var cli = Cli.Parse(args[1..]);
        var commandLine = "CaptureSpike " + string.Join(' ', args);

        try
        {
            return command switch
            {
                "info" => Commands.Info(cli, commandLine),
                "capture" => Commands.Capture(cli, commandLine),
                "bench" => Commands.Bench(cli, commandLine),
                "stream-test" => Commands.StreamTest(cli, commandLine),
                _ => Unknown(command),
            };
        }
        catch (Exception ex)
        {
            // Never crash without a report: write what we have.
            Console.Error.WriteLine($"FATAL: {ex.Message}");
            try
            {
                var label = cli.Get("label") ?? command;
                var report = new SpikeReport { Label = label, Command = commandLine };
                report.Notes.Add($"run failed: {ex.Message}");
                var dir = Commands.ReportDir(cli, label);
                report.WriteTo(dir);
                Console.Error.WriteLine($"partial report written to {dir}");
            }
            catch { }
            return 2;
        }
    }

    private static int Unknown(string command)
    {
        Console.Error.WriteLine($"unknown command: {command}");
        Usage();
        return 1;
    }

    private static void Usage()
    {
        Console.WriteLine("""
        CaptureSpike — Phase 0 VPS spike tool for the live-trade streamer.

        Commands:
          info
              System + session info and the MT5 window tree -> report.

          capture --label <name> [--duration 180] [--delay 20]
                  [--methods gdi-screen,gdi-window,printwindow,wgc,chartshot]
                  [--chart auto|<index>]
              Per-second capture test with every method. Use --delay to give
              yourself time to close RDP / lock the desktop before it starts.

          bench --method <m> [--seconds 60] [--delay 20] [--ffmpeg <path>]
              Real-time encode benchmark over the 8-setting matrix
              (720p/1080p x 15/30fps x veryfast/superfast) with CPU recording.

          stream-test --method <m> --url <rtmps-ingest> --key-file <path>
                      [--minutes 5] [--fps 15] [--size 1280x720] [--preset veryfast]
                      [--ffmpeg <path>]
              Streams to YouTube. The key is read from a file and never logged.

        Reports go to .\reports\<timestamp>-<label>\ (report.json, report.md,
        samples\, timeline.csv).
        """);
    }
}
