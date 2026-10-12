//+------------------------------------------------------------------+
//|                                             SpikeChartShot.mq5    |
//|                                   Copyright 2026, EA Safety Score |
//|                                     https://www.easafetyscore.com |
//+------------------------------------------------------------------+
//                                                                    |
//  Phase 0 spike helper EA for the Live Streamer project.            |
//                                                                    |
//  Every IntervalMs it saves a screenshot of the chart it is         |
//  attached to and appends one line to a CSV log. The CaptureSpike   |
//  tool on the same machine reads the newest PNG. This capture path  |
//  may keep working when the desktop is locked or RDP is closed;     |
//  the spike tests this hypothesis.                                 |
//                                                                    |
//  Files it writes (no account number, balance, broker or server):   |
//    Common\Files\LiveStreamerSpike\terminal-path.txt                |
//      -> one line: TERMINAL_DATA_PATH, so the tool can find the     |
//         PNG folder below.                                          |
//    Common\Files\LiveStreamerSpike\chartshot-log.csv                |
//      -> utc,seq,file,ok,err  (one line per screenshot)             |
//    <TERMINAL_DATA_PATH>\MQL5\Files\LiveStreamerSpike\shot_N.png    |
//      -> rotating shot_0.png / shot_1.png / shot_2.png              |
//                                                                    |
//  No network calls. Nothing is uploaded anywhere.                   |
//                                                                    |
//+------------------------------------------------------------------+
#property copyright "Copyright 2026, EA Safety Score"
#property link "https://www.easafetyscore.com"
#property version "1.00"
#property description "Writes chart screenshots + a CSV log for the CaptureSpike tool."
#property description "Local files only - no network, no account data."

//--- Inputs ---------------------------------------------------------
input int IntervalMs = 1000; // Milliseconds between screenshots
input int Width = 1280;      // Screenshot width in pixels
input int Height = 720;      // Screenshot height in pixels
input bool ShowCaptureClock = false; // Test-only chart clock to detect frozen capture without market ticks

//--- Layout ---------------------------------------------------------
#define SPIKE_FOLDER "LiveStreamerSpike"
#define PATH_FILE "LiveStreamerSpike\\terminal-path.txt"
#define LOG_FILE "LiveStreamerSpike\\chartshot-log.csv"
#define MAX_LOG_LINES 5000
#define SHOT_COUNT 3
#define CLOCK_OBJECT "LiveStreamerSpikeCaptureClock"

//--- Globals --------------------------------------------------------
int g_seq = 0;
int g_logLines = 0;

//+------------------------------------------------------------------+
//| UTC timestamp in ISO8601 form for the CSV log.                   |
//+------------------------------------------------------------------+
string UtcIso() {
  MqlDateTime dt;
  TimeToStruct(TimeGMT(), dt);
  return StringFormat("%04d-%02d-%02dT%02d:%02d:%02dZ", dt.year, dt.mon,
                      dt.day, dt.hour, dt.min, dt.sec);
}

//+------------------------------------------------------------------+
//| Truncate the CSV log (fresh start / keep it bounded).            |
//+------------------------------------------------------------------+
void TruncateLog() {
  int h = FileOpen(LOG_FILE, FILE_WRITE | FILE_TXT | FILE_ANSI | FILE_COMMON |
                                 FILE_SHARE_READ);
  if (h != INVALID_HANDLE) {
    FileClose(h);
    g_logLines = 0;
  }
}

//+------------------------------------------------------------------+
//| Append one line: utc,seq,file,ok,err                             |
//+------------------------------------------------------------------+
void AppendLog(const int seq, const string file, const bool ok, const int err) {
  if (g_logLines >= MAX_LOG_LINES)
    TruncateLog();

  int h = FileOpen(LOG_FILE, FILE_READ | FILE_WRITE | FILE_TXT | FILE_ANSI |
                                 FILE_COMMON | FILE_SHARE_READ);
  if (h == INVALID_HANDLE) {
    PrintFormat("[SpikeChartShot] WARNING: cannot open log file (error %d).",
                GetLastError());
    return;
  }
  FileSeek(h, 0, SEEK_END);
  FileWriteString(h, StringFormat("%s,%d,%s,%d,%d\n", UtcIso(), seq, file,
                                  ok ? 1 : 0, err));
  FileClose(h);
  g_logLines++;
}

//+------------------------------------------------------------------+
int OnInit() {
  // PNG folder under <TERMINAL_DATA_PATH>\MQL5\Files (local files).
  if (!FolderCreate(SPIKE_FOLDER)) {
    PrintFormat("[SpikeChartShot] WARNING: FolderCreate failed (error %d).", GetLastError());
  }
  // Log + path file under Terminal\Common\Files (shared with the tool).
  if (!FolderCreate(SPIKE_FOLDER, FILE_COMMON)) {
    PrintFormat("[SpikeChartShot] WARNING: FolderCreate (COMMON) failed (error %d).", GetLastError());
  }

  int h = FileOpen(PATH_FILE, FILE_WRITE | FILE_TXT | FILE_ANSI | FILE_COMMON |
                                  FILE_SHARE_READ);
  if (h == INVALID_HANDLE) {
    PrintFormat("[SpikeChartShot] WARNING: cannot write %s (error %d).",
                PATH_FILE, GetLastError());
  } else {
    FileWriteString(h, TerminalInfoString(TERMINAL_DATA_PATH));
    FileClose(h);
  }

  TruncateLog();
  if (!EventSetMillisecondTimer(IntervalMs)) {
    PrintFormat("[SpikeChartShot] ERROR: EventSetMillisecondTimer failed (error %d).", GetLastError());
    return INIT_FAILED;
  }
  PrintFormat("[SpikeChartShot] started: every %d ms, %dx%d, folder %s.",
              IntervalMs, Width, Height, SPIKE_FOLDER);
  return INIT_SUCCEEDED;
}

//+------------------------------------------------------------------+
void OnTimer() {
  g_seq++;
  if (ShowCaptureClock) {
    if (ObjectFind(0, CLOCK_OBJECT) < 0) {
      ObjectCreate(0, CLOCK_OBJECT, OBJ_LABEL, 0, 0, 0);
      ObjectSetInteger(0, CLOCK_OBJECT, OBJPROP_XDISTANCE, 20);
      ObjectSetInteger(0, CLOCK_OBJECT, OBJPROP_YDISTANCE, 40);
      ObjectSetInteger(0, CLOCK_OBJECT, OBJPROP_COLOR, clrOrange);
      ObjectSetInteger(0, CLOCK_OBJECT, OBJPROP_FONTSIZE, 16);
    }
    ObjectSetString(0, CLOCK_OBJECT, OBJPROP_TEXT, "CAPTURE TEST " + UtcIso());
    ChartRedraw(0);
  }
  string file = StringFormat("shot_%d.png", g_seq % SHOT_COUNT);
  string path = SPIKE_FOLDER + "\\" + file;

  ResetLastError();
  bool ok = ChartScreenShot(0, path, Width, Height, ALIGN_RIGHT);
  int err = GetLastError();

  AppendLog(g_seq, file, ok, err);
}

//+------------------------------------------------------------------+
void OnDeinit(const int reason) {
  EventKillTimer();
  if (ShowCaptureClock)
    ObjectDelete(0, CLOCK_OBJECT);
  Print("[SpikeChartShot] stopped.");
}
//+------------------------------------------------------------------+
