# Contributing to AztroMidi

Thanks for wanting to help! Bug reports, ideas and pull requests are all welcome.

## Getting started

You need **Windows 10/11** and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
git clone https://github.com/Azmide/AztroMidi.git
cd AztroMidi
dotnet run --project src/AztroMidi            # start the app
dotnet run --project tests/AztroMidi.Tests    # run the tests
```

You can also open `AztroMidi.sln` in Visual Studio 2022+ or JetBrains Rider.

> Tip: switch **Send to** to **Preview** while developing. Notes then play through your
> speakers instead of being typed as key presses, so nothing ends up in your editor.

## Project structure

| Path | What lives there |
|---|---|
| `src/AztroMidi/Core/` | The engine: MIDI parsing, key mapping, humanizer, player, input. No UI code. |
| `src/AztroMidi/Controls/` | Custom WPF controls (the piano visualizer) |
| `src/AztroMidi/Themes/Theme.xaml` | Colors, fonts and control styles |
| `src/AztroMidi/*.xaml` | Windows: the main window and the mini player |
| `tests/AztroMidi.Tests/` | Dependency-free tests for `Core` |
| `tools/` | Helper scripts (e.g. icon generator) |
| `docs/` | Images and documentation |

## Guidelines

- **Keep it light.** The app has zero NuGet dependencies and is ~400 KB. Please do not add a
  package unless there is no reasonable alternative.
- **Engine changes need tests.** Add a check to `tests/AztroMidi.Tests/Program.cs`.
- **Match the existing style.** `.editorconfig` covers formatting; private fields are
  `_camelCase`, file-scoped namespaces, comments explain *why*, not *what*.
- **UI colors come from `Theme.xaml`.** Use the existing brushes (`Brand`, `Surface`, `Ink`...)
  instead of hard-coded colors.
- **No game exploits.** AztroMidi only sends normal key presses. Changes that inject into
  Roblox, read its memory or bypass its anti-cheat will not be accepted.

## Pull requests

1. Fork the repo and create a branch: `git checkout -b feature/my-idea`
2. Make your change and run the tests.
3. Add a line under **Unreleased** in `CHANGELOG.md`.
4. Open a pull request and fill in the template.

CI builds the app and runs the tests on every pull request.

## Releasing (maintainers)

1. Move the **Unreleased** notes in `CHANGELOG.md` under a new version heading, e.g. `## [1.4.0] - 2026-11-15`.
2. Update `<Version>` in `src/AztroMidi/AztroMidi.csproj`.
3. Commit, then tag and push:
   ```powershell
   git tag v1.4.0
   git push origin main --tags
   ```
4. The **Release** workflow builds both downloads and publishes the GitHub Release
   with the notes from `CHANGELOG.md`.
