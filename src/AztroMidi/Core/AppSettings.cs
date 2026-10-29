using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AztroMidi.Core;

public enum SustainMode { Off, FollowPedal }

public sealed class HumanizeSettings
{
    public bool Enabled { get; set; } = true;
    /// <summary>Random timing offset per note, in milliseconds (bell-shaped).</summary>
    public double TimingJitterMs { get; set; } = 8;
    /// <summary>Max delay between notes of the same chord, like a rolling hand.</summary>
    public double ChordRollMs { get; set; } = 14;
    /// <summary>Slow, smooth tempo wander in percent.</summary>
    public double TempoDriftPercent { get; set; } = 2.5;
    /// <summary>Random variation of how long keys are held, in percent.</summary>
    public double HoldVariancePercent { get; set; } = 12;
    /// <summary>Random velocity change (only matters with velocity keys on).</summary>
    public int VelocityVariance { get; set; } = 6;
    /// <summary>Small hesitation added after long rests (phrase breathing), in ms.</summary>
    public double PhraseBreathMs { get; set; } = 35;
    /// <summary>Chance (0-5 %) of brushing a neighbour key right before the real one.</summary>
    public double MistakeChancePercent { get; set; } = 0;
}

public sealed class HotkeySettings
{
    public string PlayPause { get; set; } = "F1";
    public string Stop { get; set; } = "F2";
    public string SlowDown { get; set; } = "F3";
    public string SpeedUp { get; set; } = "F4";
    public string NextSong { get; set; } = "F5";
    public string ToggleHumanize { get; set; } = "F6";
    public string ToggleMini { get; set; } = "F7";
}

public sealed class AppSettings
{
    public string LibraryFolder { get; set; } = "";
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public KeyLayout Layout { get; set; } = KeyLayout.Keys88;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OutOfRange OutOfRange { get; set; } = OutOfRange.Fold;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public SustainMode Sustain { get; set; } = SustainMode.Off;
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public OutputMode Output { get; set; } = OutputMode.Keyboard;
    public bool VelocityKeys { get; set; }
    public bool AutoTranspose { get; set; } = true;
    public bool SkipDrums { get; set; } = true;
    public bool OnlyWhenRobloxFocused { get; set; } = true;
    public bool AutoFocusRoblox { get; set; } = true;
    public bool AlwaysOnTop { get; set; }
    public bool Loop { get; set; }
    public bool AutoNext { get; set; }
    public int CountdownSeconds { get; set; } = 3;
    public double Speed { get; set; } = 1.0;
    public double MinHoldMs { get; set; } = 25;
    public double MaxHoldMs { get; set; } = 4000;
    public string LastSong { get; set; } = "";
    public List<string> Favorites { get; set; } = new();
    public bool MiniSnapToRoblox { get; set; } = true;
    public double MiniOpacity { get; set; } = 0.94;
    public double MiniLeft { get; set; } = double.NaN;
    public double MiniTop { get; set; } = double.NaN;
    public HumanizeSettings Humanize { get; set; } = new();
    public HotkeySettings Hotkeys { get; set; } = new();

    private static readonly JsonSerializerOptions Json = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    /// <summary>
    /// Settings live next to the exe so the app stays portable and does not
    /// write to the system drive; falls back to AppData if that is read-only.
    /// </summary>
    public static string FilePath
    {
        get
        {
            var local = Path.Combine(AppContext.BaseDirectory, "settings.json");
            if (CanWrite(AppContext.BaseDirectory)) return local;
            var dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "AztroMidi");
            Directory.CreateDirectory(dir);
            return Path.Combine(dir, "settings.json");
        }
    }

    private static bool CanWrite(string dir)
    {
        try
        {
            var probe = Path.Combine(dir, ".write_test");
            File.WriteAllText(probe, "");
            File.Delete(probe);
            return true;
        }
        catch { return false; }
    }

    public static AppSettings Load()
    {
        try
        {
            var path = FilePath;
            if (File.Exists(path))
            {
                var s = JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), Json) ?? new AppSettings();
                if (s.Output == OutputMode.Background) s.Output = OutputMode.Keyboard; // removed in 1.3
                return s;
            }
        }
        catch { /* corrupted file: start fresh */ }
        return new AppSettings();
    }

    public void Save()
    {
        try { File.WriteAllText(FilePath, JsonSerializer.Serialize(this, Json)); }
        catch { /* not fatal */ }
    }
}
