using System.IO;
using AztroMidi.Core;
using AztroMidi.Tests;

// Tiny test runner: every Check prints PASS/FAIL, exit code = number of failures.
// Nothing here sends key presses; the player is only loaded, never started.

int fails = 0;
void Check(bool ok, string what)
{
    Console.ForegroundColor = ok ? ConsoleColor.Green : ConsoleColor.Red;
    Console.Write(ok ? "PASS  " : "FAIL  ");
    Console.ResetColor();
    Console.WriteLine(what);
    if (!ok) fails++;
}

Console.WriteLine("== Key mapping");
Check(KeyMapper.Layout61.Length == 61, "61-key layout has 61 keys");
Check(KeyMapper.Lookup(36) is { Label: '1', Mods: Mods.None }, "C2 -> 1");
Check(KeyMapper.Lookup(37) is { Label: '!', Mods: Mods.Shift, Vk: 0x31 }, "C#2 -> Shift+1");
Check(KeyMapper.Lookup(60)?.Label == 't', "C4 (middle C) -> t");
Check(KeyMapper.Lookup(61) is { Label: 'T', Mods: Mods.Shift }, "C#4 -> Shift+T");
Check(KeyMapper.Lookup(96)?.Label == 'm', "C7 -> m");
Check(KeyMapper.Lookup(21) is { Label: '1', Mods: Mods.Ctrl }, "A0 -> Ctrl+1");
Check(KeyMapper.Lookup(108) is { Label: 'j', Mods: Mods.Ctrl }, "C8 -> Ctrl+j");
Check(Enumerable.Range(21, 88).All(p => KeyMapper.Lookup(p) != null), "all 88 keys mapped");
Check(KeyMapper.Fit(24, KeyLayout.Keys61, OutOfRange.Fold) == 36, "C1 folds to C2 on 61 keys");
Check(KeyMapper.Fit(24, KeyLayout.Keys88, OutOfRange.Fold) == 24, "C1 stays on 88 keys");
Check(KeyMapper.Fit(12, KeyLayout.Keys88, OutOfRange.Fold) == 24, "C0 folds to C1 on 88 keys");
Check(KeyMapper.Fit(24, KeyLayout.Keys61, OutOfRange.Skip) == null, "skip mode drops C1");
Check(KeyMapper.VelocityKey(127).Label == 'c' && KeyMapper.VelocityKey(0).Label == '1', "velocity keys");
int bt = KeyMapper.BestTranspose(new[] { 61, 63, 66, 68, 70 }, KeyLayout.Keys61);
Check(Math.Abs(bt) <= 6, $"auto transpose picks a small shift ({bt})");

Console.WriteLine("== MIDI parsing");
var path = TestMidi.WriteSample();
try
{
    var midi = MidiFile.Load(path);
    Check(midi.Tracks.Count == 3, "3 tracks");
    Check(midi.Tracks[1].Name == "Piano", "track name read");
    Check(midi.Tracks[2].IsDrums, "drum track detected");
    Check(midi.Notes.Count == 6, $"6 notes parsed (got {midi.Notes.Count})");
    var c5 = midi.Notes.First(n => n.Pitch == 72);
    Check(Math.Abs(c5.Start - 1.0) < 1e-6 && Math.Abs(c5.End - 2.0) < 1e-6, $"tempo map: C5 at 1.0-2.0 s (got {c5.Start:0.###}-{c5.End:0.###})");
    var c1 = midi.Notes.First(n => n.Pitch == 24);
    Check(Math.Abs(c1.Start - 2.0) < 1e-6 && Math.Abs(c1.End - 3.0) < 1e-6, $"60 bpm section: 2.0-3.0 s (got {c1.Start:0.###}-{c1.End:0.###})");
    Check(midi.Sustain.Count == 2, "sustain pedal events parsed");
    Check(midi.InitialBpm == 120, "initial tempo 120 bpm");

    Console.WriteLine("== Schedule");
    var settings = new AppSettings { AutoTranspose = false, Sustain = SustainMode.FollowPedal, Output = OutputMode.Preview };
    settings.Humanize.Enabled = false;
    using var player = new Player(settings);
    player.Load(midi);
    Check(!player.TrackEnabled[2], "drum track disabled by default");
    Check(player.EventCount == 5 * 2 + 2, $"5 notes x (down+up) + 2 pedal events (got {player.EventCount})");

    settings.Humanize.Enabled = true;
    player.Rebuild();
    Check(player.EventCount == 12, "humanized schedule keeps every event");
    Check(player.Length > 2.9 && player.Length < 3.3, $"humanized length stays near 3 s ({player.Length:0.###})");

    var next = player.Upcoming(3);
    Check(next.Count == 3 && next[0].StartsWith('['), $"upcoming keys start with the chord ({string.Join(" ", next)})");
}
finally
{
    File.Delete(path);
}

