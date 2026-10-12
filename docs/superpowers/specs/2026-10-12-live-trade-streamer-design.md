# Live Trade Streamer (Windows desktop app) — Design Spec

> Repository context: this document started in SafetyScore. Paths beginning with
> `web/`, `supabase/`, or `mt5/` refer to that separate platform repository.
> Here, `spike/` is local; historical `live-streamer/spike/` paths refer to the same source.

Date: 2026-10-12
Status: Implementation in progress — Phase 0 recovery. Phases 1a and 1b are not built.
Admin UI approval in §6.3 is still pending.

## 1. What we are building

A small Windows app that runs on the MT5 Windows VPS. It takes the chart of
one MT5 terminal, adds a branded stats panel, and streams it to YouTube Live
24/5. It also tells the website which YouTube video is live, so the existing
`/live-trade` page always shows the right video.

OBS Studio is too heavy for this job. This app does one thing only.

The website side already exists (spec `2026-07-28-live-trade-design.md`,
migration `049_live_streams.sql`). This spec adds the missing part:
**MT5 → YouTube → website, with no human in the loop.**

## 2. Decisions already made (2026-10-12)

| Topic | Decision |
|---|---|
| Where MT5 runs | Windows VPS (size unknown — measured in Phase 0) |
| Who uses the app | Only Sila (internal tool, not sold) |
| Look | Hybrid: real MT5 chart + branded overlay panel from EA data |
| Must be hidden | Account number, $ balance, broker server |
| Per-trade info | Show symbol, Buy/Sell, **lots**, prices, **$ profit**, pips |
| Stream length | 24/5 |
| Weekend | Stop on market close, start a new broadcast when ticks return |
| Streams at once | One EA per stream (MVP) |
| Language | C# on .NET 10 (LTS). .NET 8 support ends Nov 2026 |
| YouTube | App logs in with OAuth and manages broadcasts itself |
| Sound | Silent (a silent audio track is still sent) |
| Delay | Several seconds is OK (`latencyPreference = low`) |
| Website | App sends the new video ID + heartbeat to a new secure API |

Note: showing lots and $ profit per trade lets viewers roughly estimate the
account size. Sila accepted this.

## 3. Non-goals (MVP)

- No chat, no viewer count, no audio, no webcam.
- No multi-stream grid. One app instance = one EA = one YouTube stream.
- No remote control from the admin console (Phase 2).
- No distribution to customers (no installer, no license check).
- No change to `license-verify`, signing, or payments.

## 4. Architecture

```
Windows VPS
┌──────────────────────────────────────────────────────────────┐
│ MT5 terminal                                                  │
│  ├─ Chart window ─────────────(capture, crop to chart)──┐     │
│  └─ StreamFeed EA ──(JSON file every 1 s, local only)─┐ │     │
│                                                       ▼ ▼     │
│ Live Streamer (C# tray app)                                   │
│  ├─ FrameSource   – grabs chart pixels                        │
│  ├─ FeedReader    – reads EA JSON                             │
│  ├─ Compositor    – crop + blur boxes + overlay → frame       │
│  ├─ Encoder       – ffmpeg.exe child (raw frames → H.264)     │
│  ├─ YouTubeClient – OAuth, broadcasts, 11 h rotation          │
│  ├─ SiteReporter  – video ID + heartbeat → website            │
│  └─ Supervisor    – state machine, restarts, logs             │
└──────────────────────────────┬───────────────────────────────┘
                               │ RTMPS
                               ▼
                         YouTube Live
                               ▲ embed by video ID
easafetyscore.com /live-trade ─┘  (video ID + "is live" from DB)
```

## 5. Privacy rules (hard rules)

1. **Capture only the chart area.** The MT5 title bar shows the account number
   and server. The Toolbox shows balance. We never send pixels outside the
   chart rectangle.
2. **Fail closed.** If the app cannot find the chart rectangle, it sends a
   "Reconnecting to MT5…" slate. It never falls back to the full window.
3. **Blur boxes.** Sila can draw blur boxes on the chart (for example an EA
   panel that prints balance). Blur is applied inside the app, before the
   frame goes to ffmpeg.
4. **The feed file contains only showable data.** The EA never writes account
   number, account name, balance, equity in $, broker, or server. Percentages
   are computed inside the EA.
