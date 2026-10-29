using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
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
    private MiniPlayer? _mini;

    private List<SongEntry> _songs = new();
    private List<TrackItem> _tracks = new();
    private SongEntry? _current;
    private bool _seeking;
    private bool _syncing;
    private string _hint = "";
    private string _lastNext = "";
    private int _frameCount;
    private readonly List<Action> _refreshers = new();

    public Player Player => _player;

    public MainWindow()
    {
        InitializeComponent();
        // Fit smaller screens such as 1366x768 laptops.
        var work = SystemParameters.WorkArea;
        Height = Math.Min(Height, work.Height - 16);
        Width = Math.Min(Width, work.Width - 16);
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
        RestoreLastSong();

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
            _mini?.Close();
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

    // ------------------------------------------------------------------ mini player

    private void OnMini(object sender, RoutedEventArgs e) => EnterMiniMode();

    public void EnterMiniMode()
    {
        _mini ??= new MiniPlayer(this, _settings);
        _mini.Show();
        Hide();
    }

    public void ExitMiniMode()
    {
        _mini?.Hide();
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void ToggleMiniMode()
    {
        if (_mini?.IsVisible == true) ExitMiniMode();
        else EnterMiniMode();
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
                ShowHint($"Could not read folder: {ex.Message}");
            }
            LibraryInfo.Text = folder;
            LibraryInfo.ToolTip = folder;
        }
        else
        {
            LibraryInfo.Text = "No folder selected";
        }
        _songs.AddRange(extra.Where(e => _songs.All(s => s.Path != e.Path)));

        var favs = new HashSet<string>(_settings.Favorites, StringComparer.OrdinalIgnoreCase);
        foreach (var s in _songs) s.IsFavorite = favs.Contains(s.Path);
        ApplySearch();
    }

    private void RestoreLastSong()
    {
        var path = _settings.LastSong;
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return;
        var song = _songs.FirstOrDefault(s => string.Equals(s.Path, path, StringComparison.OrdinalIgnoreCase));
        if (song == null)
        {
            song = new SongEntry { Path = path, Folder = "(added)" };
            _songs.Add(song);
            ApplySearch();
        }
        SelectSong(song);
    }

    private static bool IsMidi(string path)
    {
        var ext = Path.GetExtension(path);
        return ext.Equals(".mid", StringComparison.OrdinalIgnoreCase) || ext.Equals(".midi", StringComparison.OrdinalIgnoreCase)
            || ext.Equals(".rmi", StringComparison.OrdinalIgnoreCase);
    }

    private List<SongEntry> VisibleSongs() => (SongList.ItemsSource as List<SongEntry>) ?? _songs;

    private void ApplySearch()
    {
        var q = SearchBox.Text.Trim();
        IEnumerable<SongEntry> items = _songs;
        if (FilterFav.IsChecked == true) items = items.Where(s => s.IsFavorite);
        if (q.Length > 0)
            items = items.Where(s => s.Name.Contains(q, StringComparison.OrdinalIgnoreCase)
                                  || s.Folder.Contains(q, StringComparison.OrdinalIgnoreCase));
        var list = items.ToList();

        _syncing = true;
        SongList.ItemsSource = list;
        if (_current != null) SongList.SelectedItem = list.FirstOrDefault(s => s.Path == _current.Path);
        _syncing = false;

        CountText.Text = list.Count == _songs.Count ? $"{_songs.Count} songs" : $"{list.Count} of {_songs.Count}";
        LibraryEmpty.Visibility = _songs.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnSearch(object sender, TextChangedEventArgs e) => ApplySearch();

    private void OnFilterChanged(object sender, RoutedEventArgs e)
    {
        if (SongList != null) ApplySearch();
    }

    private void OnToggleFavorite(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.Tag is not SongEntry song) return;
        song.IsFavorite = !song.IsFavorite;
        _settings.Favorites.RemoveAll(p => string.Equals(p, song.Path, StringComparison.OrdinalIgnoreCase));
        if (song.IsFavorite) _settings.Favorites.Add(song.Path);
        _settings.Save();
        if (FilterFav.IsChecked == true) ApplySearch();
        e.Handled = true;
    }

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
        if (files.Count == 0) { ShowHint("No MIDI files found in what you dropped."); return; }

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
        FilterAll.IsChecked = true;
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
            ShowHint($"Could not open {song.Name}: {ex.Message}");
            return;
        }

        _current = song;
        _settings.LastSong = song.Path;
        _player.Load(midi);

        _syncing = true;
        SongList.SelectedItem = song;
        if (SongList.SelectedItem != null) SongList.ScrollIntoView(song);
        _syncing = false;

        SongTitle.Text = song.Name;
        int used = midi.Tracks.Count(t => t.NoteCount > 0);
        SongMeta.Text = $"{midi.Notes.Count:N0} notes · {used} tracks · {midi.InitialBpm:0.#} BPM · {Format(midi.Length)}";
        BuildTrackList(midi);
        UpdateTranspose();
        UpdateState();
        ShowHint(_settings.Output switch
        {
            OutputMode.Preview => "Loaded. Press Play to listen on this PC.",
            OutputMode.Background => $"Loaded. Press {_settings.Hotkeys.PlayPause} to play into Roblox, even from other apps.",
            _ when _settings.OnlyWhenRobloxFocused => $"Loaded. Press {_settings.Hotkeys.PlayPause} inside Roblox to start playing.",
            _ => $"Loaded. Press Play, then click into Roblox within {_settings.CountdownSeconds}s.",
        });

        if (autoPlay) _player.Play(useCountdown: false);
    }

    public void StepSong(int dir, bool autoPlay)
    {
        var list = VisibleSongs();
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

    public void TogglePlay()
    {
        if (_player.Song == null)
        {
            if (_songs.Count > 0) SelectSong((SongList.SelectedItem as SongEntry) ?? VisibleSongs().FirstOrDefault() ?? _songs[0]);
            if (_player.Song == null) return;
        }
        _player.TogglePlay();
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
        var mode = OutPreview.IsChecked == true ? OutputMode.Preview
                 : OutBackground.IsChecked == true ? OutputMode.Background
                 : OutputMode.Keyboard;
        SetOutput(mode);
    }

    private void SetOutput(OutputMode mode)
    {
        _player.SetOutput(mode);
        RefreshSettingsPages();
        ShowHint(mode == OutputMode.Background
            ? "Background mode is experimental. If notes don't play in your game, switch back to Roblox mode."
            : OutputDescription(mode));
    }

    private static string OutputDescription(OutputMode mode) => mode switch
    {
        OutputMode.Background => "Experimental. Keeps playing into Roblox while you use other apps.",
        OutputMode.Preview => "Plays through your PC speakers. Nothing goes to Roblox.",
        _ => "Real key presses. Click into Roblox before playing.",
    };

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

    // ------------------------------------------------------------------ live updates

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

    /// <summary>Status label and brush key for the current player state (shared with the mini player).</summary>
    public static (string Text, string Brush) Describe(Player player) => player.State switch
    {
        PlayState.Playing when player.WaitingForFocus => (
            player.Settings.Output == OutputMode.Background ? "Roblox not found" : "Waiting for Roblox", "Warning"),
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

        bool active = IsPlaying();
        PlayIcon.Text = active ? "" : "";
        PlayLabel.Text = active ? "Pause" : "Play";

        if (_player.WaitingForFocus)
            HintText.Text = _settings.Output == OutputMode.Background
                ? "Waiting for Roblox to start. Open the game and playback continues."
                : "Paused until the Roblox window is focused. Click into the game to continue.";
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

    // ------------------------------------------------------------------ settings pages

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

    /// <summary>Pushes current settings into every generated control and quick toggle.</summary>
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
        OutBackground.IsChecked = _settings.Output == OutputMode.Background;
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
        AddCombo(PlaybackRows, "Send notes to", "Background mode is experimental and may not work in every game.",
            new[] { "Roblox (focused window)", "Roblox in background (experimental)", "Preview on this PC" },
            () => (int)s.Output,
            i => SetOutput((OutputMode)i));
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
}
