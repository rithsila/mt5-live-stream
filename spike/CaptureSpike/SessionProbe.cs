using System.Runtime.InteropServices;

namespace CaptureSpike;

public sealed record SessionState(
    uint SessionId,
    uint ConsoleSessionId,
    bool IsConsoleSession,
    bool IsRemoteSession,
    string WtsConnectState,
    bool InputDesktopAccessible)
{
    public override string ToString() =>
        $"session={SessionId} wts={WtsConnectState} console={(IsConsoleSession ? "yes" : "no")} " +
        $"remote={(IsRemoteSession ? "yes" : "no")} inputDesktop={(InputDesktopAccessible ? "yes" : "no")}";
}

/// <summary>Current Windows session state, sampled once per second during capture.</summary>
internal static class SessionProbe
{
    public static SessionState Current()
    {
        uint sessionId = 0;
        try
        {
            Win32.ProcessIdToSessionId(Win32.GetCurrentProcessId(), out sessionId);
        }
        catch { /* keep 0 */ }

        uint consoleId = 0xFFFFFFFF;
        try { consoleId = Win32.WTSGetActiveConsoleSessionId(); } catch { }

        string wtsState = "unknown";
        try
        {
            if (Win32.WTSQuerySessionInformationW(IntPtr.Zero, sessionId, Win32.WTS_CONNECT_STATE_CLASS,
                    out IntPtr buf, out uint bytes) && bytes >= sizeof(int))
            {
                wtsState = ((Win32.WtsConnectState)Marshal.ReadInt32(buf)).ToString();
            }
            if (buf != IntPtr.Zero)
                Win32.WTSFreeMemory(buf);
        }
        catch { }

        bool isRemote = false;
        try
        {
            if (Win32.WTSQuerySessionInformationW(IntPtr.Zero, sessionId, Win32.WTS_IS_REMOTE_SESSION,
                    out IntPtr buf, out uint bytes) && bytes >= 1)
            {
                isRemote = Marshal.ReadByte(buf) != 0;
            }
            if (buf != IntPtr.Zero)
                Win32.WTSFreeMemory(buf);
        }
        catch { }

        bool inputDesktop = false;
        IntPtr desk = IntPtr.Zero;
        try
        {
            desk = Win32.OpenInputDesktop(0, false, Win32.GENERIC_READ);
            inputDesktop = desk != IntPtr.Zero;
        }
        catch { }
        finally
        {
            if (desk != IntPtr.Zero)
                Win32.CloseDesktop(desk);
        }

        return new SessionState(
            sessionId,
            consoleId,
            sessionId == consoleId,
            isRemote,
            wtsState,
            inputDesktop);
    }
}
