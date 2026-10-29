using System.Diagnostics;
using System.Runtime.InteropServices;

namespace AztroMidi.Core;

public enum PlayState { Stopped, Countdown, Playing, Paused }

public enum EventKind { Up = 0, SustainUp = 1, SustainDown = 2, Down = 3 }

public readonly record struct PlayEvent(double Time, EventKind Kind, int Pitch, int NoteId, int Velocity);

/// <summary>
/// High-precision playback engine. Runs on its own thread, keeps a song clock
/// that follows speed and humanized tempo drift, and fires key events on time.
/// </summary>
public sealed class Player : IDisposable
{
    [DllImport("winmm.dll")] private static extern uint timeBeginPeriod(uint ms);
    [DllImport("winmm.dll")] private static extern uint timeEndPeriod(uint ms);

    private readonly object _sync = new();
    private readonly AutoResetEvent _wake = new(false);
    private readonly Thread _thread;
    private volatile bool _disposed;

    private List<PlayEvent> _events = new();
    private int _index;
    private double _songTime;
    private double _countdownEnd;
    private Humanizer _humanizer = new(new HumanizeSettings { Enabled = false }, 0);
    private readonly Dictionary<int, int> _owner = new();
    private int _lastVelocityBucket = -1;
    private bool _sustainHeld;
    private bool _focusLost;
    private readonly KeyboardOutput _keyboard = new();
    private readonly PreviewOutput _preview = new();
    private INoteOutput _output;

    public AppSettings Settings { get; }
    public MidiFile? Song { get; private set; }
    public bool[] TrackEnabled { get; private set; } = Array.Empty<bool>();
    public int Transpose { get; set; }
    public PlayState State { get; private set; } = PlayState.Stopped;
    public double Length { get; private set; }
    public double Position { get { lock (_sync) return _songTime; } }
    public double CountdownRemaining { get; private set; }
    public bool WaitingForFocus => _focusLost;
    public int EventCount => _events.Count;

    /// <summary>Keys currently held, by MIDI pitch. Read by the visualizer.</summary>
    public bool[] Active { get; } = new bool[128];

    public event Action? StateChanged;
    public event Action? Finished;

    public Player(AppSettings settings)
    {
        Settings = settings;
        _output = OutputFor(settings.Output);
        _thread = new Thread(Run) { IsBackground = true, Name = "AztroMidi Player", Priority = ThreadPriority.Highest };
        _thread.Start();
    }

    private INoteOutput OutputFor(OutputMode mode) => mode switch
    {
        OutputMode.Preview => _preview,
        _ => _keyboard,
    };

    /// <summary>Switches where notes go, releasing anything held on the old output first.</summary>
    public void SetOutput(OutputMode mode)
    {
        lock (_sync)
        {
            ReleaseAll();
            if (_output == _preview && mode != OutputMode.Preview) _preview.Dispose();
            Settings.Output = mode;
            _output = OutputFor(mode);
            _focusLost = false;
        }
        StateChanged?.Invoke();
    }

    /// <summary>
    /// Keys coming up next, grouped into chords the way virtual piano sheets
    /// write them, e.g. "t", "[tuo]".
    /// </summary>
    public List<string> Upcoming(int count)
    {
        var result = new List<string>(count);
        lock (_sync)
        {
            int i = _index;
            while (result.Count < count && i < _events.Count)
            {
                if (_events[i].Kind != EventKind.Down) { i++; continue; }
                double t = _events[i].Time;
                var keys = new List<string>();
                while (i < _events.Count && _events[i].Time - t < 0.06)
                {
                    if (_events[i].Kind == EventKind.Down && KeyMapper.Lookup(_events[i].Pitch) is KeyStroke k)
                        keys.Add(k.Display);
                    i++;
                }
                keys = keys.Distinct().ToList();
                if (keys.Count == 1) result.Add(keys[0]);
                else if (keys.Count > 1) result.Add($"[{string.Concat(keys)}]");
            }
        }
        return result;
    }

