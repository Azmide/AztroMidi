namespace AztroMidi.Core;

public readonly record struct KeyStroke(ushort Vk, Mods Mods, char Label)
{
    public string Display => Mods switch
    {
        Mods.Ctrl => $"^{char.ToLowerInvariant(Label)}",
        _ => Label.ToString(),
    };
}

public enum KeyLayout { Keys61, Keys88 }

public enum OutOfRange { Fold, Skip }

/// <summary>
/// Maps MIDI pitches to the standard Roblox virtual piano layout
/// (61 keys, C2-C7) with the common Ctrl-based 88-key extension.
/// </summary>
public static class KeyMapper
{
    // C2 (36) .. C7 (96). Upper-case and symbols are played with Shift.
    public const string Layout61 = "1!2@34$5%6^78*9(0qQwWeErtTyYuiIoOpPasSdDfgGhHjJklLzZxcCvVbBnm";
    public const int Low61 = 36;
    public const int High61 = 96;

    // 88-key extension: A0..B1 and C#7..C8 are played with Ctrl.
    public const string LowExt = "1234567890qwert";
    public const string HighExt = "yuiopasdfghj";
    public const int Low88 = 21;
    public const int High88 = 108;

    // 32-step velocity selection used by velocity-aware Roblox pianos (Alt + key).
    public const string VelocityKeys = "1234567890qwertyuiopasdfghjklzxc";

    private const string ShiftedDigits = ")!@#$%^&*(";

    private static readonly Dictionary<int, KeyStroke> Map = Build();

    private static Dictionary<int, KeyStroke> Build()
    {
        var map = new Dictionary<int, KeyStroke>();
        for (int i = 0; i < Layout61.Length; i++)
            map[Low61 + i] = FromChar(Layout61[i]);
        for (int i = 0; i < LowExt.Length; i++)
            map[Low88 + i] = new KeyStroke(InputSender.VkFromChar(LowExt[i]), Mods.Ctrl, LowExt[i]);
        for (int i = 0; i < HighExt.Length; i++)
            map[High61 + 1 + i] = new KeyStroke(InputSender.VkFromChar(HighExt[i]), Mods.Ctrl, HighExt[i]);
        return map;
    }

    private static KeyStroke FromChar(char c)
    {
        int shifted = ShiftedDigits.IndexOf(c);
        if (shifted >= 0)
            return new KeyStroke(InputSender.VkFromChar((char)('0' + shifted)), Mods.Shift, c);
        if (char.IsUpper(c))
            return new KeyStroke(InputSender.VkFromChar(c), Mods.Shift, c);
        return new KeyStroke(InputSender.VkFromChar(c), Mods.None, c);
    }

    public static (int Low, int High) Range(KeyLayout layout) =>
        layout == KeyLayout.Keys88 ? (Low88, High88) : (Low61, High61);

    /// <summary>Returns the playable pitch after folding, or null if the note is skipped.</summary>
    public static int? Fit(int pitch, KeyLayout layout, OutOfRange mode)
    {
        var (low, high) = Range(layout);
        if (pitch >= low && pitch <= high) return pitch;
        if (mode == OutOfRange.Skip) return null;
        while (pitch < low) pitch += 12;
        while (pitch > high) pitch -= 12;
        return pitch;
    }

    public static KeyStroke? Lookup(int pitch) => Map.TryGetValue(pitch, out var k) ? k : null;

    public static KeyStroke VelocityKey(int velocity)
    {
        int idx = Math.Clamp(velocity * VelocityKeys.Length / 128, 0, VelocityKeys.Length - 1);
        char c = VelocityKeys[idx];
        return new KeyStroke(InputSender.VkFromChar(c), Mods.Alt, c);
    }

    public static int VelocityBucket(int velocity) =>
        Math.Clamp(velocity * VelocityKeys.Length / 128, 0, VelocityKeys.Length - 1);

    /// <summary>
    /// Finds the transpose (-12..+12) that keeps the most notes inside the
    /// selected range, preferring the smallest shift on ties.
    /// </summary>
    public static int BestTranspose(IEnumerable<int> pitches, KeyLayout layout)
    {
        var (low, high) = Range(layout);
        var list = pitches.ToList();
        if (list.Count == 0) return 0;
        int best = 0;
        (int InRange, int White, int Move) bestScore = (-1, -1, 0);
        foreach (int t in Enumerable.Range(0, 25).Select(i => i % 2 == 0 ? i / 2 : -(i + 1) / 2))
        {
            int inRange = 0, white = 0;
            foreach (var p in list)
            {
                int q = p + t;
                if (q >= low && q <= high)
                {
                    inRange++;
                    if (!GeneralMidi.IsBlack(q)) white++;
                }
            }
            // Range coverage first, then fewer Shift (black) keys. Candidates are
            // visited from the smallest shift outward, so ties keep the smaller move.
            var score = (inRange, white, 0);
            if (score.inRange > bestScore.InRange ||
                (score.inRange == bestScore.InRange && score.white > bestScore.White))
            {
                bestScore = score;
                best = t;
            }
        }
        return best;
    }
}
