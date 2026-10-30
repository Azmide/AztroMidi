using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Interop;
using System.Windows.Media;
using System.Windows.Threading;
using AztroMidi.Core;

namespace AztroMidi;

// Small always-on-top controller that sits in the top-right corner of the Roblox window. It never
// takes keyboard focus, so clicking it keeps Roblox focused and the notes keep landing in the game.
public partial class MiniPlayer : Window
{
    [DllImport("user32.dll")] private static extern int GetWindowLong(IntPtr hWnd, int index);
    [DllImport("user32.dll")] private static extern int SetWindowLong(IntPtr hWnd, int index, int value);

    private const int GWL_EXSTYLE = -20;
    private const int WS_EX_TOOLWINDOW = 0x00000080;
    private const int WS_EX_NOACTIVATE = 0x08000000;

    private const double MiniWidth = 300;
    private const double MiniHeight = 190;

    private readonly MainWindow _main;
    private readonly AppSettings _settings;
    private readonly DispatcherTimer _timer;
    private string _lastNext = "";

    public MiniPlayer(MainWindow main, AppSettings settings)
    {
        InitializeComponent();
        _main = main;
        _settings = settings;
        Opacity = Math.Clamp(settings.MiniOpacity, 0.4, 1.0);

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            SetWindowLong(hwnd, GWL_EXSTYLE, GetWindowLong(hwnd, GWL_EXSTYLE) | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW);
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(100) };
        _timer.Tick += (_, _) => Refresh();
        IsVisibleChanged += (_, _) =>
        {
            if (IsVisible) { PlaceWindow(force: true); Refresh(); _timer.Start(); }
            else _timer.Stop();
        };
        UpdateSnapButton();
    }

    // ------------------------------------------------------------------ position

    private void PlaceWindow(bool force = false)
    {
        // The size is fixed on purpose. Letting WPF size this layered window to its
        // content made Windows squeeze it to 2 px wide when it was positioned before
        // the first layout pass, which looked like the mini player had vanished.
        if (Width != MiniWidth) Width = MiniWidth;
        if (Height != MiniHeight) Height = MiniHeight;

        if (_settings.MiniSnapToRoblox && WindowGuard.RobloxClientRect() is { } r)
        {
            var source = PresentationSource.FromVisual(this);
            var toDip = source?.CompositionTarget?.TransformFromDevice ?? Matrix.Identity;
            var topRight = toDip.Transform(new Point(r.Right, r.Top));
            // Leave room for Roblox's own top-right buttons.
            double left = topRight.X - MiniWidth - 6;
            double top = topRight.Y + 46;
            if (Math.Abs(Left - left) > 0.5 || Math.Abs(Top - top) > 0.5) { Left = left; Top = top; }
            return;
        }
        if (!force) return;

        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        var saved = new Rect(_settings.MiniLeft, _settings.MiniTop, MiniWidth, MiniHeight);
        // Only reuse the saved spot if most of the player would still be on a screen.
        if (!double.IsNaN(saved.X) && !double.IsNaN(saved.Y) &&
            screen.Contains(new Point(saved.X + 40, saved.Y + 20)) && screen.Contains(new Point(saved.Right - 40, saved.Y + 20)))
        {
            Left = saved.X;
            Top = saved.Y;
        }
        else
        {
            var area = SystemParameters.WorkArea;
            Left = area.Right - MiniWidth - 12;
            Top = area.Top + 60;
        }
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState != MouseButtonState.Pressed) return;
        bool wasSnapped = _settings.MiniSnapToRoblox;
        double l = Left, t = Top;
        DragMove();
        if (Math.Abs(Left - l) < 2 && Math.Abs(Top - t) < 2) return; // just a click
        // Dragging means the user wants it somewhere specific.
        if (wasSnapped) _settings.MiniSnapToRoblox = false;
        _settings.MiniLeft = Left;
        _settings.MiniTop = Top;
        UpdateSnapButton();
    }

    private void OnSnap(object sender, RoutedEventArgs e)
    {
        _settings.MiniSnapToRoblox = !_settings.MiniSnapToRoblox;
        UpdateSnapButton();
        PlaceWindow(force: true);
        _main.ShowHint(_settings.MiniSnapToRoblox
            ? "Mini player sticks to the Roblox window."
            : "Mini player stays where you drag it.");
    }

    private void UpdateSnapButton() =>
        SnapButton.Foreground = (Brush)FindResource(_settings.MiniSnapToRoblox ? "Brand" : "InkMuted");

    // ------------------------------------------------------------------ live state

    private void Refresh()
    {
        PlaceWindow();
        var player = _main.Player;

        TitleText.Text = player.Song?.Title ?? "No song loaded";
        double len = Math.Max(0.1, player.Length);
        double pos = Math.Clamp(player.Position, 0, len);
        ProgressFill.Width = Math.Max(0, (Shell.ActualWidth - Shell.Padding.Left - Shell.Padding.Right - 2) * pos / len);
        PosText.Text = MainWindow.Format(pos);
        LenText.Text = MainWindow.Format(player.Length);
        SpeedText.Text = $"{_settings.Speed:0.00}x";

        var (text, brush) = MainWindow.Describe(player);
        StatusText.Text = text;
        StatusDot.Fill = (Brush)FindResource(brush);
        bool active = player.State is PlayState.Playing or PlayState.Countdown;
        PlayIcon.Text = active ? "\uE769" : "\uE768";

        var next = player.Upcoming(8);
        var joined = string.Join(" ", next);
        if (joined != _lastNext)
        {
            _lastNext = joined;
            NextKeys.Inlines.Clear();
            if (next.Count == 0)
            {
                NextKeys.Inlines.Add(new Run("—") { Foreground = (Brush)FindResource("InkFaint") });
            }
            for (int i = 0; i < next.Count; i++)
            {
                NextKeys.Inlines.Add(new Run(next[i] + " ")
                {
                    Foreground = (Brush)FindResource(i == 0 ? "Brand" : i < 3 ? "Ink" : "InkMuted"),
                    FontWeight = i == 0 ? FontWeights.Bold : FontWeights.Normal,
                });
            }
        }
    }

    // ------------------------------------------------------------------ buttons

    private void OnPlay(object sender, RoutedEventArgs e) => _main.TogglePlay();
    private void OnStop(object sender, RoutedEventArgs e) => _main.Player.Stop();
    private void OnPrev(object sender, RoutedEventArgs e) => _main.StepSong(-1, _main.IsPlaying());
    private void OnNext(object sender, RoutedEventArgs e) => _main.StepSong(1, _main.IsPlaying());
    private void OnSlower(object sender, RoutedEventArgs e) => _main.ChangeSpeed(-0.05);
    private void OnFaster(object sender, RoutedEventArgs e) => _main.ChangeSpeed(0.05);
    private void OnExpand(object sender, RoutedEventArgs e) => _main.ExitMiniMode();
}