    public void Load(MidiFile song)
    {
        lock (_sync)
        {
            StopInternal();
            Song = song;
            TrackEnabled = song.Tracks.Select(t => !(Settings.SkipDrums && t.IsDrums) && t.NoteCount > 0).ToArray();
            Transpose = Settings.AutoTranspose ? KeyMapper.BestTranspose(PlayablePitches(), Settings.Layout) : 0;
            BuildInternal();
        }
        StateChanged?.Invoke();
    }

    public IEnumerable<int> PlayablePitches()
    {
        if (Song == null) yield break;
        foreach (var n in Song.Notes)
            if (IncludeNote(n)) yield return n.Pitch;
    }

    private bool IncludeNote(MidiNote n) =>
        n.Track < TrackEnabled.Length && TrackEnabled[n.Track] && !(Settings.SkipDrums && n.Channel == 9);

    /// <summary>Re-creates the schedule (after track/transpose/humanize changes) keeping position.</summary>
    public void Rebuild()
    {
        lock (_sync)
        {
            double pos = _songTime;
            ReleaseAll();
            BuildInternal();
            SeekInternal(pos);
        }
        StateChanged?.Invoke();
    }

    private void BuildInternal()
    {
        var list = new List<PlayEvent>();
        Length = 0;
        if (Song == null) { _events = list; return; }

        _humanizer = new Humanizer(Settings.Humanize, Environment.TickCount);
        var notes = Song.Notes.Where(IncludeNote).ToList();
        double minHold = Settings.MinHoldMs / 1000.0;
        double maxHold = Math.Max(minHold, Settings.MaxHoldMs / 1000.0);

        // Cumulative breathing offset over time, used for sustain events too.
        var shiftMarks = new List<(double Time, double Shift)> { (0, 0) };
        double shift = 0;
        double latestEnd = 0;
        int noteId = 0;

        int i = 0;
        while (i < notes.Count)
        {
            // Notes that start within 25 ms of each other form one chord.
            double groupStart = notes[i].Start;
            int j = i;
            while (j < notes.Count && notes[j].Start - groupStart < 0.025) j++;

            var chord = new List<(MidiNote Note, int Pitch)>();
            var seen = new HashSet<int>();
            for (int k = i; k < j; k++)
            {
                var fitted = KeyMapper.Fit(notes[k].Pitch + Transpose, Settings.Layout, Settings.OutOfRange);
                if (fitted is int p && KeyMapper.Lookup(p) != null && seen.Add(p))
                    chord.Add((notes[k], p));
            }
            chord.Sort((a, b) => a.Pitch.CompareTo(b.Pitch));

            double rest = groupStart - latestEnd;
            if (latestEnd > 0 && rest > 0)
            {
                double add = _humanizer.Breath(rest);
                if (add > 0)
                {
                    shift += add;
                    shiftMarks.Add((groupStart, shift));
                }
            }

            var roll = _humanizer.ChordRoll(chord.Count);
            double groupJitter = _humanizer.TimingOffset();
            for (int c = 0; c < chord.Count; c++)
            {
                var (note, pitch) = chord[c];
                // Shared hand offset plus a smaller per-finger one.
                double start = note.Start + shift + groupJitter + _humanizer.TimingOffset() * 0.35 + roll[c];
                start = Math.Max(0, start);

                if (_humanizer.Mistake())
                {
                    int dir = _humanizer.MistakeDirection();
                    var wrong = KeyMapper.Fit(pitch + dir, Settings.Layout, OutOfRange.Skip);
                    if (wrong is int w && KeyMapper.Lookup(w) != null && w != pitch)
                    {
                        double len = _humanizer.MistakeLength();
                        int wid = ++noteId;
                        list.Add(new PlayEvent(start, EventKind.Down, w, wid, note.Velocity / 2 + 1));
                        list.Add(new PlayEvent(start + len * 0.7, EventKind.Up, w, wid, 0));
                        start += len;
                    }
                }

                double hold = Math.Clamp(note.Duration * _humanizer.HoldScale(), minHold, maxHold);
                int id = ++noteId;
                list.Add(new PlayEvent(start, EventKind.Down, pitch, id, _humanizer.Velocity(note.Velocity)));
                list.Add(new PlayEvent(start + hold, EventKind.Up, pitch, id, 0));
                latestEnd = Math.Max(latestEnd, note.End);
            }
            i = j;
        }

        if (Settings.Sustain == SustainMode.FollowPedal)
        {
            foreach (var s in Song.Sustain)
            {
                if (s.Track >= TrackEnabled.Length || !TrackEnabled[s.Track]) continue;
                double offset = shiftMarks.Last(m => m.Time <= s.Time).Shift;
                list.Add(new PlayEvent(s.Time + offset, s.Down ? EventKind.SustainDown : EventKind.SustainUp, 0, 0, 0));
            }
        }

        list.Sort((a, b) =>
        {
            int c = a.Time.CompareTo(b.Time);
            return c != 0 ? c : a.Kind.CompareTo(b.Kind);
        });
        _events = list;
        Length = list.Count == 0 ? 0 : list[^1].Time;
    }