5. **Preview = output.** The preview in the app shows the exact outgoing frame.
6. **Secrets are never logged.** Stream key, Google refresh token, and site
   token are redacted in logs.

## 6. Components

### 6.1 StreamFeed EA (MQL5)

- File: `mt5/live-stream-feed/SafetyScoreStreamFeed.mq5` (Expert Advisor).
- Follows `mt5/AGENTS.md`: compile with 0 errors and 0 warnings, one-line
  `input` and `#property` lines.
- No network calls. It only writes a file, so no WebRequest allowlist.
- `OnTimer` every 1 s writes `live-feed-<StreamId>.json` to the **Common**
  files folder (`FILE_COMMON`). It writes to a temp file, then `FileMove`
  with `FILE_REWRITE`, so the app never reads half a file.
- Inputs: `StreamId` (text), `MagicFilter` (0 = all trades), `Product name`.
- JSON fields (example):

```json
{
  "v": 1,
  "ts_utc": "2026-10-12T08:00:01Z",
  "symbol": "XAUUSD",
  "market_open": true,
  "last_tick_utc": "2026-10-12T08:00:00Z",
  "today_pl_pct": 0.84,
  "floating_pl_pct": -0.12,
  "closed_today": { "wins": 3, "losses": 1 },
  "positions": [
    { "symbol": "XAUUSD", "side": "BUY", "lots": 0.10,
      "open_price": 2650.12, "price": 2651.40,
      "sl": 2645.00, "tp": 2660.00,
      "profit": 12.80, "pips": 12.8, "opened_utc": "2026-10-12T07:41:00Z" }
  ]
}
```

- `market_open` comes from the symbol's trade session times. The app also
  checks tick age (see §7).

### 6.2 Desktop app (C# / .NET 10)

**Repo layout**

```
live-streamer/
  LiveStreamer.sln
  src/LiveStreamer.Core/       # pure logic, cross-platform (net10.0)
  src/LiveStreamer.Windows/    # capture, DPAPI, Task Scheduler (net10.0-windows)
  src/LiveStreamer.App/        # WinForms tray app + settings window
  tests/LiveStreamer.Core.Tests/  # xUnit, runs on macOS / CI too
  spike/                       # Phase 0 throwaway tools
  scripts/Get-FFmpeg.ps1       # downloads pinned ffmpeg build + SHA-256 check
```

`Core` holds all logic that can be tested without Windows: state machine,
rotation timer, market-hours logic, feed parsing, overlay text formatting,
crop/blur geometry, ffmpeg argument builder, retry backoff.

**UI (WinForms, lightest option)**

- Tray icon with status color (grey = idle, amber = waiting/degraded,
  red = live) and menu: Start, Stop, Open settings, Open logs, Exit.
- Settings window:
  - Pick the MT5 window and the chart.
  - Live preview of the outgoing frame. Draw/remove blur boxes with the mouse.
  - Overlay options (product name, Real/Demo badge, which stats to show).
  - "Connect YouTube" button (OAuth) and the YouTube privacy setting
    (default: Public).
  - Website: stream ID + app token (pasted once from admin).
  - Status: state, FPS, bitrate, CPU %, YouTube status, last heartbeat.
- Single instance (named mutex). Auto-start with a Task Scheduler
  "At log on" task for the VPS auto-logon user.

**FrameSource (pluggable)**

Phase 0 picks the default. Candidates:

| Source | How | Notes |
|---|---|---|
| `GdiWindowSource` | BitBlt of the MT5 window | Simple. Needs a live desktop. |
| `PrintWindowSource` | `PrintWindow(PW_RENDERFULLCONTENT)` | Window draws itself. May survive a covered window. |
| `WgcSource` | Windows Graphics Capture | Modern API. Needs Windows 10 1903+ / Server 2022. |
| `ChartScreenShotSource` | EA calls `ChartScreenShot()` every second, app reads the PNG | Only the chart, by design. ~1 new image per second. Possible fix if the desktop is locked. |

The chart rectangle comes from the chart's child window (HWND) and is
refreshed every second, so moving/resizing MT5 is followed.

**Compositor and overlay**

- Output canvas 1280×720 (final size/FPS set by Phase 0).
- Layout sketch:

