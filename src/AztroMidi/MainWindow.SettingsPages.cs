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
    #region Settings pages

    private void ApplySettingsToUi()
    {
        _syncing = true;
        SpeedSlider.Value = Math.Clamp(_settings.Speed, SpeedSlider.Minimum, SpeedSlider.Maximum);
        SpeedText.Text = $"{SpeedSlider.Value:0.00}x";
        Piano.SetRange(_settings.Layout);
        KeyHint.Visibility = _settings.Layout == KeyLayout.Keys88 ? Visibility.Visible : Visibility.Collapsed;
        _syncing = false;
        RefreshSettingsPages();
        ApplyTopmost();
    }

    // Pushes current settings into every generated control and quick toggle.
    private void RefreshSettingsPages()
    {
        _syncing = true;
        QuickHumanize.IsChecked = _settings.Humanize.Enabled;
        HumanizeEnabled.IsChecked = _settings.Humanize.Enabled;
        QuickLoop.IsChecked = _settings.Loop;
        QuickAutoNext.IsChecked = _settings.AutoNext;
        Layout61.IsChecked = _settings.Layout == KeyLayout.Keys61;
        Layout88.IsChecked = _settings.Layout == KeyLayout.Keys88;
        OutKeyboard.IsChecked = _settings.Output == OutputMode.Keyboard;
        OutPreview.IsChecked = _settings.Output == OutputMode.Preview;
        OutputHint.ToolTip = OutputDescription(_settings.Output);
        foreach (var r in _refreshers) r();
        _syncing = false;
    }

    private void BuildHumanizePage()
    {
        var h = _settings.Humanize;
        var presets = new (string Name, string Tip, Action Apply)[]
        {
            ("Off", "Exact MIDI timing", () => h.Enabled = false),
            ("Subtle", "Barely noticeable looseness", () => SetPreset(true, 4, 8, 1, 6, 3, 15, 0)),
            ("Natural", "Sounds like a confident player", () => SetPreset(true, 8, 14, 2.5, 12, 6, 35, 0)),
            ("Expressive", "Loose, emotional playing", () => SetPreset(true, 14, 24, 4.5, 20, 10, 70, 0.4)),
            ("Sloppy", "Beginner with the odd slip", () => SetPreset(true, 22, 34, 6, 28, 14, 90, 2)),
        };
        foreach (var p in presets)
        {
            var b = new Button { Content = p.Name, ToolTip = p.Tip, Margin = new Thickness(0, 0, 8, 8) };
            b.Click += (_, _) =>
            {
                p.Apply();
                _player.Rebuild();
                RefreshSettingsPages();
                ShowHint($"Humanize preset: {p.Name}");
            };
            PresetPanel.Children.Add(b);
        }

        AddSlider(HumanizeRows, "Timing looseness", "How far each note may land before or after the beat.",
            0, 40, 1, () => h.TimingJitterMs, v => h.TimingJitterMs = v, v => $"±{v:0} ms");
        AddSlider(HumanizeRows, "Chord roll", "Chords are rolled like real fingers instead of hitting all keys at once.",
            0, 60, 1, () => h.ChordRollMs, v => h.ChordRollMs = v, v => $"{v:0} ms");
        AddSlider(HumanizeRows, "Tempo drift", "Slow, natural speeding up and slowing down across the song.",
            0, 10, 0.5, () => h.TempoDriftPercent, v => h.TempoDriftPercent = v, v => $"±{v:0.#}%");
        AddSlider(HumanizeRows, "Key hold variation", "Notes are held a little longer or shorter each time.",
            0, 40, 1, () => h.HoldVariancePercent, v => h.HoldVariancePercent = v, v => $"±{v:0}%");
        AddSlider(HumanizeRows, "Phrase breathing", "A tiny pause after rests, like a player lifting their hands.",
            0, 150, 5, () => h.PhraseBreathMs, v => h.PhraseBreathMs = v, v => $"{v:0} ms");
        AddSlider(HumanizeRows, "Velocity variation", "Random loudness changes (only with velocity keys on).",
            0, 30, 1, () => h.VelocityVariance, v => h.VelocityVariance = (int)v, v => $"±{v:0}");
        AddSlider(HumanizeRows, "Wrong-note slips", "Chance to brush a neighbouring key right before the real note.",
            0, 5, 0.1, () => h.MistakeChancePercent, v => h.MistakeChancePercent = v, v => $"{v:0.0}%");

        void SetPreset(bool on, double jitter, double roll, double drift, double hold, int vel, double breath, double mistakes)
        {
            h.Enabled = on;
            h.TimingJitterMs = jitter;
            h.ChordRollMs = roll;
            h.TempoDriftPercent = drift;
            h.HoldVariancePercent = hold;
            h.VelocityVariance = vel;
            h.PhraseBreathMs = breath;
            h.MistakeChancePercent = mistakes;
        }
    }

    private void BuildSettingsPage()
    {
        var s = _settings;
        AddCombo(PlaybackRows, "Send notes to", "Roblox only accepts keys while its window is active.",
            new[] { "Roblox", "Preview on this PC" },
            () => s.Output == OutputMode.Preview ? 1 : 0,
            i => SetOutput(i == 1 ? OutputMode.Preview : OutputMode.Keyboard));
        AddCombo(PlaybackRows, "Dropped MIDI files", "What happens when you drag .mid files onto AztroMidi.",
            new[] { "Move into library folder", "Copy into library folder", "Only add to the list" },
            () => (int)s.DropAction,
            i => s.DropAction = (DropAction)i);
        AddCombo(PlaybackRows, "Out-of-range notes", "What to do with notes the piano cannot play.",
            new[] { "Fold into range (octave shift)", "Skip them" },
            () => s.OutOfRange == OutOfRange.Fold ? 0 : 1,
            i => { s.OutOfRange = i == 0 ? OutOfRange.Fold : OutOfRange.Skip; _player.Rebuild(); });
        AddCombo(PlaybackRows, "Sustain pedal", "Hold Space while the MIDI sustain pedal is down.",
            new[] { "Off", "Follow MIDI pedal (Space)" },
            () => s.Sustain == SustainMode.Off ? 0 : 1,
            i => { s.Sustain = i == 0 ? SustainMode.Off : SustainMode.FollowPedal; _player.Rebuild(); });
        AddSwitch(PlaybackRows, "Velocity keys", "Send Alt + key to set note loudness (32 levels) on pianos that support it.",
            () => s.VelocityKeys, v => s.VelocityKeys = v);
        AddSwitch(PlaybackRows, "Auto transpose on load", "Pick the key that fits the most notes when a song is opened.",
            () => s.AutoTranspose, v => s.AutoTranspose = v);
        AddSwitch(PlaybackRows, "Skip drum tracks", "Ignore MIDI channel 10 (percussion) which sounds wrong on a piano.",
            () => s.SkipDrums, v => { s.SkipDrums = v; _player.Rebuild(); });
        AddSwitch(PlaybackRows, "Switch to Roblox on Play", "In Roblox mode, pressing Play in AztroMidi brings the game to the front automatically.",
            () => s.AutoFocusRoblox, v => s.AutoFocusRoblox = v);
        AddSwitch(PlaybackRows, "Only play while Roblox is focused", "In Roblox mode, stop key presses from going into other apps.",
            () => s.OnlyWhenRobloxFocused, v => s.OnlyWhenRobloxFocused = v);
        AddSwitch(PlaybackRows, "Always on top", "Keep AztroMidi above the game window.",
            () => s.AlwaysOnTop, v => { s.AlwaysOnTop = v; ApplyTopmost(); });
        AddSlider(PlaybackRows, "Countdown", "Seconds to wait after pressing Play so you can switch to Roblox.",
            0, 10, 1, () => s.CountdownSeconds, v => s.CountdownSeconds = (int)v, v => v == 0 ? "Off" : $"{v:0} s");
        AddSlider(PlaybackRows, "Shortest key press", "Very short notes are held at least this long so the game registers them.",
            5, 120, 5, () => s.MinHoldMs, v => { s.MinHoldMs = v; _player.Rebuild(); }, v => $"{v:0} ms");

        AddSwitch(MiniRows, "Stick to Roblox", "Keep the mini player in the top-right corner of the Roblox window.",
            () => s.MiniSnapToRoblox, v => s.MiniSnapToRoblox = v);
        AddSlider(MiniRows, "Opacity", "How see-through the mini player is.",
            40, 100, 5, () => s.MiniOpacity * 100, v => { s.MiniOpacity = v / 100; if (_mini != null) _mini.Opacity = s.MiniOpacity; }, v => $"{v:0}%");
        var open = new Button { Content = "Open mini player", Style = (Style)FindResource("PrimaryButton"), HorizontalAlignment = HorizontalAlignment.Left, Margin = new Thickness(0, 10, 0, 0) };
        open.Click += (_, _) => EnterMiniMode();
        MiniRows.Children.Add(open);

        var h = s.Hotkeys;
        void Hotkey(string title, Func<string> get, Action<string> set) =>
            AddCombo(HotkeyRows, title, null, HotkeyManager.Choices,
                () => Math.Max(0, Array.IndexOf(HotkeyManager.Choices, get())),
                i => { set(HotkeyManager.Choices[i]); RegisterHotkeys(); });
        Hotkey("Play / Pause", () => h.PlayPause, v => h.PlayPause = v);
        Hotkey("Stop", () => h.Stop, v => h.Stop = v);
        Hotkey("Slow down", () => h.SlowDown, v => h.SlowDown = v);
        Hotkey("Speed up", () => h.SpeedUp, v => h.SpeedUp = v);
        Hotkey("Next song", () => h.NextSong, v => h.NextSong = v);
        Hotkey("Toggle humanize", () => h.ToggleHumanize, v => h.ToggleHumanize = v);
        Hotkey("Mini player", () => h.ToggleMini, v => h.ToggleMini = v);
    }

    private Grid Row(Panel host, string title, string? description)
    {
        var grid = new Grid { Margin = new Thickness(0, 10, 0, 10) };
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        grid.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
        var text = new StackPanel { Margin = new Thickness(0, 0, 24, 0), VerticalAlignment = VerticalAlignment.Center };
        text.Children.Add(new TextBlock { Text = title, Style = (Style)FindResource("H2"), FontWeight = FontWeights.Medium });
        if (description != null)
            text.Children.Add(new TextBlock { Text = description, Style = (Style)FindResource("Muted"), Margin = new Thickness(0, 2, 0, 0) });
        grid.Children.Add(text);
        if (host.Children.Count > 0)
            host.Children.Add(new Border { Height = 1, Background = (Brush)FindResource("Line") });
        host.Children.Add(grid);
        return grid;
    }

    private void AddSwitch(Panel host, string title, string description, Func<bool> get, Action<bool> set)
    {
        var grid = Row(host, title, description);
        var sw = new CheckBox { Style = (Style)FindResource("Switch"), VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(sw, 1);
        grid.Children.Add(sw);
        RoutedEventHandler handler = (_, _) =>
        {
            if (_syncing) return;
            set(sw.IsChecked == true);
            RefreshSettingsPages();
        };
        sw.Checked += handler;
        sw.Unchecked += handler;
        _refreshers.Add(() => sw.IsChecked = get());
    }

    private void AddCombo(Panel host, string title, string? description, string[] items, Func<int> get, Action<int> set)
    {
        var grid = Row(host, title, description);
        var combo = new ComboBox { ItemsSource = items, MinWidth = 240, VerticalAlignment = VerticalAlignment.Center };
        Grid.SetColumn(combo, 1);
        grid.Children.Add(combo);
        combo.SelectionChanged += (_, _) =>
        {
            if (_syncing || combo.SelectedIndex < 0) return;
            set(combo.SelectedIndex);
        };
        _refreshers.Add(() => combo.SelectedIndex = get());
    }

    private void AddSlider(Panel host, string title, string description, double min, double max, double step,
        Func<double> get, Action<double> set, Func<double, string> format)
    {
        var grid = Row(host, title, description);
        var right = new Grid { Width = 260, VerticalAlignment = VerticalAlignment.Center };
        right.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
        right.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(64) });
        var slider = new Slider
        {
            Minimum = min, Maximum = max, SmallChange = step, LargeChange = step * 5,
            TickFrequency = step, IsSnapToTickEnabled = true, VerticalAlignment = VerticalAlignment.Center,
        };
        var value = new TextBlock { Style = (Style)FindResource("Value"), TextAlignment = TextAlignment.Right };
        Grid.SetColumn(value, 1);
        right.Children.Add(slider);
        right.Children.Add(value);
        Grid.SetColumn(right, 1);
        grid.Children.Add(right);

        // Rebuilding the schedule is cheap, but only do it once the drag settles.
        var debounce = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(250) };
        debounce.Tick += (_, _) => { debounce.Stop(); _player.Rebuild(); };

        slider.ValueChanged += (_, e) =>
        {
            value.Text = format(e.NewValue);
            if (_syncing) return;
            set(e.NewValue);
            debounce.Stop();
            debounce.Start();
        };
        _refreshers.Add(() =>
        {
            slider.Value = get();
            value.Text = format(slider.Value);
        });
    }

    #endregion
}