    public void Play(bool useCountdown = true)
    {
        lock (_sync)
        {
            if (Song == null || _events.Count == 0) return;
            if (State == PlayState.Stopped && _index >= _events.Count) SeekInternal(0);
            if (useCountdown && Settings.CountdownSeconds > 0 && State != PlayState.Playing)
            {
                _countdownEnd = Now() + Settings.CountdownSeconds;
                State = PlayState.Countdown;
            }
            else
            {
                State = PlayState.Playing;
            }
        }
        _wake.Set();
        StateChanged?.Invoke();
    }

    public void Pause()
    {
        lock (_sync)
        {
            if (State is PlayState.Playing or PlayState.Countdown)
            {
                State = PlayState.Paused;
                ReleaseAll();
            }
        }
        StateChanged?.Invoke();
    }

    public void TogglePlay()
    {
        if (State is PlayState.Playing or PlayState.Countdown) Pause();
        else Play();
    }

    public void Stop()
    {
        lock (_sync) StopInternal();
        StateChanged?.Invoke();
    }

    private void StopInternal()
    {
        State = PlayState.Stopped;
        ReleaseAll();
        _songTime = 0;
        _index = 0;
        _focusLost = false;
    }

    public void Seek(double seconds)
    {
        lock (_sync)
        {
            ReleaseAll();
            SeekInternal(seconds);
        }
        StateChanged?.Invoke();
    }

    private void SeekInternal(double seconds)
    {
        _songTime = Math.Clamp(seconds, 0, Math.Max(0, Length));
        int lo = 0, hi = _events.Count;
        while (lo < hi)
        {
            int mid = (lo + hi) / 2;
            if (_events[mid].Time < _songTime) lo = mid + 1; else hi = mid;
        }
        _index = lo;
    }

    private static readonly Stopwatch Clock = Stopwatch.StartNew();
    private static double Now() => Clock.Elapsed.TotalSeconds;

