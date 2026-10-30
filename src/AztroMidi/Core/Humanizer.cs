namespace AztroMidi.Core;

/// <summary>
/// Turns the mechanically exact MIDI timeline into something that feels played
/// by hand: loose timing, rolled chords, uneven key holds, breathing between
/// phrases, a wandering tempo and the occasional brushed neighbour key.
/// </summary>
public sealed class Humanizer
{
    private readonly HumanizeSettings _s;
    private readonly Random _rng;

    // Smooth tempo drift: sum of a few slow sine waves with random phases.
    private readonly double[] _driftFreq = new double[3];
    private readonly double[] _driftPhase = new double[3];

    public Humanizer(HumanizeSettings settings, int seed)
    {
        _s = settings;
        _rng = new Random(seed);
        for (int i = 0; i < 3; i++)
        {
            _driftFreq[i] = 0.02 + _rng.NextDouble() * 0.08; // 12-50 s periods
            _driftPhase[i] = _rng.NextDouble() * Math.PI * 2;
        }
    }

    public bool Enabled => _s.Enabled;

    // Gaussian sample via Box-Muller.
    private double Gauss()
    {
        double u1 = 1.0 - _rng.NextDouble();
        double u2 = _rng.NextDouble();
        return Math.Sqrt(-2.0 * Math.Log(u1)) * Math.Cos(2.0 * Math.PI * u2);
    }

    /// <summary>Bell-shaped offset in seconds, clamped so it never gets silly.</summary>
    public double TimingOffset()
    {
        if (!_s.Enabled || _s.TimingJitterMs <= 0) return 0;
        double ms = Math.Clamp(Gauss() * _s.TimingJitterMs / 2.0, -_s.TimingJitterMs, _s.TimingJitterMs);
        return ms / 1000.0;
    }

    /// <summary>
    /// Offsets for each note in a chord (sorted low to high). Hands roll
    /// mostly upward, sometimes downward, and the spread itself varies.
    /// </summary>
    public double[] ChordRoll(int count)
    {
        var result = new double[count];
        if (!_s.Enabled || _s.ChordRollMs <= 0 || count < 2) return result;
        bool upward = _rng.NextDouble() < 0.75;
        double total = _s.ChordRollMs * (0.35 + _rng.NextDouble() * 0.65);
        double step = total / (count - 1);
        for (int i = 0; i < count; i++)
        {
            int idx = upward ? i : count - 1 - i;
            double ms = step * i + (_rng.NextDouble() - 0.5) * step * 0.6;
            result[idx] = Math.Max(0, ms) / 1000.0;
        }
        return result;
    }

    public double HoldScale()
    {
        if (!_s.Enabled || _s.HoldVariancePercent <= 0) return 1;
        double v = _s.HoldVariancePercent / 100.0;
        return Math.Clamp(1 + Gauss() * v / 2, 1 - v, 1 + v);
    }

    public int Velocity(int velocity)
    {
        if (!_s.Enabled || _s.VelocityVariance <= 0) return velocity;
        return Math.Clamp(velocity + (int)Math.Round(Gauss() * _s.VelocityVariance / 2), 1, 127);
    }

    /// <summary>Extra pause after a rest, in seconds. Applied cumulatively.</summary>
    public double Breath(double restSeconds)
    {
        if (!_s.Enabled || _s.PhraseBreathMs <= 0 || restSeconds < 0.45) return 0;
        double weight = Math.Min(1.0, (restSeconds - 0.45) / 1.5 + 0.3);
        return _rng.NextDouble() * _s.PhraseBreathMs * weight / 1000.0;
    }

    public bool Mistake() =>
        _s.Enabled && _s.MistakeChancePercent > 0 && _rng.NextDouble() * 100 < _s.MistakeChancePercent;

    public int MistakeDirection() => _rng.NextDouble() < 0.5 ? -1 : 1;

    public double MistakeLength() => (18 + _rng.NextDouble() * 30) / 1000.0;

    /// <summary>Tempo multiplier at the given song time (e.g. 0.98 .. 1.02).</summary>
    public double TempoFactor(double songTime)
    {
        if (!_s.Enabled || _s.TempoDriftPercent <= 0) return 1;
        double sum = 0;
        for (int i = 0; i < 3; i++)
            sum += Math.Sin(songTime * _driftFreq[i] * Math.PI * 2 + _driftPhase[i]);
        return 1 + sum / 3.0 * _s.TempoDriftPercent / 100.0;
    }
}
