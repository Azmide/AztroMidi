namespace AztroMidi.Core;

public static class GeneralMidi
{
    private static readonly string[] Families =
    {
        "Piano", "Chromatic Perc.", "Organ", "Guitar", "Bass", "Strings", "Ensemble", "Brass",
        "Reed", "Pipe", "Synth Lead", "Synth Pad", "Synth FX", "Ethnic", "Percussive", "Sound FX",
    };

    private static readonly string[] NoteNames = { "C", "C#", "D", "D#", "E", "F", "F#", "G", "G#", "A", "A#", "B" };

    public static string InstrumentName(int program) =>
        program is >= 0 and < 128 ? $"{Families[program / 8]} #{program + 1}" : "";

    public static string NoteName(int pitch) => $"{NoteNames[pitch % 12]}{pitch / 12 - 1}";

    public static bool IsBlack(int pitch) => (pitch % 12) is 1 or 3 or 6 or 8 or 10;
}
