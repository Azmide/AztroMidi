using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AztroMidi.Core;

/// <summary>Finds the Roblox game window and checks whether it has focus.</summary>
public static class WindowGuard
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool IsIconic(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool GetWindowRect(IntPtr hWnd, out RECT rect);

    [StructLayout(LayoutKind.Sequential)]
    public struct RECT { public int Left, Top, Right, Bottom; }

    private static readonly object Gate = new();
    private static IntPtr _lastFg;
    private static bool _lastFgResult;
    private static IntPtr _roblox;
    private static long _robloxCheckedAt = -10_000;

    private static bool IsRobloxProcess(string name) =>
        name.StartsWith("RobloxPlayer", StringComparison.OrdinalIgnoreCase)
        || name.Equals("Windows10Universal", StringComparison.OrdinalIgnoreCase); // Microsoft Store build

    public static bool RobloxIsFocused()
    {
        lock (Gate)
        {
            var hwnd = GetForegroundWindow();
            if (hwnd == _lastFg) return _lastFgResult;
            _lastFg = hwnd;
            _lastFgResult = false;
            try
            {
                GetWindowThreadProcessId(hwnd, out var pid);
                using var p = Process.GetProcessById((int)pid);
                _lastFgResult = IsRobloxProcess(p.ProcessName);
                if (_lastFgResult) _roblox = hwnd;
            }
            catch { _lastFgResult = false; }
            return _lastFgResult;
        }
    }

    /// <summary>Main Roblox window handle, or zero when the game is not running. Rescans at most once a second.</summary>
    public static IntPtr FindRobloxWindow()
    {
        lock (Gate)
        {
            if (_roblox != IntPtr.Zero && IsWindow(_roblox)) return _roblox;
            long now = Environment.TickCount64;
            if (now - _robloxCheckedAt < 1000) return IntPtr.Zero;
            _robloxCheckedAt = now;
            _roblox = IntPtr.Zero;
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (!IsRobloxProcess(p.ProcessName)) continue;
                    var h = p.MainWindowHandle;
                    if (h != IntPtr.Zero && IsWindowVisible(h)) { _roblox = h; break; }
                }
                catch { /* process exited */ }
                finally { p.Dispose(); }
            }
            return _roblox;
        }
    }

    [DllImport("user32.dll")] private static extern bool SetForegroundWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool BringWindowToTop(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern bool ShowWindow(IntPtr hWnd, int cmd);
    [DllImport("user32.dll")] private static extern bool AttachThreadInput(uint idAttach, uint idAttachTo, bool attach);
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();

    /// <summary>
    /// Brings the Roblox window to the front and gives it keyboard focus.
    /// Returns true when Roblox ends up focused.
    /// </summary>
    public static bool FocusRoblox()
    {
        var h = FindRobloxWindow();
        if (h == IntPtr.Zero) return false;
        if (IsIconic(h)) ShowWindow(h, 9); // SW_RESTORE

        // Windows only lets the foreground thread hand focus to another window,
        // so briefly share input state with whoever is in front right now.
        var fg = GetForegroundWindow();
        uint fgThread = GetWindowThreadProcessId(fg, out _);
        uint me = GetCurrentThreadId();
        bool attached = fgThread != 0 && fgThread != me && AttachThreadInput(me, fgThread, true);
        try
        {
            BringWindowToTop(h);
            SetForegroundWindow(h);
        }
        finally
        {
            if (attached) AttachThreadInput(me, fgThread, false);
        }
        return GetForegroundWindow() == h;
    }

    [DllImport("user32.dll")] private static extern bool GetClientRect(IntPtr hWnd, out RECT rect);
    [DllImport("user32.dll")] private static extern bool ClientToScreen(IntPtr hWnd, ref POINT pt);

    [StructLayout(LayoutKind.Sequential)]
    private struct POINT { public int X, Y; }

    /// <summary>
    /// Screen rectangle (physical pixels) of the Roblox game area, excluding the
    /// title bar in windowed mode. Null when Roblox is missing or minimized.
    /// </summary>
    public static RECT? RobloxClientRect()
    {
        var h = FindRobloxWindow();
        if (h == IntPtr.Zero || IsIconic(h) || !GetClientRect(h, out var c)) return null;
        var origin = new POINT();
        if (!ClientToScreen(h, ref origin)) return null;
        return new RECT { Left = origin.X, Top = origin.Y, Right = origin.X + c.Right, Bottom = origin.Y + c.Bottom };
    }
}
