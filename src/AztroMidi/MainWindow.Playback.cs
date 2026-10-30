using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AztroMidi.Core;
using Microsoft.Win32;

namespace AztroMidi;

public partial class MainWindow
{
    #region Tracks

    private void BuildTrackList(MidiFile midi)
    {
        _tracks = midi.Tracks
            .Where(t => t.NoteCount > 0)
            .Select(t => new TrackItem
            {
                Index = t.Index,
                Name = t.DisplayName,
                Detail = $"{t.NoteCount:N0} notes · ch {string.Join(",", t.Channels.Order().Select(c => c + 1))}" + (t.IsDrums ? " · drums" : ""),
            })
            .ToList();
        foreach (var t in _tracks)
        {
            t.SetQuiet(_player.TrackEnabled[t.Index]);
            t.Changed += OnTrackToggled;
        }
        TrackList.ItemsSource = _tracks;
        TracksEmpty.Visibility = _tracks.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnTrackToggled(TrackItem item)
    {
        _player.TrackEnabled[item.Index] = item.Enabled;
        _player.Rebuild();
    }

    private void SetAllTracks(Func<TrackItem, bool> enabled)
    {
        foreach (var t in _tracks)
        {
            bool on = enabled(t);
            t.SetQuiet(on);
            _player.TrackEnabled[t.Index] = on;
        }
        _player.Rebuild();
    }

    private void OnTracksAll(object sender, RoutedEventArgs e) => SetAllTracks(_ => true);
    private void OnTracksNone(object sender, RoutedEventArgs e) => SetAllTracks(_ => false);

    private void OnSolo(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not TrackItem solo) return;
        bool alreadySolo = solo.Enabled && _tracks.All(t => t == solo || !t.Enabled);
        SetAllTracks(t => alreadySolo || t == solo);
    }

    #endregion

    #region Transport

    private void OnPlayPause(object sender, RoutedEventArgs e) => TogglePlay();

    // Play/pause. fromHotkey is true for the global hotkey: if the song is waiting because another
    // app has focus, the hotkey jumps back to Roblox instead of pausing. Buttons always pause
    // (clicking AztroMidi itself takes focus away from Roblox, so the song would otherwise never be
    // pausable from here).
    public void TogglePlay() => TogglePlay(fromHotkey: false);

    private void TogglePlay(bool fromHotkey)
    {
        if (_player.Song == null)
        {
            if (_songs.Count > 0) SelectSong((SongList.SelectedItem as SongEntry) ?? VisibleSongs().FirstOrDefault() ?? _songs[0]);
            if (_player.Song == null) return;
        }
        if (fromHotkey && _player.State == PlayState.Playing && _player.WaitingForFocus
            && WindowGuard.FindRobloxWindow() != IntPtr.Zero && WindowGuard.FocusRoblox())
        {
            ShowHint("Back in Roblox. Playback continues.");
            return;
        }

        _player.TogglePlay();

        // In Roblox mode, pressing Play from this app jumps straight into the game.
        if (IsPlaying() && _settings.Output == OutputMode.Keyboard && _settings.AutoFocusRoblox && !WindowGuard.RobloxIsFocused())
            FocusRobloxWithHint("Switched to Roblox. Playing as soon as the countdown ends.");
    }

    // Brings Roblox to the front and explains what happened in the status bar.
    public void FocusRobloxWithHint(string success)
    {
        if (WindowGuard.FocusRoblox()) ShowHint(success);
        else if (WindowGuard.FindRobloxWindow() == IntPtr.Zero) ShowHint("Roblox is not running. Open the game and sit at a piano first.");
        else ShowHint("Could not switch to Roblox automatically. Click into the game to continue.");
    }

    private void OnStop(object sender, RoutedEventArgs e) => _player.Stop();
    private void OnPrev(object sender, RoutedEventArgs e) => StepSong(-1, IsPlaying());
    private void OnNext(object sender, RoutedEventArgs e) => StepSong(1, IsPlaying());

    public bool IsPlaying() => _player.State is PlayState.Playing or PlayState.Countdown;