    private void Run()
    {
        timeBeginPeriod(1);
        try
        {
            double last = Now();
            while (!_disposed)
            {
                double wait;
                bool finished = false;
                bool stateChanged = false;
                lock (_sync)
                {
                    double now = Now();
                    double dt = now - last;
                    last = now;

                    if (State == PlayState.Countdown)
                    {
                        CountdownRemaining = Math.Max(0, _countdownEnd - now);
                        if (CountdownRemaining <= 0)
                        {
                            State = PlayState.Playing;
                            stateChanged = true;
                        }
                        wait = 0.02;
                    }
                    else if (State != PlayState.Playing)
                    {
                        wait = 0.03;
                    }
                    else if (!RobloxReady())
                    {
                        if (!_focusLost)
                        {
                            _focusLost = true;
                            ReleaseAll();
                            stateChanged = true;
                        }
                        wait = 0.05;
                    }
                    else
                    {
                        if (_focusLost)
                        {
                            _focusLost = false;
                            stateChanged = true;
                            dt = 0; // do not jump ahead over the time spent unfocused
                        }

                        double speed = Math.Clamp(Settings.Speed, 0.1, 4.0);
                        _songTime += dt * speed * _humanizer.TempoFactor(_songTime);

                        while (_index < _events.Count && _events[_index].Time <= _songTime)
                            Fire(_events[_index++]);

                        if (_index >= _events.Count)
                        {
                            ReleaseAll();
                            State = PlayState.Stopped;
                            finished = true;
                            wait = 0.03;
                        }
                        else
                        {
                            wait = (_events[_index].Time - _songTime) / speed;
                        }
                    }
                }

                if (stateChanged) StateChanged?.Invoke();
                if (finished)
                {
                    StateChanged?.Invoke();
                    Finished?.Invoke();
                }

                // Sleep for the bulk of the gap, then spin for the last moment.
                if (wait > 0.003)
                    _wake.WaitOne(TimeSpan.FromMilliseconds(Math.Min(15, (wait - 0.0015) * 1000)));
                else if (wait > 0)
                    Thread.SpinWait(200);
            }
        }
        finally
        {
            timeEndPeriod(1);
        }
    }

    /// <summary>Whether notes can be delivered right now with the current output.</summary>
    private bool RobloxReady() => Settings.Output switch
    {
        OutputMode.Preview => true,
        _ => !Settings.OnlyWhenRobloxFocused || WindowGuard.RobloxIsFocused(),
    };

    private void Fire(PlayEvent e)
    {
        switch (e.Kind)
        {
            case EventKind.Down:
            {
                if (KeyMapper.Lookup(e.Pitch) is not KeyStroke key) return;
                if (_owner.ContainsKey(e.Pitch)) _output.NoteUp(e.Pitch, key);
                if (Settings.VelocityKeys)
                {
                    int bucket = KeyMapper.VelocityBucket(e.Velocity);
                    if (bucket != _lastVelocityBucket)
                    {
                        _output.Velocity(KeyMapper.VelocityKey(e.Velocity));
                        _lastVelocityBucket = bucket;
                    }
                }
                _output.NoteDown(e.Pitch, key, e.Velocity);
                _owner[e.Pitch] = e.NoteId;
                Active[e.Pitch] = true;
                break;
            }
            case EventKind.Up:
            {
                if (!_owner.TryGetValue(e.Pitch, out var id) || id != e.NoteId) return;
                if (KeyMapper.Lookup(e.Pitch) is KeyStroke key) _output.NoteUp(e.Pitch, key);
                _owner.Remove(e.Pitch);
                Active[e.Pitch] = false;
                break;
            }
            case EventKind.SustainDown when !_sustainHeld:
                _output.Sustain(true);
                _sustainHeld = true;
                break;
            case EventKind.SustainUp when _sustainHeld:
                _output.Sustain(false);
                _sustainHeld = false;
                break;
        }
    }

    private void ReleaseAll()
    {
        foreach (var pitch in _owner.Keys)
        {
            if (KeyMapper.Lookup(pitch) is KeyStroke key) _output.NoteUp(pitch, key);
            Active[pitch] = false;
        }
        bool hadKeys = _owner.Count > 0;
        _owner.Clear();
        if (_sustainHeld)
        {
            _output.Sustain(false);
            _sustainHeld = false;
            hadKeys = true;
        }
        // Only touch modifiers if we might have pressed them, so we never
        // release a Shift/Ctrl the user is physically holding for no reason.
        if (hadKeys || _lastVelocityBucket >= 0) _output.ReleaseModifiers();
        _lastVelocityBucket = -1;
        Array.Clear(Active);
    }

    public void Dispose()
    {
        _disposed = true;
        _wake.Set();
        _thread.Join(500);
        lock (_sync) ReleaseAll();
        _preview.Dispose();
    }
}