```
┌───────────────────────────────────────────────────────────────┐
│ [logo] FlexScalper · REAL · ● LIVE                08:00:01 UTC│
├───────────────────────────────────────────┬───────────────────┤
│                                           │ Today   +0.84 %   │
│                                           │ Floating −0.12 %  │
│        MT5 chart (cropped, blurred        │ Closed  3W / 1L   │
│        boxes applied)                     ├───────────────────┤
│                                           │ XAUUSD BUY 0.10   │
│                                           │  2650.12→2651.40  │
│                                           │  +12.80 $ +12.8p  │
│                                           │ … +2 more         │
├───────────────────────────────────────────┴───────────────────┤
│ Trading involves risk. Past results do not guarantee future.  │
└───────────────────────────────────────────────────────────────┘
```

- Colors and fonts follow `DESIGN.md` (dark surface, semantic green/red).
- Drawing uses `System.Drawing` (built into .NET on Windows, no extra
  dependency).
- If the feed is stale (> 10 s), the panel shows "Data paused". The chart
  keeps streaming.

**Encoder (ffmpeg child process)**

- The app writes raw BGRA frames to ffmpeg `stdin`.
- ffmpeg encodes H.264 (libx264) + a silent AAC track (`anullsrc`) and sends
  RTMPS to the ingest address returned by the YouTube API (not hard-coded).
- Keyframe every 2 s. Frame rate, preset, and bitrate are set from Phase 0
  results (start candidates: 15 or 30 FPS, `veryfast`, 2.5–4 Mbps).
- ffmpeg is **not** committed. `scripts/Get-FFmpeg.ps1` downloads a pinned
  build and checks its SHA-256.
- A GPL build (with libx264) is fine because the app is internal only. If the
  app is ever given to customers, review the licence first.

**YouTubeClient**

- Library: official `Google.Apis.YouTube.v3` NuGet package.
- OAuth "Desktop app" client, loopback redirect, scope
  `https://www.googleapis.com/auth/youtube`. The refresh token is stored with
  Windows DPAPI.
- **The Google Cloud OAuth consent screen must be "In production".** In
  "Testing" mode Google expires the refresh token after 7 days, which would
  break 24/5. For personal use (< 100 users) no Google verification is
  needed. Sila sees an "unverified app" warning once.
- One **reusable** `liveStream` (the ingest point) is created once and saved.
- Each session = one `liveBroadcast`:
  `enableAutoStart = true`, `enableAutoStop = true`, `enableEmbed = true`,
  `enableDvr = true`, `latencyPreference = low`,
  `selfDeclaredMadeForKids = false`, title like
  "FlexScalper LIVE · Real account · 12 Oct 2026 08:00 UTC".
- The broadcast ID is also the YouTube video ID. It goes to the website right
  after the broadcast is created.
- **Rotation every 11 h** (YouTube may not archive streams over 12 h).
  Preferred: create + bind the next broadcast to the same reusable stream,
  then complete the old one, with no ffmpeg restart. Fallback: stop ffmpeg,
  complete old, create new, start ffmpeg (a gap of a few seconds). Phase 1a
  tests which one works.
- API usage is a few calls per session plus one status check per minute,
  far below the default daily quota.

**SiteReporter**

- `POST https://easafetyscore.com/api/live-streams/ingest` with
  `Authorization: Bearer <app token>`.
- Sends `{ status: "live", youtube_video_id }` every 30 s while live, and
  `{ status: "offline" }` when the session ends.
- Network errors: retry with backoff. Never blocks the video.

**Config, secrets, logs**