    private void OnProgressDown(object sender, MouseButtonEventArgs e) => _seeking = true;

    private void OnProgressUp(object sender, MouseButtonEventArgs e)
    {
        _seeking = false;
        _player.Seek(ProgressSlider.Value);
    }

    private void OnSpeedChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (SpeedText == null) return;
        _settings.Speed = Math.Round(e.NewValue, 2);
        SpeedText.Text = $"{_settings.Speed:0.00}x";
    }

    public void ChangeSpeed(double delta)
    {
        SpeedSlider.Value = Math.Clamp(Math.Round(SpeedSlider.Value + delta, 2), SpeedSlider.Minimum, SpeedSlider.Maximum);
        ShowHint($"Speed {_settings.Speed:0.00}x");
    }

    private void OnTransposeDown(object sender, RoutedEventArgs e) => SetTranspose(_player.Transpose - 1);
    private void OnTransposeUp(object sender, RoutedEventArgs e) => SetTranspose(_player.Transpose + 1);

    private void OnTransposeAuto(object sender, RoutedEventArgs e) =>
        SetTranspose(KeyMapper.BestTranspose(_player.PlayablePitches(), _settings.Layout));

    private void SetTranspose(int value)
    {
        _player.Transpose = Math.Clamp(value, -24, 24);
        _player.Rebuild();
        UpdateTranspose();
    }

    private void UpdateTranspose() =>
        TransposeText.Text = _player.Transpose > 0 ? $"+{_player.Transpose}" : _player.Transpose.ToString();

    private void OnLayoutChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing || Piano == null) return;
        _settings.Layout = Layout88.IsChecked == true ? KeyLayout.Keys88 : KeyLayout.Keys61;
        Piano.SetRange(_settings.Layout);
        KeyHint.Visibility = _settings.Layout == KeyLayout.Keys88 ? Visibility.Visible : Visibility.Collapsed;
        _player.Rebuild();
        RefreshSettingsPages();
    }

    private void OnQuickHumanize(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _settings.Humanize.Enabled = QuickHumanize.IsChecked == true;
        _player.Rebuild();
        RefreshSettingsPages();
    }

    private void OnHumanizeEnabled(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _settings.Humanize.Enabled = HumanizeEnabled.IsChecked == true;
        _player.Rebuild();
        RefreshSettingsPages();
    }

    private void OnQuickToggle(object sender, RoutedEventArgs e)
    {
        if (_syncing) return;
        _settings.Loop = QuickLoop.IsChecked == true;
        _settings.AutoNext = QuickAutoNext.IsChecked == true;
        RefreshSettingsPages();
    }

    private void OnOutputChanged(object sender, RoutedEventArgs e)
    {
        if (_syncing || OutPreview == null) return;
        var mode = OutPreview.IsChecked == true ? OutputMode.Preview : OutputMode.Keyboard;
        SetOutput(mode);
    }

    private void SetOutput(OutputMode mode)
    {
        _player.SetOutput(mode);
        RefreshSettingsPages();
        ShowHint(OutputDescription(mode));
    }

    private static string OutputDescription(OutputMode mode) => mode switch
    {
        OutputMode.Preview => "Plays through your PC speakers. Nothing goes to Roblox.",
        _ => "Real key presses. Pressing Play here switches to Roblox for you.",
    };

    #endregion

    #region Hotkeys

    private void RegisterHotkeys()
    {
        if (_hotkeys == null) return;
        _hotkeys.Clear();
        var h = _settings.Hotkeys;
        var failed = new List<string>();
        void Reg(string key, Action a) { if (!_hotkeys.Register(key, () => Dispatcher.BeginInvoke(a))) failed.Add(key); }

        Reg(h.PlayPause, () => TogglePlay(fromHotkey: true));
        Reg(h.Stop, () => _player.Stop());
        Reg(h.SlowDown, () => ChangeSpeed(-0.05));
        Reg(h.SpeedUp, () => ChangeSpeed(0.05));
        Reg(h.NextSong, () => StepSong(1, IsPlaying()));
        Reg(h.ToggleMini, ToggleMiniMode);
        Reg(h.ToggleHumanize, () =>
        {
            _settings.Humanize.Enabled = !_settings.Humanize.Enabled;
            _player.Rebuild();
            RefreshSettingsPages();
            ShowHint(_settings.Humanize.Enabled ? "Humanize on" : "Humanize off");
        });

        HotkeyText.Text = $"{h.PlayPause} play/pause · {h.Stop} stop · {h.SlowDown}/{h.SpeedUp} speed · {h.NextSong} next · {h.ToggleMini} mini";
        MiniButton.ToolTip = $"Mini player over Roblox ({h.ToggleMini})";
        if (failed.Count > 0) ShowHint($"These hotkeys are used by another app: {string.Join(", ", failed)}");
    }

    #endregion

    #region Live updates

    private void OnFrame()
    {
        if (!IsVisible) return;
        Piano.Tick();
        if (!_seeking && _player.Song != null)
        {
            double pos = _player.Position;
            ProgressSlider.Value = Math.Min(pos, ProgressSlider.Maximum);
            PosText.Text = Format(pos);
        }
        if (_player.State == PlayState.Countdown)
            StatusText.Text = $"Starting in {Math.Ceiling(_player.CountdownRemaining):0}…";

        // The upcoming-keys readout does not need 60 fps.
        if (++_frameCount % 6 == 0) UpdateNextKeys();
    }

    private void UpdateNextKeys()
    {
        var next = _player.Upcoming(14);
        var joined = string.Join(" ", next);
        if (joined == _lastNext) return;
        _lastNext = joined;
        NextKeysText.Inlines.Clear();
        if (next.Count == 0)
        {
            NextKeysText.Inlines.Add(new Run("—") { Foreground = (Brush)FindResource("InkFaint") });
            return;
        }
        for (int i = 0; i < next.Count; i++)
        {
            NextKeysText.Inlines.Add(new Run(next[i] + "  ")
            {
                Foreground = (Brush)FindResource(i == 0 ? "Brand" : i < 4 ? "Ink" : "InkFaint"),
                FontWeight = i == 0 ? FontWeights.Bold : FontWeights.Normal,
            });
        }
    }

    // Status label and brush key for the current player state (shared with the mini player).
    public static (string Text, string Brush) Describe(Player player) => player.State switch
    {
        PlayState.Playing when player.WaitingForFocus => ("Waiting for Roblox", "Warning"),
        PlayState.Playing when player.Settings.Output == OutputMode.Preview => ("Previewing", "Success"),
        PlayState.Playing => ("Playing", "Success"),
        PlayState.Countdown => ($"Starting in {Math.Ceiling(player.CountdownRemaining):0}…", "Warning"),
        PlayState.Paused => ("Paused", "BrandMuted"),
        _ => (player.Song == null ? "Idle" : "Ready", "InkMuted"),
    };

    private void UpdateState()
    {
        ProgressSlider.Maximum = Math.Max(0.1, _player.Length);
        LenText.Text = Format(_player.Length);

        var (text, brush) = Describe(_player);
        StatusText.Text = text;
        StatusDot.Fill = (Brush)FindResource(brush);

        // Song name in the title bar (and taskbar) while something is going on.
        string song = _player.Song?.Title ?? "";
        TitleSong.Text = IsPlaying() ? $"♪  {song}" : _player.State == PlayState.Paused ? $"{song}  ·  paused" : "";
        Title = IsPlaying() && song.Length > 0 ? $"{song} - AztroMidi" : "AztroMidi";

        bool active = IsPlaying();
        PlayIcon.Text = active ? "\uE769" : "\uE768";
        PlayLabel.Text = active ? "Pause" : "Play";

        if (_player.WaitingForFocus)
            HintText.Text = $"Waiting: Roblox only accepts keys while it is the active window. " +
                            $"Press {_settings.Hotkeys.PlayPause} to jump back, or Pause to stop here.";
        else
            HintText.Text = _hint;
    }

    public void ShowHint(string text)
    {
        _hint = text;
        HintText.Text = text;
    }

    public static string Format(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    #endregion
}
