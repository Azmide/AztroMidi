using System.Runtime.InteropServices;

namespace AztroMidi.Core;

public enum OutputMode
{
    // Real key presses (SendInput). Roblox must be the focused window.
    Keyboard,
    // Legacy value from v1.1-1.2. Roblox ignores input sent to an unfocused window, so this is
    // treated as Keyboard. Kept so older settings files still load.
    Background,
    // Plays the notes on this PC's MIDI synth. Nothing is sent to Roblox.
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
