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

    /// <summary>True for the song that is loaded in the player.</summary>
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

    public string TileGlyph => _current ? "" : "";
    public Brush TileBrush => (Brush)Application.Current.FindResource(_current ? "Brand" : "Surface2");
    public Brush TileInk => (Brush)Application.Current.FindResource(_current ? "Ink" : "InkMuted");

    public string StarGlyph => _favorite ? "" : "";
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

    /// <summary>Sets the value without notifying the player (used for bulk updates).</summary>
    public void SetQuiet(bool value)
    {
        _enabled = value;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Enabled)));
    }

    public event Action<TrackItem>? Changed;
    public event PropertyChangedEventHandler? PropertyChanged;
}
