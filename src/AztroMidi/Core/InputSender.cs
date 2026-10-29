using System.Runtime.InteropServices;

namespace AztroMidi.Core;

[Flags]
public enum Mods { None = 0, Shift = 1, Ctrl = 2, Alt = 4 }

/// <summary>
/// Sends hardware-like key events with scan codes. Roblox reads raw input, so
/// plain virtual-key messages are not enough; scan codes are.
/// </summary>
public static class InputSender
{
    private const uint INPUT_KEYBOARD = 1;
    private const uint KEYEVENTF_KEYUP = 0x0002;
    private const uint KEYEVENTF_SCANCODE = 0x0008;
    private const uint MAPVK_VK_TO_VSC = 0;

    public const ushort VK_SHIFT = 0xA0;   // left shift
    public const ushort VK_CONTROL = 0xA2; // left ctrl
    public const ushort VK_MENU = 0xA4;    // left alt
    public const ushort VK_SPACE = 0x20;

    [StructLayout(LayoutKind.Sequential)]
    private struct KEYBDINPUT
    {
        public ushort wVk;
        public ushort wScan;
        public uint dwFlags;
        public uint time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct MOUSEINPUT
    {
        public int dx, dy;
        public uint mouseData, dwFlags, time;
        public IntPtr dwExtraInfo;
    }

    [StructLayout(LayoutKind.Explicit)]
    private struct InputUnion
    {
        [FieldOffset(0)] public MOUSEINPUT mi;
        [FieldOffset(0)] public KEYBDINPUT ki;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct INPUT
    {
        public uint type;
        public InputUnion u;
    }

    [DllImport("user32.dll", SetLastError = true)]
    private static extern uint SendInput(uint nInputs, INPUT[] pInputs, int cbSize);

    [DllImport("user32.dll")]
    private static extern uint MapVirtualKey(uint uCode, uint uMapType);

    [DllImport("user32.dll")]
    private static extern short VkKeyScan(char ch);

    private static readonly Dictionary<ushort, ushort> ScanCache = new();
    private static readonly object Gate = new();

    /// <summary>Virtual-key code for an unshifted character such as 'a' or '1'.</summary>
    public static ushort VkFromChar(char c) => (ushort)(VkKeyScan(char.ToLowerInvariant(c)) & 0xFF);

    private static ushort Scan(ushort vk)
    {
        lock (ScanCache)
        {
            if (!ScanCache.TryGetValue(vk, out var sc))
                ScanCache[vk] = sc = (ushort)MapVirtualKey(vk, MAPVK_VK_TO_VSC);
            return sc;
        }
    }

    private static INPUT Make(ushort vk, bool up) => new()
    {
        type = INPUT_KEYBOARD,
        u = new InputUnion
        {
            ki = new KEYBDINPUT
            {
                wVk = 0,
                wScan = Scan(vk),
                dwFlags = KEYEVENTF_SCANCODE | (up ? KEYEVENTF_KEYUP : 0),
            },
        },
    };

    private static void Send(List<INPUT> list)
    {
        if (list.Count == 0) return;
        lock (Gate) SendInput((uint)list.Count, list.ToArray(), Marshal.SizeOf<INPUT>());
    }

    private static void AddMods(List<INPUT> list, Mods mods, bool up)
    {
        if (mods.HasFlag(Mods.Ctrl)) list.Add(Make(VK_CONTROL, up));
        if (mods.HasFlag(Mods.Alt)) list.Add(Make(VK_MENU, up));
        if (mods.HasFlag(Mods.Shift)) list.Add(Make(VK_SHIFT, up));
    }

    /// <summary>Presses a key with modifiers held only for the moment of the press.</summary>
    public static void Down(ushort vk, Mods mods)
    {
        var list = new List<INPUT>(8);
        AddMods(list, mods, up: false);
        list.Add(Make(vk, up: false));
        AddMods(list, mods, up: true);
        Send(list);
    }

    public static void Up(ushort vk)
    {
        Send(new List<INPUT> { Make(vk, up: true) });
    }

    /// <summary>Quick press and release, e.g. for velocity selection keys.</summary>
    public static void Tap(ushort vk, Mods mods)
    {
        var list = new List<INPUT>(8);
        AddMods(list, mods, up: false);
        list.Add(Make(vk, up: false));
        list.Add(Make(vk, up: true));
        AddMods(list, mods, up: true);
        Send(list);
    }

    public static void ReleaseModifiers()
    {
        Send(new List<INPUT> { Make(VK_SHIFT, true), Make(VK_CONTROL, true), Make(VK_MENU, true) });
    }
}
