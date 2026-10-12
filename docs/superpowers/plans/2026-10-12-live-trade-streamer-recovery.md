# Live Trade Streamer — recovery checkpoint

> Repository context: this document started in SafetyScore. Paths beginning with
> `web/`, `supabase/`, or `mt5/` refer to that separate platform repository.
> Here, `spike/` is local; historical `live-streamer/spike/` paths refer to the same source.

Date: 2026-10-12
Status: Phase 0 partly verified. Production app and website integration are pending.

## 1. What was found

Devin left untracked Phase 0 sources in `live-streamer/spike/` and a Windows
ZIP. There were no production `LiveStreamer.Core`, `LiveStreamer.Windows`, or
`LiveStreamer.App` projects, no production feed EA, and no streamer ingest route.
The existing website has manual live-stream management and migration
`049_live_streams.sql` only.

The screenshot's ZIP extraction did not run correctly: PowerShell variables
were removed by shell quoting. The ZIP was still at `C:\LiveStreamerSpike.zip`.
After extraction, the framework-dependent EXE could not start because the VPS
had no .NET runtime. Also, SSH commands run in session 0, not the MT5 desktop.

## 2. Repairs implemented

1. Reproducible self-contained Windows x64 ZIP builder; .NET install not required.
2. Interactive desktop-task launcher for tests started from SSH.
3. Correct C#/WinRT capture projections and new-frame tracking; pinned bitmap memory.
4. Safe log parsing, screenshot filename checks, chart-bound checks, RTMPS validation,
   stale-frame slate, and failed-command exit codes.
5. Optional test-only UTC clock, report formatting fixes, and secret-file ignores.

Only the isolated test terminal was started/restarted, with user approval.
Its startup config disables trading and DLL imports. No existing trading
terminal, hosting task, database, payment code, or protected admin screen was changed.
The pre-existing deletion of the root `AGENTS.md` was left unchanged.

## 3. VPS facts and capture evidence

- Host: `mqlvm`; Windows 10 Pro 22H2, build 19045.3803.
- Resources: 4 logical CPUs, 8 GB RAM; no display adapter reported in this session.
- Test terminal: `C:\LiveStreamerTestTerminal`; desktop session 2, disconnected.
- Current spike: `C:\LiveStreamerV02\LiveStreamerSpike`.
- FFmpeg: 9.0.2 essentials; official archive SHA-256 checked successfully.

Test `v02-disconnected-clock` ran for 120 seconds on an offline EURUSD chart
with a moving diagnostic UTC clock. This tests capture, **not live market ticks**.

| Method | Usable frames | Black frames | Same-frame rate | Result |
|---|---|---|---|---|
| ChartScreenShot | 120/120 | 0 | 2% | Promising; clock changes while disconnected |
| GDI window | 120/120 | 0 | 99% | Frozen; not a safe default |
| PrintWindow | 120/120 | 0 | 99% | Frozen; not a safe default |
| GDI screen | 0/120 | — | — | Cannot capture disconnected desktop |
| WGC | 0/120 | — | — | Initializes after fix, but supplies no frames here |

Evidence: `reports/20261012-223221-v02-disconnected-clock/` under the current
VPS spike folder. Reports include per-frame CSV/JSON and chart-only sample PNGs.
Do not treat fresh-looking frozen GDI pixels as a capture pass.

The subsequent 120-second `rdp-connected-baseline` also passed for
ChartScreenShot: 120/120 usable frames, 0 black, 0 repeated. All samples were
in an active RDP session. Other methods were not consistently usable/fresh.
Evidence: `reports/20261012-224019-rdp-connected-baseline/` in the same folder.
Both report sets were also copied to the ignored local folder
`live-streamer/spike/reports/recovery/v02/`.

An earlier 10-second-per-setting FFmpeg smoke test completed all 8 encodes,
exit 0. 720p reached target 15/30 FPS without drops. 1080p reached about 11 FPS
and dropped frames. That smoke test ran beside capture probes on a mostly static
chart; it is **not** a production CPU qualification.

