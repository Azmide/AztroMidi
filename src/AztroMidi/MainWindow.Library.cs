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
    #region Library

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
        ScanDurations();
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
        ToggleFavorite(song);
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
        bool files = e.Data.GetDataPresent(DataFormats.FileDrop);
        e.Effects = !files ? DragDropEffects.None
                  : _settings.DropAction == DropAction.Move ? DragDropEffects.Move
                  : DragDropEffects.Copy;
        if (files && DropOverlay.Visibility != Visibility.Visible)
        {
            DropText.Text = _settings.DropAction switch
            {
                DropAction.Move => "Drop to move into your library",
                DropAction.Copy => "Drop to copy into your library",
                _ => "Drop to add to the list",
            };
            DropOverlay.Visibility = Visibility.Visible;
        }
        e.Handled = true;
    }

    private void OnDragLeave(object sender, DragEventArgs e)
    {
        // DragLeave also fires when moving between child elements; only hide when leaving the window.
        var pos = e.GetPosition(this);
        if (pos.X <= 0 || pos.Y <= 0 || pos.X >= ActualWidth || pos.Y >= ActualHeight)
            DropOverlay.Visibility = Visibility.Collapsed;
    }

    private void OnDrop(object sender, DragEventArgs e)
    {
        DropOverlay.Visibility = Visibility.Collapsed;
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

        if (_settings.DropAction != DropAction.ListOnly)
        {
            ImportIntoLibrary(files);
            return;
        }

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
        ScanDurations();
        if (first != null) SelectSong(first);
    }

    // Moves or copies dropped songs into the library folder so they stay in the library next time.
    // Uses a "songs" folder next to the exe if none is set.
    private void ImportIntoLibrary(List<string> files)
    {
        var library = _settings.LibraryFolder;
        if (string.IsNullOrEmpty(library) || !Directory.Exists(library))
        {
            library = Path.Combine(AppContext.BaseDirectory, "songs");
            Directory.CreateDirectory(library);
            _settings.LibraryFolder = library;
        }

        bool move = _settings.DropAction == DropAction.Move;
        var imported = new List<string>();
        var failed = new List<string>();
        foreach (var file in files)
        {
            // Already in the library (for example dragged from the list itself): nothing to do.
            if (Path.GetFullPath(file).StartsWith(Path.GetFullPath(library) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase))
            {
                imported.Add(file);
                continue;
            }
            try
            {
                var target = FreeFileName(library, Path.GetFileName(file));
                if (move) File.Move(file, target);
                else File.Copy(file, target);
                imported.Add(target);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                failed.Add(Path.GetFileName(file));
            }
        }
        _settings.Save();
        LoadLibrary();

        var first = imported.Select(p => _songs.FirstOrDefault(s => string.Equals(s.Path, p, StringComparison.OrdinalIgnoreCase)))
                            .FirstOrDefault(s => s != null);
        FilterAll.IsChecked = true;
        SearchBox.Text = "";
        if (first != null)
        {
            if (IsPlaying()) { _syncing = true; SongList.SelectedItem = first; SongList.ScrollIntoView(first); _syncing = false; }
            else SelectSong(first);
        }

        string verb = move ? "Moved" : "Copied";
        string what = imported.Count == 1 ? $"\"{Path.GetFileNameWithoutExtension(imported[0])}\"" : $"{imported.Count} songs";
        ShowHint(failed.Count == 0
            ? $"{verb} {what} into your library."
            : $"{verb} {what}. Could not import {string.Join(", ", failed)} (file in use?).");
    }

    // "Song.mid", or "Song (2).mid" if that name is taken.
    private static string FreeFileName(string folder, string fileName)
    {
        var name = Path.GetFileNameWithoutExtension(fileName);
        var ext = Path.GetExtension(fileName);
        var path = Path.Combine(folder, fileName);
        for (int i = 2; File.Exists(path); i++)
            path = Path.Combine(folder, $"{name} ({i}){ext}");
        return path;
    }

    private void OnSongSelected(object sender, SelectionChangedEventArgs e)
    {
        if (_syncing || SongList.SelectedItem is not SongEntry s || s.Path == _current?.Path) return;
        // A single click while music plays only selects; it must not cut off the song.
        if (IsPlaying())
        {
            ShowHint($"Double-click or press Enter to play \"{s.Name}\".");
            return;
        }
        SelectSong(s);
    }

    private void OnSongDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindParent<ButtonBase>(d) != null) return; // star button
        PlaySelected();
    }

    // Loads the selected song (if needed) and starts it.
    private void PlaySelected()
    {
        if (SongList.SelectedItem is not SongEntry s) return;
        if (s.Path != _current?.Path)
        {
            bool wasPlaying = IsPlaying();
            SelectSong(s, autoPlay: wasPlaying);
            if (wasPlaying) return;
        }
        if (!IsPlaying()) TogglePlay();
    }

    private static T? FindParent<T>(DependencyObject child) where T : DependencyObject
    {
        for (var d = child; d != null; d = VisualTreeHelper.GetParent(d))
            if (d is T match) return match;
        return null;
    }

    #endregion

    #region Song menu and durations

    private void BuildSongMenu()
    {
        var menu = new ContextMenu();
        MenuItem Item(string header, string glyph, string gesture, Action<SongEntry> act)
        {
            var item = new MenuItem { Header = header, Tag = glyph, InputGestureText = gesture };
            item.Click += (_, _) => { if (SongList.SelectedItem is SongEntry s) act(s); };
            menu.Items.Add(item);
            return item;
        }

        Item("Play", "", "Enter", _ => PlaySelected());
        var fav = Item("Add to favorites", "", "", s => ToggleFavorite(s));
        menu.Items.Add(new Separator());
        Item("Show in folder", "", "", s =>
            System.Diagnostics.Process.Start("explorer.exe", $"/select,\"{s.Path}\""));
        Item("Copy file path", "", "", s => Clipboard.SetText(s.Path));
        menu.Items.Add(new Separator());
        Item("Remove from list", "", "Del", s => RemoveFromList(s));

        menu.Opened += (_, _) =>
        {
            if (SongList.SelectedItem is not SongEntry s) { menu.IsOpen = false; return; }
            fav.Header = s.IsFavorite ? "Remove from favorites" : "Add to favorites";
            fav.Tag = s.IsFavorite ? "" : "";
        };
        SongList.ContextMenu = menu;

        // Right-click selects the row under the mouse first, like Explorer does.
        SongList.PreviewMouseRightButtonDown += (_, e) =>
        {
            if (e.OriginalSource is DependencyObject d && FindParent<ListBoxItem>(d) is { } row)
            {
                _syncing = true; // selecting for the menu must not load the song
                row.IsSelected = true;
                _syncing = false;
            }
        };
        SongList.KeyDown += (_, e) =>
        {
            if (e.Key == Key.Enter) { PlaySelected(); e.Handled = true; }
            else if (e.Key == Key.Delete && SongList.SelectedItem is SongEntry s) { RemoveFromList(s); e.Handled = true; }
        };
    }

    private void ToggleFavorite(SongEntry song)
    {
        song.IsFavorite = !song.IsFavorite;
        _settings.Favorites.RemoveAll(p => string.Equals(p, song.Path, StringComparison.OrdinalIgnoreCase));
        if (song.IsFavorite) _settings.Favorites.Add(song.Path);
        _settings.Save();
        if (FilterFav.IsChecked == true) ApplySearch();
    }

    // Hides a song from the list for this session. The file itself is never touched.
    private void RemoveFromList(SongEntry song)
    {
        _songs.Remove(song);
        ApplySearch();
        ShowHint($"Removed \"{song.Name}\" from the list. The file was not deleted.");
    }

    private CancellationTokenSource? _durationScan;

    // Reads each song's length in the background so the list can show it.
    private void ScanDurations()
    {
        _durationScan?.Cancel();
        var cts = _durationScan = new CancellationTokenSource();
        var songs = _songs.Where(s => s.Duration.Length == 0).ToList();
        Task.Run(() =>
        {
            foreach (var s in songs)
            {
                if (cts.IsCancellationRequested) return;
                string text;
                try { text = Format(MidiFile.Load(s.Path).Length); }
                catch { text = "?"; }
                Dispatcher.BeginInvoke(() => s.Duration = text, DispatcherPriority.Background);
            }
        }, cts.Token);
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

        if (_current != null) _current.IsCurrent = false;
        _current = song;
        song.IsCurrent = true;
        _settings.LastSong = song.Path;
        _settings.Save(); // survive a crash or a killed process
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
            _ when _settings.AutoFocusRoblox => $"Loaded. Press Play (AztroMidi switches to Roblox for you) or {_settings.Hotkeys.PlayPause} inside the game.",
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

    #endregion
}
