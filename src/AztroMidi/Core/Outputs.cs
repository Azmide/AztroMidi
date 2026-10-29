using System.Runtime.InteropServices;

namespace AztroMidi.Core;

public enum OutputMode
{
    /// <summary>Real key presses (SendInput). Roblox must be the focused window.</summary>
    Keyboard,
    /// <summary>Key messages posted straight to the Roblox window, works while other apps are focused.</summary>
    Background,
    /// <summary>Plays the notes on this PC's MIDI synth. Nothing is sent to Roblox.</summary>
    Preview,
}

/// <summary>Where the player's notes go.</summary>
public interface INoteOutput
{
    void NoteDown(int pitch, KeyStroke key, int velocity);
    void NoteUp(int pitch, KeyStroke key);
    void Velocity(KeyStroke velocityKey);
    void Sustain(bool down);
    void ReleaseModifiers();
}

/// <summary>Foreground key presses with scan codes.</summary>
public sealed class KeyboardOutput : INoteOutput
{
    public void NoteDown(int pitch, KeyStroke key, int velocity) => InputSender.Down(key.Vk, key.Mods);
    public void NoteUp(int pitch, KeyStroke key) => InputSender.Up(key.Vk);
    public void Velocity(KeyStroke k) => InputSender.Tap(k.Vk, k.Mods);

    public void Sustain(bool down)
    {
        if (down) InputSender.Down(InputSender.VK_SPACE, Mods.None);
        else InputSender.Up(InputSender.VK_SPACE);
    }

    public void ReleaseModifiers() => InputSender.ReleaseModifiers();
}

/// <summary>
/// Posts WM_KEYDOWN / WM_KEYUP to the Roblox window so it keeps receiving notes
/// while you use other apps. Experimental: works as long as the game accepts
/// window messages without focus.
/// </summary>
public sealed class BackgroundOutput : INoteOutput
{
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint MapVirtualKey(uint code, uint type);

    private const uint WM_KEYDOWN = 0x0100, WM_KEYUP = 0x0101;
    private const uint WM_SYSKEYDOWN = 0x0104, WM_SYSKEYUP = 0x0105;

    private static IntPtr Target => WindowGuard.FindRobloxWindow();

    private static IntPtr LParam(ushort vk, bool up)
    {
        uint scan = MapVirtualKey(vk, 0);
        uint l = 1 | (scan << 16);
        if (up) l |= 0xC0000000;
        return (IntPtr)(int)l;
    }

    private static void Post(uint msg, ushort vk, bool up)
    {
        var hwnd = Target;
        if (hwnd != IntPtr.Zero) PostMessage(hwnd, msg, (IntPtr)vk, LParam(vk, up));
    }

    private static void ModsDown(Mods m)
    {
        if (m.HasFlag(Mods.Ctrl)) Post(WM_KEYDOWN, 0x11, false);
        if (m.HasFlag(Mods.Alt)) Post(WM_SYSKEYDOWN, 0x12, false);
        if (m.HasFlag(Mods.Shift)) Post(WM_KEYDOWN, 0x10, false);
    }

    private static void ModsUp(Mods m)
    {
        if (m.HasFlag(Mods.Shift)) Post(WM_KEYUP, 0x10, true);
        if (m.HasFlag(Mods.Alt)) Post(WM_SYSKEYUP, 0x12, true);
        if (m.HasFlag(Mods.Ctrl)) Post(WM_KEYUP, 0x11, true);
    }

    public void NoteDown(int pitch, KeyStroke key, int velocity)
    {
        ModsDown(key.Mods);
        Post(WM_KEYDOWN, key.Vk, false);
        ModsUp(key.Mods);
    }

    public void NoteUp(int pitch, KeyStroke key) => Post(WM_KEYUP, key.Vk, true);

    public void Velocity(KeyStroke k)
    {
        ModsDown(k.Mods);
        Post(WM_SYSKEYDOWN, k.Vk, false);
        Post(WM_SYSKEYUP, k.Vk, true);
        ModsUp(k.Mods);
    }

    public void Sustain(bool down) => Post(down ? WM_KEYDOWN : WM_KEYUP, InputSender.VK_SPACE, !down);

    public void ReleaseModifiers() => ModsUp(Mods.Shift | Mods.Ctrl | Mods.Alt);
}

/// <summary>Plays notes through the Windows MIDI synth so songs can be checked without Roblox.</summary>
public sealed class PreviewOutput : INoteOutput, IDisposable
{
    [DllImport("winmm.dll")] private static extern int midiOutOpen(out IntPtr h, int deviceId, IntPtr cb, IntPtr inst, int flags);
    [DllImport("winmm.dll")] private static extern int midiOutShortMsg(IntPtr h, int msg);
    [DllImport("winmm.dll")] private static extern int midiOutReset(IntPtr h);
    [DllImport("winmm.dll")] private static extern int midiOutClose(IntPtr h);

    private IntPtr _handle;
    private bool _opened;

    private IntPtr Handle
    {
        get
        {
            if (!_opened)
            {
                _opened = true;
                if (midiOutOpen(out _handle, -1, IntPtr.Zero, IntPtr.Zero, 0) != 0) _handle = IntPtr.Zero;
                else midiOutShortMsg(_handle, 0xC0); // acoustic grand piano
            }
            return _handle;
        }
    }

    private void Send(int status, int d1, int d2)
    {
        var h = Handle;
        if (h != IntPtr.Zero) midiOutShortMsg(h, status | (d1 << 8) | (d2 << 16));
    }

    public void NoteDown(int pitch, KeyStroke key, int velocity) => Send(0x90, pitch, Math.Clamp(velocity, 1, 127));
    public void NoteUp(int pitch, KeyStroke key) => Send(0x80, pitch, 0);
    public void Velocity(KeyStroke velocityKey) { }
    public void Sustain(bool down) => Send(0xB0, 64, down ? 127 : 0);
    public void ReleaseModifiers() { }

    public void Dispose()
    {
        if (_handle == IntPtr.Zero) return;
        midiOutReset(_handle);
        midiOutClose(_handle);
        _handle = IntPtr.Zero;
        _opened = false;
    }
}