Provisional choice: ChartScreenShot at 1280×720, 15 FPS, `veryfast`, 3000 kbps,
with a silent AAC track. The chart updates about once per second. Repeat the
full CPU matrix with the intended trading EA before choosing final settings.

A separate `v02-bench-clock` test then ran all 8 settings for 15 seconds each,
without a parallel capture probe. RDP was active at the start. At 720p/15 FPS/
veryfast it sent 15.1 FPS, had zero drops/capture errors, and used 3.8% average
machine CPU (8.7% p95). All encodes exited 0. At 1080p it still only reached
about 11 FPS. These remain short, offline-chart measurements, not a full
trading-workload pass.

## 4. Verification

- Logic tests: 51 pass; line coverage 85.1%, branch coverage 82.7%.
- Release build: 0 errors and 0 warnings.
- MQL5 helper with diagnostic clock: MetaEditor reports 0 errors and 0 warnings.
  Its process exits 1 even after a successful compile; the result line and EX5
  file are the evidence, not that process exit code.
- Windows PowerShell parser: all three shipped PS1 scripts pass.
- Secret scan: no leaks found in `live-streamer/`.

Generated EXEs, ZIPs, reports, screenshots, FFmpeg, and EX5 files are ignored
by git. The recovery test run did not deploy the website or start a public stream.
Source and documentation are now managed in the separate repository below.

Installed release ZIP SHA-256:
`ec84fa0f55ed6ccb22361d066eea687d0b521e81a7185ee4e6ce48a84647beee`.
The final installed build also completed an interactive `release-info` probe,
exit 0. The extra report/geometry tests were added after this ZIP was built;
they do not change its application code.

## 5. Next bounded work

1. Test console transfer and the locked-desktop state used by the production host.
2. Repeat open/disconnected capture with the intended live chart, without replacing its trading EA.
3. Run the full 60-second-per-setting CPU matrix with the intended MT5 trading workload,
   then a 5-minute unlisted YouTube test (stream key stays on the VPS).
4. Build Phase 1a: production feed, compositor/privacy masks, tray app, OAuth,
   supervisor, rotation, and weekend handling; then do the 24-hour soak.
5. Build Phase 1b after admin UI approval: ingest migration/API, token creation,
   heartbeat freshness, and SiteReporter.

For the screenshot fallback, do **not** replace the trading EA with the feed EA.
The production design must let a separate helper chart capture the selected
trading chart, or add feed/screenshot support inside the trading EA.

## Operator commands and rollback

In an Administrator PowerShell window on the VPS:

```powershell
cd C:\LiveStreamerV02\LiveStreamerSpike
.\CaptureSpike.exe capture --label A-rdp-connected --delay 5 --duration 120
```

To start the existing test terminal after a reboot, without enabling trading:

```powershell
Start-ScheduledTask -TaskName 'SafetyScore-LiveStreamer-TestTerminal'
```

To undo this test deployment, first stop only the isolated test terminal.
With user approval, remove the generated `SafetyScore-Spike-*` tasks and the
`SafetyScore-LiveStreamer-TestTerminal` task, then remove only the new test/spike
folders. Do not remove or edit `RSTradingHostingAgent`, trading terminals, or
the original ZIP. No database rollback is needed.

## Repository and operator guide

The streamer now has its own Git repository:
https://github.com/rithsila/mt5-live-stream

The [root README](../../../README.md) covers current status, VPS setup, desktop-state
tests, CPU tests, manual YouTube streaming, report checks, and troubleshooting.
`spike/README.md` contains the same operator steps so the Windows ZIP has a guide.
Generated binaries, captures, test reports, FFmpeg, and local credentials are ignored.

Publishing this source does not mean the production app is complete. Phase 0
console/lock/live-workload/YouTube checks and Phases 1a/1b remain pending.
The website changes still belong to the separate SafetyScore repository.
