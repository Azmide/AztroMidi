using System.Runtime.InteropServices;
using System.Windows.Input;
using System.Windows.Interop;

namespace AztroMidi.Core;

/// <summary>System-wide hotkeys so playback can be controlled while Roblox has focus.</summary>
public sealed class HotkeyManager : IDisposable
{
    [DllImport("user32.dll")] private static extern bool RegisterHotKey(IntPtr hWnd, int id, uint mods, uint vk);
    [DllImport("user32.dll")] private static extern bool UnregisterHotKey(IntPtr hWnd, int id);

    private const int WM_HOTKEY = 0x0312;
    private const uint MOD_NOREPEAT = 0x4000;

    private readonly HwndSource _source;
    private readonly Dictionary<int, Action> _actions = new();
    private int _nextId = 0xA270;

    public HotkeyManager(IntPtr hwnd)
    {
        _source = HwndSource.FromHwnd(hwnd);
        _source.AddHook(Hook);
    }

    public static readonly string[] Choices =
        { "None", "F1", "F2", "F3", "F4", "F5", "F6", "F7", "F8", "F9", "F10", "F11", "F12",
          "Insert", "Home", "End", "PageUp", "PageDown", "Pause" };

    /// <summary>Registers a key by name (e.g. "F1"). Returns false if another app owns it.</summary>
    public bool Register(string keyName, Action action)
    {
        if (string.IsNullOrEmpty(keyName) || keyName == "None") return true;
        if (!Enum.TryParse<Key>(keyName, true, out var key)) return false;
        int vk = KeyInterop.VirtualKeyFromKey(key);
        int id = _nextId++;
        if (!RegisterHotKey(_source.Handle, id, MOD_NOREPEAT, (uint)vk)) return false;
        _actions[id] = action;
        return true;
    }

    public void Clear()
    {
        foreach (var id in _actions.Keys) UnregisterHotKey(_source.Handle, id);
        _actions.Clear();
    }

    private IntPtr Hook(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if (msg == WM_HOTKEY && _actions.TryGetValue(wParam.ToInt32(), out var action))
        {
            action();
            handled = true;
        }
        return IntPtr.Zero;
    }

    public void Dispose()
    {
        Clear();
        _source.RemoveHook(Hook);
    }
}
