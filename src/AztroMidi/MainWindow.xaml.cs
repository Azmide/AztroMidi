using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AztroMidi.Core;
using Microsoft.Win32;

namespace AztroMidi;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly Player _player;
    private readonly DispatcherTimer _frame;
    private HotkeyManager? _hotkeys;

    private List<SongEntry> _songs = new();
    private List<TrackItem> _tracks = new();
    private SongEntry? _current;
    private bool _seeking;
    private bool _syncing;
    private string _hint = "";
    private readonly List<Action> _refreshers = new();

    public MainWindow()
    {
        InitializeComponent();
        _player = new Player(_settings);
        _player.StateChanged += () => Dispatcher.BeginInvoke(UpdateState);
        _player.Finished += () => Dispatcher.BeginInvoke(OnSongFinished);

        Piano.SetSource(_player.Active);

        _frame = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _frame.Tick += (_, _) => OnFrame();
        _frame.Start();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"v{version?.Major}.{version?.Minor}";
        AboutText.Text =
            $"AztroMidi {VersionText.Text} — a lightweight MIDI auto-player for Roblox pianos.\n" +
            $"Settings file: {AppSettings.FilePath}\n" +
            "Layout: standard Roblox virtual piano (61 keys C2–C7) with the Ctrl 88-key extension.";

        BuildHumanizePage();
        BuildSettingsPage();
        ApplySettingsToUi();
        LoadLibrary();

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            RoundCorners(hwnd);
            _hotkeys = new HotkeyManager(hwnd);
            RegisterHotkeys();
        };
        StateChanged += (_, _) => MaxButton.Content = WindowState == WindowState.Maximized ? "" : "";
        Closing += (_, _) =>
        {
            _settings.Save();
            _hotkeys?.Dispose();
            _player.Dispose();
        };
    }

    // ------------------------------------------------------------------ window chrome

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private static void RoundCorners(IntPtr hwnd)
    {
        int round = 2; // DWMWCP_ROUND (Windows 11)
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        int dark = 1;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
    }

    private void OnTitleBarMouseDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2) { OnMaximize(sender, e); return; }
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnPin(object sender, RoutedEventArgs e)
    {
        _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
        ApplyTopmost();
    }

    private void ApplyTopmost()
    {
        Topmost = _settings.AlwaysOnTop;
        PinButton.Foreground = _settings.AlwaysOnTop ? (Brush)FindResource("Brand") : (Brush)FindResource("InkMuted");
    }

    private void OnNav(object sender, RoutedEventArgs e)
    {
        if (PagePlayer == null) return;
        PagePlayer.Visibility = NavPlayer.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageHumanize.Visibility = NavHumanize.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
    }

    // ------------------------------------------------------------------ library

    private void LoadLibrary()
    {
        var folder = _settings.LibraryFolder;
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder))
        {
            var bundled = Path.Combine(AppContext.BaseDirectory, "songs");
            folder = Directory.Exists(bundled) ? bundled : "";
        }

        var extra = _songs.Where(s => s.Folder == "(added)").ToList();
        _songs = new List<SongEntry>();
        if (folder.Length > 0)
        {
            try
            {
                _songs = Directory.EnumerateFiles(folder, "*.*", SearchOption.AllDirectories)
                    .Where(IsMidi)
                    .Select(p => SongEntry.From(p, folder))
                    .OrderBy(s => s.Name, StringComparer.OrdinalIgnoreCase)
                    .ToList();
            }
            catch (Exception ex)
            {
                SetHint($"Could not read folder: {ex.Message}");
            }
            LibraryInfo.Text = $"{_songs.Count} songs · {folder}";
        }
        else
        {
            LibraryInfo.Text = "No folder selected";
        }
        _songs.AddRange(extra.Where(e => _songs.All(s => s.Path != e.Path)));
        ApplySearch();
    }

    private static bool IsMidi(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".mid", StringComparison.OrdinalIgnoreCase) || ext.Equals(".midi", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".rmi", StringComparison.OrdinalIgnoreCase);
    }

    private void ApplySearch()
    {
        var q = SearchBox.Text.Trim();
        var items = q.Length == 0
            ? _songs
            : _songs.Where(s => s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                             || s.Folder.Contains(q, StringComparison.OrdinalIgnoreCase)).ToList();
        SongList.ItemsSource = items;
        LibraryEmpty.Visibility = _songs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        if (_current != null) SongList.SelectedItem = items.FirstOrDefault(s => s.Path == _current.Path);
    }

    private void OnSearch(object sender, TextChangedEventArgs e) => ApplySearch();

    private void OnPickFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFolderDialog { Title = "Choose your MIDI songs folder" };
        if (Directory.Exists(_settings.LibraryFolder)) dlg.InitialDirectory = _settings.LibraryFolder;
        if (dlg.ShowDialog(this) != true) return;
        _settings.LibraryFolder = dlg.FolderName;
        _settings.Save();
        LoadLibrary();
    }

    private void OnRefreshLibrary(object sender, RoutedEventArgs e) => LoadLibrary();

    private void OnOpenFile(object sender, RoutedEventArgs e)
    {
        var dlg = new OpenFileDialog { Filter = "MIDI files|*.mid;*.midi;*.rmi|All files|*.*", Multiselect = true };
        if (dlg.ShowDialog(this) != true) return;
        AddFiles(dlg.FileNames);
    }

    private void OnDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(DataFormats.FileDrop) ? DragDropEffects.Copy : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        if (e.Data.GetData(DataFormats.FileDrop) is string[] files) AddFiles(files);
    }

    private void AddFiles(IEnumerable<string> paths)
    {
        var files = new List<string>();
        foreach (var p in paths)
        {
            if (Directory.Exists(p)) files.AddRange(Directory.EnumerateFiles(p, "*.*", SearchOption.AllDirectories).Where(IsMidi));
            else if (File.Exists(p) && IsMidi(p)) files.Add(p);
        }
        if (files.Count == 0) { SetHint("No MIDI files found in what you dropped."); return; }

        SongEntry? first = null;
        foreach (var f in files)
        {
            var existing = _songs.FirstOrDefault(s => string.Equals(s.Path, f, StringComparison.OrdinalIgnoreCase));
            if (existing == null)
            {
                existing = new SongEntry { Path = f, Folder = "(added)" };
                _songs.Add(existing);
            }
            first ??= existing;
        }
        ApplySearch();
        if (first != null) SelectSong(first);
    }

    private void OnSongSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing) return;
        if (SongList.SelectedItem is SongEntry s && s.Path != _current?.Path) SelectSong(s);
    }

    private void OnSongDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (SongList.SelectedItem is SongEntry s)
        {
            if (s.Path != _current?.Path) SelectSong(s);
            _player.Play();
        }
    }

    private void SelectSong(SongEntry song, bool autoPlay = false)
    {
        MidiFile midi;
        try
        {
            midi = MidiFile.Load(song.Path);
        }
        catch (Exception ex)
        {
            SetHint($"Could not open {song.Name}: {ex.Message}");
            return;
        }

        _current = song;
        _player.Load(midi);

        _syncing = true;
        SongList.SelectedItem = song;
        SongList.ScrollIntoView(song);
        _syncing = false;

        SongTitle.Text = song.Name;
        int used = midi.Tracks.Count(t => t.NoteCount > 0);
        SongMeta.Text = $"{midi.Notes.Count:N0} notes · {used} tracks · {midi.InitialBpm:0.#} BPM · {Format(midi.Length)}";
        BuildTrackList(midi);
        UpdateTranspose();
        UpdateState();
        SetHint(_settings.OnlyWhenRobloxFocused
            ? $"Loaded. Press {_settings.Hotkeys.PlayPause} inside Roblox to start playing."
            : $"Loaded. Press Play, then click into Roblox within {_settings.CountdownSeconds}s.");

        if (autoPlay) _player.Play(useCountdown: false);
    }

    private void StepSong(int dir, bool autoPlay)
    {
        var list = SongList.ItemsSource as List<SongEntry> ?? _songs;
        if (list.Count == 0) return;
        int idx = _current == null ? -1 : list.FindIndex(s => s.Path == _current.Path);
        idx = ((idx + dir) % list.Count + list.Count) % list.Count;
        SelectSong(list[idx], autoPlay);
    }

    private void OnSongFinished()
    {
        if (_settings.Loop)
        {
            _player.Seek(0);
            _player.Play(useCountdown: false);
        }
        else if (_settings.AutoNext)
        {
            StepSong(1, autoPlay: true);
        }
    }

    // ------------------------------------------------------------------ tracks

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

    // ------------------------------------------------------------------ transport

    private void OnPlayPause(object sender, RoutedEventArgs e) => TogglePlay();

    private void TogglePlay()
    {
        if (_player.Song == null)
        {
            if (_songs.Count > 0) SelectSong((SongList.SelectedItem as SongEntry) ?? _songs[0]);
            if (_player.Song == null) return;
        }
        _player.TogglePlay();
    }

    private void OnStop(object sender, RoutedEventArgs e) => _player.Stop();
    private void OnPrev(object sender, RoutedEventArgs e) => StepSong(-1, IsPlaying());
    private void OnNext(object sender, RoutedEventArgs e) => StepSong(1, IsPlaying());

    private bool IsPlaying() => _player.State is PlayState.Playing or PlayState.Countdown;

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

    private void ChangeSpeed(double delta)
    {
        SpeedSlider.Value = Math.Clamp(Math.Round(SpeedSlider.Value + delta, 2), SpeedSlider.Minimum, SpeedSlider.Maximum);
        SetHint($"Speed {_settings.Speed:0.00}x");
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
        _settings.OnlyWhenRobloxFocused = QuickGuard.IsChecked == true;
        RefreshSettingsPages();
    }

    // ------------------------------------------------------------------ hotkeys

    private void RegisterHotkeys()
    {
        if (_hotkeys == null) return;
        _hotkeys.Clear();
        var h = _settings.Hotkeys;
        var failed = new List<string>();
        void Reg(string key, Action a) { if (!_hotkeys.Register(key, () => Dispatcher.BeginInvoke(a))) failed.Add(key); }

        Reg(h.PlayPause, TogglePlay);
        Reg(h.Stop, () => _player.Stop());
        Reg(h.SlowDown, () => ChangeSpeed(-0.05));
        Reg(h.SpeedUp, () => ChangeSpeed(0.05));
        Reg(h.NextSong, () => StepSong(1, IsPlaying()));
        Reg(h.ToggleHumanize, () =>
        {
            _settings.Humanize.Enabled = !_settings.Humanize.Enabled;
            _player.Rebuild();
            RefreshSettingsPages();
            SetHint(_settings.Humanize.Enabled ? "Humanize on" : "Humanize off");
        });

        HotkeyText.Text = $"{h.PlayPause} play/pause · {h.Stop} stop · {h.SlowDown}/{h.SpeedUp} speed · {h.NextSong} next";
        if (failed.Count > 0) SetHint($"These hotkeys are used by another app: {string.Join(", ", failed)}");
    }

    // ------------------------------------------------------------------ live updates

    private void OnFrame()
    {
        Piano.Tick();
        if (!_seeking && _player.Song != null)
        {
            double pos = _player.Position;
            ProgressSlider.Value = Math.Min(pos, ProgressSlider.Maximum);
            PosText.Text = Format(pos);
        }
        if (_player.State == PlayState.Countdown)
            StatusText.Text = $"Starting in {Math.Ceiling(_player.CountdownRemaining):0}…";
    }

    private void UpdateState()
    {
        ProgressSlider.Maximum = Math.Max(0.1, _player.Length);
        LenText.Text = Format(_player.Length);

        var (text, brush) = _player.State switch
        {
            PlayState.Playing when _player.WaitingForFocus => ("Waiting for Roblox", "Warning"),
            PlayState.Playing => ("Playing", "Success"),
            PlayState.Countdown => ("Get ready…", "Warning"),
            PlayState.Paused => ("Paused", "BrandMuted"),
            _ => (_player.Song == null ? "Idle" : "Ready", "InkMuted"),
        };
        StatusText.Text = text;
        StatusDot.Fill = (Brush)FindResource(brush);

        bool active = IsPlaying();
        PlayIcon.Text = active ? "" : "";
        PlayLabel.Text = active ? "Pause" : "Play";

        if (_player.WaitingForFocus)
            SetHint("Paused until the Roblox window is focused. Click into the game to continue.");
        else if (_player.State == PlayState.Playing)
            SetHint(_hint.StartsWith("Paused until") ? "" : _hint);
    }

    private void SetHint(string text)
    {
        _hint = text;
        HintText.Text = text;
    }

    private static string Format(double seconds)
    {
        var t = TimeSpan.FromSeconds(Math.Max(0, seconds));
        return t.TotalHours >= 1 ? t.ToString(@"h\:mm\:ss") : t.ToString(@"m\:ss");
    }

    // ------------------------------------------------------------------ settings pages

    private void ApplySettingsToUi()
    {
        _syncing = true;
        SpeedSlider.Value = Math.Clamp(_settings.Speed, SpeedSlider.Minimum, SpeedSlider.Maximum);
        SpeedText.Text = $"{SpeedSlider.Value:0.00}x";
        Layout61.IsChecked = _settings.Layout == KeyLayout.Keys61;
        Layout88.IsChecked = _settings.Layout == KeyLayout.Keys88;
        Piano.SetRange(_settings.Layout);
        KeyHint.Visibility = _settings.Layout == KeyLayout.Keys88 ? Visibility.Visible : Visibility.Collapsed;
        _syncing = false;
        RefreshSettingsPages();
        ApplyTopmost();
    }

    /// <summary>Pushes current settings into every generated control and quick toggle.</summary>
    private void RefreshSettingsPages()
    {
        _syncing = true;
        QuickHumanize.IsChecked = _settings.Humanize.Enabled;
        HumanizeEnabled.IsChecked = _settings.Humanize.Enabled;
        QuickLoop.IsChecked = _settings.Loop;
        QuickAutoNext.IsChecked = _settings.AutoNext;
        QuickGuard.IsChecked = _settings.OnlyWhenRobloxFocused;
        Layout61.IsChecked = _settings.Layout == KeyLayout.Keys61;
        Layout88.IsChecked = _settings.Layout == KeyLayout.Keys88;
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
                SetHint($"Humanize preset: {p.Name}");
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
        AddSwitch(PlaybackRows, "Only play while Roblox is focused", "Stops key presses from going into other apps.",
            () => s.OnlyWhenRobloxFocused, v => s.OnlyWhenRobloxFocused = v);
        AddSwitch(PlaybackRows, "Always on top", "Keep AztroMidi above the game window.",
            () => s.AlwaysOnTop, v => { s.AlwaysOnTop = v; ApplyTopmost(); });
        AddSlider(PlaybackRows, "Countdown", "Seconds to wait after pressing Play so you can switch to Roblox.",
            0, 10, 1, () => s.CountdownSeconds, v => s.CountdownSeconds = (int)v, v => v == 0 ? "Off" : $"{v:0} s");
        AddSlider(PlaybackRows, "Shortest key press", "Very short notes are held at least this long so the game registers them.",
            5, 120, 5, () => s.MinHoldMs, v => { s.MinHoldMs = v; _player.Rebuild(); }, v => $"{v:0} ms");

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
        var combo = new ComboBox { ItemsSource = items, MinWidth = 220, VerticalAlignment = VerticalAlignment.Center };
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
}
