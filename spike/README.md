# MT5 Live Stream

**Status — 2026-10-12: Phase 0 test tool works. The full streaming app is not built yet.**

Use this tool to test MT5 chart capture, encoder speed, and a short YouTube
stream. It does not run an automatic 24/5 stream or update the website.

## 1. Current progress

| Part | Status |
|---|---|
| Windows test tool and self-contained ZIP | Done; no .NET install needed on the VPS |
| Chart capture with RDP open / closed | Passed on the separate test terminal |
| Encoder checks and logic tests | 51 tests pass; 85.1% line coverage; short encoder tests pass |
| Production tests | Console transfer, locked desktop, real-EA CPU test, and YouTube test still pending |
| Full app and website connection | Not built: tray app, privacy masks, stats feed, OAuth, rotation, and website heartbeat |

**Current test choice:** `chartshot`, 1280×720, 15 FPS, `veryfast`, 3000 kbps.
The helper makes about one new chart image per second. The encoder sends
15 frames per second by repeating that image. Sound is silent.

At 720p/15 FPS, a 15-second test sent 15.1 FPS with no dropped frames and
3.8% average machine CPU. This was an offline chart, not a real trading EA.
1080p reached only about 11 FPS. These are starting settings, not a production guarantee.

Design and recovery notes are in the source repository under `docs/superpowers/`.
Repository: https://github.com/rithsila/mt5-live-stream
The website lives in the separate SafetyScore project; it is not included here.

## 2. Use the installed VPS tool first

These paths are for the existing `mqlvm` test setup. A fresh VPS needs section 3.
Connect with Remote Desktop (RDP), then open **PowerShell as Administrator**.

### Step 1 — Open the tool folder (10 seconds)

```powershell
cd C:\LiveStreamerV02\LiveStreamerSpike
.\CaptureSpike.exe --help
```

Keep the EXE and all its DLLs together. This is a command-line tool, not a tray app.

### Step 2 — Start the separate test terminal, if it is closed (15 seconds)

```powershell
Start-ScheduledTask -TaskName 'SafetyScore-LiveStreamer-TestTerminal'
```

Its files are in `C:\LiveStreamerTestTerminal`. Its startup config disables
trading and DLL imports. If it is already open, leave it running.
Do not start or change your trading terminals for this test.

### Step 3 — Check the setup (10 seconds)

```powershell
.\CaptureSpike.exe info --label setup-check
```

Open the report folder printed by the command. In `report.md`, look for
`EA files` during capture, and a chart rectangle in the `info` report.
`info` can finish successfully even when no chart or FFmpeg is found; read its report.

### Step 4 — Capture chart images (2 minutes)

```powershell
.\CaptureSpike.exe capture --methods chartshot --label quick-chart --delay 0 --duration 120
```

This command saves local chart samples. It **does not send a stream to YouTube**.
Open its `samples` folder. The orange `CAPTURE TEST` clock should change between images.
The clock proves that capture moves; it does not prove that market ticks are live.

## 3. Install on a fresh Windows VPS

Requirements: Windows x64, Windows 10 1903 or newer, MT5, and a logged-on desktop
user. FFmpeg is needed for encoding and YouTube tests, but not for capture.

1. Build or obtain `LiveStreamerSpike.zip` using section 8. Unblock the ZIP in
   Windows Properties, then extract it. For example, extract under `C:\LiveStreamerV02`
   to get `C:\LiveStreamerV02\LiveStreamerSpike`. Keep every DLL with the EXE.
2. Open Administrator PowerShell in that folder. Download the checked FFmpeg build:
   ```powershell
   powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Get-FFmpeg.ps1
   ```
   Wait for `SHA-256 OK` and `Done`.
3. In a **separate test MT5 terminal**, use File → Open Data Folder. Copy
   `mql5\SpikeChartShot.mq5` from the ZIP to that terminal's `MQL5\Experts` folder.
4. Open the copied source in MetaEditor and press **F7**. Require **0 errors and
   0 warnings**. Attach it to a test chart. **Never replace a trading EA with this helper.**
5. For an offline test, set `ShowCaptureClock = true` in the helper's inputs.
   Keep `IntervalMs = 1000`, `Width = 1280`, and `Height = 720`. Leave AutoTrading off;
   this helper only writes local files. Then run the setup and capture commands above.

Only run one SpikeChartShot helper at a time. Helpers share the same files.
For a live-market test, use a separate demo chart. A separate chart does not
replace your trading EA, but it also does not show that EA's on-chart panel.
Capturing a trading EA's own chart safely needs the production helper design.

## 4. Test capture in each desktop state

