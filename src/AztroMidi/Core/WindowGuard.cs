using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AztroMidi.Core;

/// <summary>Checks whether the foreground window belongs to Roblox.</summary>
public static class WindowGuard
{
    [DllImport("user32.dll")] private static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);

    private static IntPtr _lastHwnd;
    private static bool _lastResult;

    public static bool RobloxIsFocused()
    {
        var hwnd = GetForegroundWindow();
        if (hwnd == _lastHwnd) return _lastResult;
        _lastHwnd = hwnd;
        _lastResult = false;
        try
        {
            GetWindowThreadProcessId(hwnd, out var pid);
            using var p = Process.GetProcessById((int)pid);
            var name = p.ProcessName;
            _lastResult = name.StartsWith("RobloxPlayer", StringComparison.OrdinalIgnoreCase)
                       || name.Equals("Windows10Universal", StringComparison.OrdinalIgnoreCase); // Microsoft Store build
        }
        catch { _lastResult = false; }
        return _lastResult;
    }
}