Console.WriteLine("== MIDI validation");
void RejectMidi(byte[] body, string what, int division = 480, int format = 0)
{
    var invalidPath = TestMidi.WriteRaw(body, division, format);
    try
    {
        try { MidiFile.Load(invalidPath); Check(false, what); }
        catch (InvalidDataException) { Check(true, what); }
    }
    finally { File.Delete(invalidPath); }
}
RejectMidi(new byte[] { 0, 0x90, 60 }, "reject truncated note event");
RejectMidi(new byte[] { 0, 0xFF }, "reject truncated meta event");
RejectMidi(new byte[] { 0, 0xFF, 3, 5, 65 }, "reject meta payload beyond track");
RejectMidi(new byte[] { 0, 0xF0, 5, 1 }, "reject SysEx payload beyond track");
RejectMidi(new byte[] { 0x81 }, "reject truncated delta time");
RejectMidi(new byte[] { 0x81, 0x80, 0x80, 0x80, 0 }, "reject oversized variable-length value");
RejectMidi(new byte[] { 0, 60, 100 }, "reject running status without channel event");
RejectMidi(new byte[] { 0, 0x90, 60, 0xFF }, "reject status byte inside note data");
RejectMidi(new byte[] { 0, 0xF1, 0 }, "reject unsupported system status");
RejectMidi(new byte[] { 0, 0xFF, 0x2F, 0 }, "reject zero PPQN division", division: 0);
RejectMidi(new byte[] { 0, 0xFF, 0x2F, 0 }, "reject zero SMPTE ticks per frame", division: 0xE300);
RejectMidi(new byte[] { 0, 0xFF, 0x2F, 0 }, "reject unsupported SMPTE frame rate", division: 0xE601);
RejectMidi(new byte[] { 0, 0xFF, 0x2F, 0 }, "reject asynchronous format 2 tracks", format: 2);
var boundaryPath = TestMidi.WriteRaw(new byte[] { 0, 0x90, 60 });
try
{
    // A following chunk must never supply the missing velocity byte.
    using (var stream = File.Open(boundaryPath, FileMode.Append))
        stream.Write(new byte[] { 77, 84, 114, 107, 0, 0, 0, 4, 0, 0xFF, 0x2F, 0 });
    try { MidiFile.Load(boundaryPath); Check(false, "note data cannot cross track boundary"); }
    catch (InvalidDataException) { Check(true, "note data cannot cross track boundary"); }
}
finally { File.Delete(boundaryPath); }
var truncatedPath = TestMidi.WriteSample();
try
{
    var bytes = File.ReadAllBytes(truncatedPath);
    bool rejected = true;
    for (int length = 4; length < bytes.Length; length++)
    {
        File.WriteAllBytes(truncatedPath, bytes[..length]);
        try { MidiFile.Load(truncatedPath); rejected = false; }
        catch (InvalidDataException) { }
    }
    Check(rejected, "reject every truncated prefix of a valid multi-track file");
}
finally { File.Delete(truncatedPath); }

var smptePath = TestMidi.WriteRaw(new byte[] { 0, 0x90, 60, 100, 0x87, 0x68, 0x80, 60, 0 }, 0xE364);
try
{
    var smpte = MidiFile.Load(smptePath);
    Check(Math.Abs(smpte.Length - 1001.0 / 3000) < 1e-9, "SMPTE -29 uses 29.97 fps timing");
}
finally { File.Delete(smptePath); }

Console.WriteLine("== Humanizer");
var h = new Humanizer(new HumanizeSettings { TimingJitterMs = 10 }, seed: 1);
var offsets = Enumerable.Range(0, 5000).Select(_ => h.TimingOffset() * 1000).ToList();
Check(offsets.All(o => Math.Abs(o) <= 10.0001), "timing jitter clamped to ±10 ms");
Check(Math.Abs(offsets.Average()) < 0.5, $"timing jitter centered (mean {offsets.Average():0.00} ms)");
Check(h.ChordRoll(4).All(r => r >= 0 && r <= 0.03), "chord roll within limits");
Check(Enumerable.Range(0, 200).Select(t => h.TempoFactor(t)).All(f => f > 0.97 && f < 1.03), "tempo drift within ±2.5%");

Console.WriteLine(fails == 0 ? "\nALL PASSED" : $"\n{fails} FAILED");
return fails;