Use a test terminal with a moving chart or the diagnostic clock. Run one test
at a time. Commands in this section run **inside RDP**, not directly over SSH.
A console transfer or lock changes your desktop session. Only do those tests
when it is safe to close RDP and you can sign in again.

### A — RDP stays open (2 minutes)

```powershell
.\CaptureSpike.exe capture --label A-rdp-open --delay 0 --duration 120
```

Do not close RDP until the test ends. This checks all five capture methods.

### B — RDP is closed (about 6 minutes)

```powershell
.\CaptureSpike.exe capture --label B-rdp-closed --delay 20 --duration 300
```

Close the RDP window within 20 seconds. **Do not sign out of Windows.**
Wait 6 minutes before reconnecting, so the capture finishes while disconnected.

### C — Transfer RDP to the console (about 6 minutes)

Run from Administrator PowerShell inside RDP:

```powershell
Start-Process .\CaptureSpike.exe -ArgumentList 'capture --label C-console --delay 30 --duration 300'
Start-Sleep 10
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Disconnect-ToConsole.ps1
```

This closes RDP and moves that desktop session to the console. Wait 6 minutes,
then reconnect. Do not run this session-transfer script from SSH.

### D — Console with locked desktop (about 6 minutes)

Only test this if the production VPS will run with its desktop locked:

```powershell
Start-Process .\CaptureSpike.exe -ArgumentList 'capture --label D-locked --delay 30 --duration 300'
Start-Sleep 10
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Disconnect-ToConsole.ps1 -LockAfterSeconds 10
```

RDP closes, then the desktop locks. Wait 6 minutes before signing in again.

### Read the capture result

| Report field | What to check |
|---|---|
| `ok %` | Near 100% for the chosen method; also inspect its errors |
| `black %` | 0% |
| `same %` | Clock or moving-chart images should change; 99% is a frozen-image warning |
| `stale-src %` | 0% for the chosen method |
| `session state` | Must match the state you meant to test |

Inspect the PNG samples too. On this VPS, `chartshot` passed both open and closed
RDP tests. GDI window and PrintWindow returned frozen images. WGC did not work
reliably. Do not choose a method just because its report says `ok`.

### Run capture from SSH instead

SSH starts processes in Windows session 0. MT5 runs in the desktop user's session.
A direct EXE command over SSH cannot see that user's MT5 windows.
Use `scripts\Run-DesktopProbe.ps1` in a Windows SSH shell:

```powershell
cd C:\LiveStreamerV02\LiveStreamerSpike
powershell -NoProfile -ExecutionPolicy Bypass -File .\scripts\Run-DesktopProbe.ps1 -Command capture -Label ssh-capture-01 -Seconds 120 -DesktopUser mt5
```

Replace `mt5` if the logged-on desktop user is different. That user must already
be logged on; RDP may be disconnected. Use a **new label** for each helper run.
The helper starts a scheduled task and returns before the test finishes.

```powershell
Get-ScheduledTask -TaskName 'SafetyScore-Spike-ssh-capture-01'
Get-ScheduledTaskInfo -TaskName 'SafetyScore-Spike-ssh-capture-01'
```

Wait for task state `Ready`, then check `LastTaskResult` and the report.
`267009` means the task is still running, not that it failed.
The helper supports `info`, `capture`, and `bench`; **not** `stream-test`.

## 5. Test encoder speed and CPU

From the VPS tool folder, with the helper running:

```powershell
.\CaptureSpike.exe bench --method chartshot --label cpu-full --seconds 60 --delay 20
```

This runs 8 settings: 720p/1080p × 15/30 FPS × veryfast/superfast.
Allow **9 minutes**. It encodes locally and discards the video; it does not upload it.
During the initial 20-second delay, put the desktop in the state production will use.
Do not run another capture test at the same time.

For a production decision, measure with the intended trading workload running.
Choose a setting that reaches target FPS without sustained drops, has encoding
speed close to `1.00x`, and keeps total VPS CPU below 70%. Low total CPU alone
is not enough: a single busy CPU core can still make 1080p too slow.

## 6. Use it for a short YouTube test

**Manual test only. There is no automatic YouTube login or broadcast creation.**
This command sends video to YouTube. The other tests only create local reports.
The app has no privacy-mask editor yet. Use a checked test chart, not a real account.

### Step 1 — Create an unlisted test stream (about 2 minutes)

In YouTube Studio → Go live, create a stream with visibility **Unlisted** and
Made for kids **No**. Live streaming must already be enabled on the channel;
first-time activation can take 24 hours. Check whether Studio has automatic
start enabled before sending video; sending video may start the broadcast.

### Step 2 — Save the key locally (1 minute)

In the VPS tool folder:

```powershell
notepad .\key.txt
```

