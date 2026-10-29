using System.ComponentModel;
using System.IO;

namespace AztroMidi;

public sealed class SongEntry
{
    public required string Path { get; init; }
    public string Name => System.IO.Path.GetFileNameWithoutExtension(Path);
    public string Folder { get; init; } = "";

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