- Config: `%APPDATA%\SafetyScore\LiveStreamer\config.json`.
- Secrets (refresh token, site token): DPAPI-encrypted in the same folder.
- Logs: `%APPDATA%\SafetyScore\LiveStreamer\logs\`, daily files, kept 14 days.
- The stream key appears in the ffmpeg command line. On a single-user VPS
  this is accepted; app logs always redact it.

### 6.3 Website changes

**Migration `071_live_stream_streamer_ingest.sql`** (number checked on
2026-10-12: latest is `070`. Check again before creating. The "next: 052"
note in `AGENTS.md` is out of date.)

- `live_streams`:
  - add `source TEXT NOT NULL DEFAULT 'manual'`,
    CHECK `source IN ('manual', 'streamer')`.
  - add `last_heartbeat_at TIMESTAMPTZ NULL`.
  - make `youtube_url` and `youtube_video_id` nullable, with CHECK
    `source = 'streamer' OR (youtube_url IS NOT NULL AND youtube_video_id IS NOT NULL)`.
    Existing rows stay valid (they are all `manual` with a URL).
- New table `live_stream_ingest_tokens`:
  `id`, `live_stream_id` (UNIQUE, FK → `live_streams`, cascade delete),
  `token_hash TEXT NOT NULL UNIQUE` (SHA-256), `created_at`, `last_used_at`.
  RLS enabled, **no** public policies, `REVOKE ALL` from anon/authenticated/
  public. Service role only.
  - Why a separate table: `live_streams` is readable by `anon`, so a hash
    column there would be public.
- Run `sdd-migration-review` before merge.

**New route `POST /api/live-streams/ingest`** (category: Secret/integration)

- Bearer token → SHA-256 → look up `live_stream_ingest_tokens`. Missing or
  unknown → `401`. This check happens before the body is parsed.
- Per-token rate limit (10 requests/min) using the existing Postgres
  rate-limit buckets → `429`.
- Body (Zod, strict, unknown keys rejected):
  `{ status: "live", youtube_video_id: /^[A-Za-z0-9_-]{11}$/ }` or
  `{ status: "offline" }` → otherwise `400`.
- `live`: set `youtube_video_id`, `youtube_url =
  https://www.youtube.com/watch?v=<id>`, `last_heartbeat_at = now()`.
- `offline`: set `last_heartbeat_at = NULL`.
- Only rows with `source = 'streamer'` can be updated → otherwise `409`.
- Update `last_used_at` on the token. Response `{ success: true }`.

**New admin route `POST /api/admin/live-streams/[id]/ingest-token`**

- `requireAdminApi({ permission: "operations.manage", requireMfa: true })`.
- Creates a random token (`lst_` + 32 random bytes, base64url), stores only
  the hash (replaces any old token), returns the plain token **once**.

**Admin UI ("Live Streams")**

- Form: new "Source" select (Manual / Streamer app). For Streamer, the
  YouTube URL is optional.
- Row action: "Generate app token" → dialog shows the token once with a copy
  button and a warning.
- Repo rule: protected admin screens are frozen. **This change needs Sila's
  explicit OK.**

**Public page logic** (`web/src/lib/live-streams/public.ts`)

- A `streamer` row counts as live only if `last_heartbeat_at` is newer than
  150 s. (The heartbeat is every 30 s and the page cache is 60 s, so 150 s
  avoids flicker.) `manual` rows keep today's behavior.
- When nothing is live, a `streamer` row that has a video ID shows as the
  latest recording in the existing "Recent recordings" list.
- `LiveTradeCard` stays the same.

## 7. Stream state machine (in `Core`)

| State | Meaning | Leaves when |
|---|---|---|
| `Idle` | Stopped by Sila | Start pressed / auto-start |
| `WaitingForMarket` | Market closed (weekend/holiday), no broadcast | Feed says open **and** a tick in the last 60 s → `Starting` |
| `Starting` | Create broadcast, bind, start ffmpeg, first heartbeat | Video flowing → `Live`; error → `Backoff` |
| `Live` | Streaming, heartbeat every 30 s | 11 h → `Rotating`; market closed 15 min → `Ending`; MT5/chart lost → `Degraded` |
| `Degraded` | Slate "Reconnecting to MT5…", still streaming | Chart back → `Live`; 10 min → `Ending` |
| `Rotating` | Switch to a new broadcast (§6.2) | Done → `Live` |
| `Ending` | Stop ffmpeg, complete broadcast, heartbeat `offline` | → `WaitingForMarket` (or `Idle` if stopped) |
| `Backoff` | API/network error | Retry after 5 s, 10 s, 30 s, 60 s … max 5 min |

"Market closed" = feed `market_open = false` **or** no tick for 15 minutes.

## 8. Phase 0 — tests on the VPS (before building the app)

Sila runs a small test tool on the VPS and sends back the report file.

**Capture test.** For each FrameSource in §6.2, grab 1 frame per second for
2 minutes and detect black/frozen frames, in each session state:

| Session state | Why |
|---|---|
| RDP connected | Baseline |
| RDP disconnected (closed window) | Known black-screen problem |
| `tscon` to console, then disconnect | Common fix |
| Auto-logon + locked desktop (EA Hosting style) | Matches `Lock-Workstation.ps1` |

**CPU test.** Encode a recorded 60 s sample with candidate settings
(720p/1080p × 15/30 FPS × `veryfast`/`superfast`) and record CPU % and
encode speed. Plus 5 minutes of a real unlisted test stream, checking
YouTube Studio "stream health".

**Also collected:** Windows version, CPU cores, RAM, upload speed.

**Pass rule:** pick the FrameSource that has no black frames in the states we
will really use, and the highest quality that keeps total VPS CPU under 70 %
with MT5 running. If no window capture survives, `ChartScreenShotSource`
becomes the default.

**Recovery evidence (2026-10-12, provisional):** `mqlvm` has Windows 10 Pro
22H2, 4 logical CPUs, and 8 GB RAM. ChartScreenShot passed 120-second tests
with RDP active and disconnected using a moving test-only UTC clock.
GDI window and PrintWindow returned frozen pixels. Provisional settings are
1280×720, 15 FPS, `veryfast`, 3000 kbps. 1080p could not reach target FPS in
the short encoder tests. Console/locked tests, the full CPU test with the real
trading EA, and the unlisted YouTube test are still pending. Phase 0 is not
fully complete. See [`../plans/2026-10-12-live-trade-streamer-recovery.md`](../plans/2026-10-12-live-trade-streamer-recovery.md).

## 9. Delivery phases

| Phase | Scope | Done when |
|---|---|---|
| 0 | Spike tools + VPS report | Capture source and encoder settings chosen, written into this spec |
| 1a | Feed EA + app → YouTube (capture, overlay, ffmpeg, OAuth, rotation, weekend, auto-start) | 24 h soak on VPS incl. one rotation, one RDP disconnect, one MT5 restart, one network drop |
| 1b | Website ingest (migration, ingest route, admin token, public logic) + SiteReporter | `/live-trade` follows rotations and goes offline within ~3 min of stop |
| 2 | Later: session history list, server check that the video belongs to our channel, multi-stream, control from admin / EA Hosting agent, CI job for .NET tests | — |

Until 1b ships, an admin can paste the current video URL by hand.

## 10. Testing

- **C# (xUnit, `LiveStreamer.Core.Tests`):** state machine transitions,
  rotation timing, market-closed logic, feed parsing (valid / stale /
  broken JSON), overlay number formatting, crop + blur geometry (never
  outside the chart rect), ffmpeg argument builder (key redaction in logs),
  backoff timing. Runs with `dotnet test` on macOS and Windows.
- **Web (Vitest):** ingest route — missing/wrong token `401`, malformed or
  unknown keys `400`, manual row `409`, rate limit `429`, success, DB failure
  `500`. Admin token route — rejected `requireAdminApi()` never reaches the
  service role, success returns the token once and stores only the hash.
  Public logic — manual vs streamer freshness window.
- **Migration:** `sdd-migration-review`, then `supabase db push`, then
  `pnpm supabase:types && pnpm types:check`.
- **MQL5:** compile 0 errors / 0 warnings; manual check of the JSON file.
- **Manual E2E on VPS:** the Phase 1a soak test above.

## 11. Risks and open points

1. Capture on a locked or disconnected desktop — answered by Phase 0.
2. Seamless 11 h rotation — tested in 1a; fallback is a short gap.
3. VPS CPU budget unknown — answered by Phase 0.
4. YouTube may warn about low frame rate or static video — check stream
   health in Phase 0.
5. YouTube policy on financial content — keep the risk line on screen; no
   "guaranteed profit" words in titles.
6. A leaked app token lets someone change the video on `/live-trade` (only to
   another YouTube video ID). Mitigation: token regenerate in admin; Phase 2
   adds a server check that the video belongs to our channel.

## 12. Setup checklist for Sila

- YouTube channel: live streaming enabled (first time can take up to 24 h),
  "Made for kids" = **No** (needed for embeds).
- Google Cloud: create a project, enable **YouTube Data API v3**, OAuth
  consent screen **External** + **In production**, OAuth client type
  **Desktop app**.
- VPS: auto-logon user for the app, .NET 10 Desktop Runtime installed.
- Website: approve the admin "Live Streams" change (§6.3).