Paste only the stream key, on one line, then save. Keep it on the VPS.
Do not paste it into chat, screenshots, git, or the command line. The repo ignores
`key.txt`. It is still plaintext: use it only on a trusted, single-user VPS.

### Step 3 — Send the test stream (5 minutes)

Copy the **RTMPS ingest URL** from YouTube Studio. Set `$ingest` below to that URL.
The shown URL is an example, not a value to assume for every broadcast.

```powershell
$ingest = 'rtmps://a.rtmp.youtube.com/live2'
.\CaptureSpike.exe stream-test --method chartshot --url $ingest --key-file .\key.txt --minutes 5 --fps 15 --size 1280x720 --preset veryfast --label youtube-test
```

The tool sends H.264 video plus a silent AAC audio track. It stops encoding when
5 minutes end. There is no branded stats panel or website update yet.

### Step 4 — Check YouTube Studio (during the 5-minute test)

Check stream health, the moving chart/clock, image privacy, and the silent track.
If automatic start is off, use Studio's **Go live** control when ready.
The encoder's success report alone does not prove that YouTube showed the stream.

### Step 5 — End the broadcast and handle the key (1 minute)

Use **End stream** in Studio if needed. The tool cannot complete a broadcast by API.
After the test, confirm that the stream is no longer live. Remove `key.txt` when
it is no longer needed. If it was exposed, reset the key in YouTube Studio.
Do not use this test command as the final 24/5 setup.

## 7. Find reports and fix errors

Reports go to `reports\<date-time>-<label>\` under the folder where you ran the EXE.
The SSH helper always writes under its own tool folder. No report or screenshot
is committed to git.

| File | Use |
|---|---|
| `report.md` | Readable test summary |
| `report.json` | Full structured result |
| `timeline.csv` | Per-sample capture or CPU data |
| `samples\*.png` | Chart images from capture tests; check privacy and movement |

Exit code `0` means the command completed. For capture, **one method with one
fresh non-black frame** is enough for code 0; inspect the full report before
calling a method reliable. Code `1` means help/unknown command. Code `2` means
invalid settings, no usable capture, or capture/encoder failure.

| Error / location | Cause | Fix |
|---|---|---|
| EXE asks for .NET | An old framework-dependent ZIP | Rebuild with `Build-Package.py`; do not mix old and new DLLs |
| `no-chart` in report | No open chart, or EXE started in SSH session 0 | Open a test chart; use the desktop-task helper for SSH |
| `EA files not found` | Screenshot helper is not running for that Windows user | Attach it to the separate test chart; check MT5 Experts logs |
| `ffmpeg not found` | Encoder has not been downloaded | Run `scripts\Get-FFmpeg.ps1`; retry after `Done` |
| Scheduled task already exists | The helper label was used before | Choose a new label; do not replace a running task |

For stale data or frozen pixels, check the moving clock and `same %` / `stale-src %`.
Changing labels or getting code 0 does not fix a frozen source.

## 8. Build and test this repository locally

Requirements: Python 3.11+ and the .NET 10 SDK. Run these commands from the
`mt5-live-stream` repo root. In the SafetyScore workspace, first `cd live-streamer`.

### Logic tests (usually 2–10 seconds after packages are restored)

```sh
dotnet test spike/SpikeLogic.Tests/SpikeLogic.Tests.csproj -c Release
```

These run on macOS or Windows. They do not test the Windows desktop or YouTube.

### Release build (usually 5–30 seconds after packages are restored)

```sh
dotnet build spike/Spike.sln -c Release -warnaserror
```

Require 0 errors and 0 warnings.

### Build the Windows ZIP (allow 2 minutes; first downloads may take longer)

```sh
python3 spike/scripts/Build-Package.py
```

The builder runs the logic tests, then publishes a self-contained Windows x64 ZIP.
Output: `spike/dist/LiveStreamerSpike.zip` and `LiveStreamerSpike.zip.sha256`.
Generated binaries and FFmpeg stay out of git. A new build has a new ZIP hash;
use its matching SHA-256 file rather than a hash from an older report.

## 9. Stop tests safely

Capture, bench, and stream-test normally stop at their chosen duration. Let them
finish so they can write their reports. Stopping a process early may lose its report.
Finish a YouTube broadcast in Studio separately.

Close only the separate test terminal when testing is finished. Do not close your
trading terminals or disable the hosting agent. The test terminal task is started
manually; the tool is not a production auto-start service.

Before deleting test folders or scheduled tasks, confirm that you no longer need
those reports and that no test is running. There is no database change to roll back.

**Next action — under 1 minute:** open Administrator PowerShell on the VPS and run
`cd C:\LiveStreamerV02\LiveStreamerSpike`, then `.\CaptureSpike.exe info --label setup-check`.
