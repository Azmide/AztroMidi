# Changelog

All notable changes to AztroMidi are listed here.
The format follows [Keep a Changelog](https://keepachangelog.com/en/1.1.0/) and the project uses [Semantic Versioning](https://semver.org/).

## [Unreleased]

### Added
- Drag & drop MIDI files or folders onto the window to move them into your library folder
  (or copy, or only add to the list; see Settings → Dropped MIDI files).
- Open `.mid` files from Explorer with AztroMidi; if it is already running, the files go to that window.
- Right-click menu on songs: play, favorite, show in folder, copy path, remove from list.
- Song lengths in the library.
- Keyboard shortcuts: Space, arrow keys, +/−, Enter, Delete, Ctrl+F, Ctrl+O, Ctrl+1/2/3, Ctrl+M.
- The playing song is shown in the title bar and the taskbar.
- Window size, position and the last page are remembered.
- Releases include `SHA256SUMS.txt`, and the release notes explain the Windows SmartScreen prompt.

### Changed
- New desktop look: sidebar navigation, slimmer title bar with standard window buttons,
  Windows' own font, flatter panels and smaller corners.
- Buttons and icons have accessible names for screen readers.

### Fixed
- Reject truncated MIDI events and invalid chunk lengths without reading into another track.
- Reject invalid time divisions and asynchronous format 2 files instead of playing incorrect timing.
- Correct SMPTE 29.97 fps timing for MIDI files using drop-frame time division.
- Clicking another song while one is playing no longer stops the music; it only selects it.

## [1.3.1] - 2026-10-29

### Changed
- The title bar now behaves like a normal Windows title bar: drag a maximized window down to restore it,
  double-click to maximize, snap to screen edges, and maximizing no longer covers the taskbar.

### Fixed
- The Pause button in AztroMidi did not pause in Roblox mode; it jumped back to Roblox instead.
  Only the F1 hotkey jumps back now, buttons always pause.
- The mini player could shrink to 2 px wide and seem to disappear after F7, especially after it had been dragged.
- Hotkeys (F1, F7...) stopped working when AztroMidi was opened twice. Starting it again now
  brings the running window back instead of opening a second copy.

## [1.3.0] - 2026-10-29

### Added
- Pressing Play in AztroMidi switches to the Roblox window automatically.
- When playback is waiting because another app is focused, F1 or Play jumps back to Roblox and continues.
- Album tile in Now Playing, song icons in the library (the current song is highlighted), empty state for tracks.
- Test project, CI and automatic GitHub Releases.

### Changed
- More compact Quick controls.

### Removed
- Background mode. Roblox ignores keyboard input sent to a window that is not active, so it could not work reliably.

## [1.2.0] - 2026-10-29

### Added
- Experimental background mode test button (removed again in 1.3.0).

### Fixed
- The last song is remembered immediately, even if the app is closed unexpectedly.

## [1.1.0] - 2026-10-29

### Added
- Mini player that floats in the top-right corner of the Roblox window and never steals focus.
- Preview mode to listen to a song on your PC.
- Upcoming keys in virtual piano sheet style, e.g. `t [tuo] y`.
- Favorites and reopening the last song on start.
- Loop and auto-next toggles in the transport bar.

### Fixed
- Window now fits 1366x768 screens.

## [1.0.0] - 2026-10-29

### Added
- First release: Roblox 61/88-key layouts, humanized playback, tracks with solo, speed, transpose,
  sustain pedal, velocity keys, global hotkeys and a song library.
