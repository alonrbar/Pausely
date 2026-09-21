using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Windows.Interop;

namespace Pausely.Services;

public sealed class WindowsSession : IDisposable
{
    private readonly HwndSource _source;
    public event Action<bool>? AwayChanged;
    public event Action<bool>? SuspensionChanged;
    public WindowsSession()
    {
        _source = new HwndSource(new HwndSourceParameters("Pausely.Session") { Width = 0, Height = 0, WindowStyle = 0 });
        _source.AddHook(WindowProc);
        if (!WTSRegisterSessionNotification(_source.Handle, 0))
        {
            var error = Marshal.GetLastWin32Error();
            _source.Dispose();
            throw new Win32Exception(error, "Pausely couldn't subscribe to Windows lock/unlock events. Please reopen it after signing in.");
        }
    }

    private nint WindowProc(nint hwnd, int message, nint wParam, nint lParam, ref bool handled)
    {
        // Process power broadcasts synchronously on the UI thread before Windows sleeps.
        if (message == 0x0218)
        {
            if ((int)wParam == 4) SuspensionChanged?.Invoke(true);
            else if ((int)wParam is 7 or 18) SuspensionChanged?.Invoke(false);
        }
        if (message == 0x02B1)
        {
            switch ((int)wParam)
            {
                case 0x7: // Lock
                case 0x2: // Console disconnected
                case 0x4: // Remote disconnected
                    AwayChanged?.Invoke(true); break;
                case 0x8: // Unlock
                    AwayChanged?.Invoke(false); break;
                case 0x1: // Console connected
                case 0x3: // Remote connected
                    AwayChanged?.Invoke(IsAway()); break;
            }
        }
        return 0;
    }

    public static bool IsAway()
    {
        // WTSINFOEX: DWORD Level, 4 bytes alignment, then the level-1 union.
        // The union begins with SessionId, SessionState, SessionFlags (DWORD each).
        if (!WTSQuerySessionInformation(0, -1, 25, out var buffer, out var size))
            return true; // Do not start locking an unverified session.
        try
        {
            if (size < 20 || Marshal.ReadInt32(buffer) != 1) return true;
            var connectionState = Marshal.ReadInt32(buffer, 12);
            var flags = Marshal.ReadInt32(buffer, 16);
            return connectionState != 0 || flags != 1;
        }
        finally { WTSFreeMemory(buffer); }
    }

    public static string? TryLock()
    {
        if (LockWorkStation()) return null;
        return $"Windows couldn't lock your screen: {new Win32Exception(Marshal.GetLastWin32Error()).Message}";
    }
    public void Dispose()
    {
        WTSUnRegisterSessionNotification(_source.Handle);
        _source.RemoveHook(WindowProc);
        _source.Dispose();
    }

    [DllImport("user32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool LockWorkStation();
    [DllImport("wtsapi32.dll", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSRegisterSessionNotification(nint window, uint flags);
    [DllImport("wtsapi32.dll")] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSUnRegisterSessionNotification(nint window);
    [DllImport("wtsapi32.dll", EntryPoint = "WTSQuerySessionInformationW", SetLastError = true)] [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool WTSQuerySessionInformation(nint server, int session, int infoClass, out nint buffer, out int bytes);
    [DllImport("wtsapi32.dll")] private static extern void WTSFreeMemory(nint buffer);
}
