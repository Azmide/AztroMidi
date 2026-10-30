using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Media;

namespace AztroMidi;

public sealed class SongEntry : INotifyPropertyChanged
{
    private bool _favorite;

    public required string Path { get; init; }
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    public string Folder { get; init; } = "";

    public bool IsFavorite
    {
        get => _favorite;
        set
        {
            if (_favorite == value) return;
            _favorite = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StarGlyph)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(StarBrush)));
        }
    }

    private bool _current;

    // True for the song that is loaded in the player.
    public bool IsCurrent
    {
        get => _current;
        set
        {
            if (_current == value) return;
            _current = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TileGlyph)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TileBrush)));
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TileInk)));
        }
    }

    public string TileGlyph => _current ? "\uE995" : "\uEC4F";
    public Brush TileBrush => (Brush)Application.Current.FindResource(_current ? "Brand" : "Surface2");
    public Brush TileInk => (Brush)Application.Current.FindResource(_current ? "Ink" : "InkMuted");

    private string _duration = "";

    // Song length such as "3:42", filled in the background after the library loads.
    public string Duration
    {
        get => _duration;
        set
        {
            if (_duration == value) return;
            _duration = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Duration)));
        }
    }

    public string StarGlyph => _favorite ? "\uE735" : "\uE734";
    public Brush StarBrush => (Brush)Application.Current.FindResource(_favorite ? "Warning" : "InkFaint");

    public event PropertyChangedEventHandler? PropertyChanged;

    public static SongEntry From(string path, string? root)
    {
        string folder = System.IO.Path.GetDirectoryName(path) ?? "";
        if (!string.IsNullOrEmpty(root) && folder.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            folder = folder[root.Length..].TrimStart('\\', '/');
        return new SongEntry { Path = path, Folder = string.IsNullOrEmpty(folder) ? new DirectoryInfo(System.IO.Path.GetDirectoryName(path) ?? "").Name : folder };
    }
}

public sealed class TrackItem : INotifyPropertyChanged
{
    private bool _enabled;
    public int Index { get; init; }
    public required string Name { get; init; }
    public required string Detail { get; init; }

    public bool Enabled
    {
        get => _enabled;
        set
        {
            if (_enabled == value) return;
            _enabled = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
            Changed?.Invoke(this);
        }
    }

    // Sets the value without notifying the player (used for bulk updates).
    public void SetQuiet(bool value)
    {
        _enabled = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
    }

    public event Action<TrackItem>? Changed;
    public event PropertyChangedEventHandler? PropertyChanged;
}
