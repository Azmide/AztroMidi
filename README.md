<div align="center">

# AztroMidi

**A lightweight MIDI auto-player for Roblox pianos, with playback that sounds human.**

Windows 10/11 · C# / WPF · ~350 KB · no dependencies

</div>

---

## Features

**Playback**
- Plays any `.mid` / `.midi` file on Roblox virtual pianos (standard 61-key layout, C2–C7)
- **88-key mode** that uses the common Ctrl extension for notes below C2 and above C7
- High-precision timer (sub-millisecond scheduling) that follows tempo changes in the MIDI
- Speed control from 0.25× to 2×, seek bar, play / pause / stop, previous / next song
- **Auto transpose** picks the key that fits the most notes on the piano
- Out-of-range notes can be folded into range by octave, or skipped
- Optional **sustain pedal** (holds Space with the MIDI pedal)
- Optional **velocity keys**: Alt + key, 32 levels, for pianos that support loudness
- Track list with enable / disable, **Solo**, and drum tracks skipped automatically
- Loop song and auto-play next

**Humanize** (so it sounds played, not robotic)
| Setting | What it does |
|---|---|
| Timing looseness | Each note lands slightly before or after the beat (bell-shaped, not uniform noise) |
| Chord roll | Chords are rolled like fingers, mostly upward, with a different spread each time |
| Tempo drift | Slow, smooth speeding up and slowing down across the song |
| Key hold variation | Keys are held a little longer or shorter each time |
| Phrase breathing | Tiny pauses after rests, like lifting your hands |
| Velocity variation | Random loudness changes (with velocity keys on) |
| Wrong-note slips | Optional: now and then a neighbouring key is brushed right before the real note |

Presets: **Off, Subtle, Natural, Expressive, Sloppy**.

**Quality of life**
- Song library with search, subfolders, and drag & drop of files or folders
- Live piano visualizer that shows which key is pressed and its keyboard label
- Global hotkeys that work inside Roblox (configurable)
- **Only play while Roblox is focused**, so keys never end up typed into Discord or your browser
- Countdown before playing, always-on-top mode
- Portable: settings are saved next to the exe in `settings.json`

## Default hotkeys

| Key | Action |
|---|---|
| F1 | Play / Pause |
| F2 | Stop |
| F3 / F4 | Slow down / Speed up |
| F5 | Next song |
| F6 | Toggle humanize |

## Getting started

1. Download `AztroMidi.exe` from Releases (or build it, see below).
2. Put your MIDI files in a folder, then click the folder icon in **Library** and pick it.
   You can also create a `songs` folder next to the exe; it is loaded automatically.
3. Pick a song, join a Roblox game with a piano and sit at it.
4. Click into Roblox and press **F1**.

> Roblox pianos use Shift for black keys. If Shift Lock is on in the game, turn it off
> in Roblox settings or some notes may not play.

## Building from source

Requires the [.NET 10 SDK](https://dotnet.microsoft.com/download).

```powershell
.\build.ps1                 # small exe (needs the .NET 10 Desktop Runtime)
.\build.ps1 -SelfContained  # larger exe that runs on any Windows x64 PC
```

The output goes to `dist\AztroMidi.exe`. For development, run `dotnet run --project src/AztroMidi`.

## Project layout

```
src/AztroMidi/
├── Core/
│   ├── MidiFile.cs        Standard MIDI File parser with tempo map
│   ├── KeyMapper.cs       Roblox 61 / 88-key layout, transpose and velocity keys
│   ├── Humanizer.cs       Human-like timing, chord rolls, tempo drift, slips
│   ├── Player.cs          High-precision playback thread
│   ├── InputSender.cs     Scan-code keyboard input via SendInput
│   ├── HotkeyManager.cs   Global hotkeys
│   ├── WindowGuard.cs     Checks that Roblox is the focused window
│   └── AppSettings.cs     JSON settings
├── Controls/PianoView.cs  Piano visualizer
├── Themes/Theme.xaml      Dark / blue theme
└── MainWindow.xaml(.cs)   UI
```

## Credits

Inspired by [MIDI++](https://github.com/Zephkek/MIDIPlusPlus). AztroMidi is an independent
implementation written from scratch.

## License

[MIT](LICENSE)
