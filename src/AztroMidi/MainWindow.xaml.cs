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

public partial class MainWindow : Window
{
    private readonly AppSettings _settings = AppSettings.Load();
    private readonly Player _player;
    private readonly DispatcherTimer _frame;
    private HotkeyManager? _hotkeys;
    private MiniPlayer? _mini;

    private List<SongEntry> _songs = new();
    private List<TrackItem> _tracks = new();
    private SongEntry? _current;
    private bool _seeking;
    private bool _syncing;
    private string _hint = "";
    private string _lastNext = "";
    private int _frameCount;
    private readonly List<Action> _refreshers = new();

    public Player Player => _player;

    public MainWindow()
    {
        InitializeComponent();
        RestoreWindowPlacement();
        _player = new Player(_settings);
        _player.StateChanged += () => Dispatcher.BeginInvoke(UpdateState);
        _player.Finished += () => Dispatcher.BeginInvoke(OnSongFinished);

        Piano.SetSource(_player.Active);

        _frame = new DispatcherTimer(DispatcherPriority.Render) { Interval = TimeSpan.FromMilliseconds(16) };
        _frame.Tick += (_, _) => OnFrame();
        _frame.Start();

        var version = Assembly.GetExecutingAssembly().GetName().Version;
        VersionText.Text = $"v{version?.Major}.{version?.Minor}";
        AboutText.Text =
            $"AztroMidi {VersionText.Text} — a lightweight MIDI auto-player for Roblox pianos.\n" +
            $"Settings file: {AppSettings.FilePath}\n" +
            "Layout: standard Roblox virtual piano (61 keys C2–C7) with the Ctrl 88-key extension.";

        BuildHumanizePage();
        BuildSettingsPage();
        ApplySettingsToUi();
        BuildSongMenu();
        LoadLibrary();
        RestoreLastSong();
        ShowPage(_settings.LastPage);
        PreviewKeyDown += OnShortcut;
        if (App.StartupFiles.Length > 0) Loaded += (_, _) => AddFiles(App.StartupFiles);

        SourceInitialized += (_, _) =>
        {
            var hwnd = new WindowInteropHelper(this).Handle;
            RoundCorners(hwnd);
            HwndSource.FromHwnd(hwnd).AddHook(OnWindowMessage);
            _hotkeys = new HotkeyManager(hwnd);
            RegisterHotkeys();
        };
        StateChanged += (_, _) =>
        {
            MaxButton.Content = WindowState == WindowState.Maximized ? "\uE923" : "\uE922";
            MaxButton.ToolTip = WindowState == WindowState.Maximized ? "Restore" : "Maximize";
            ApplyMaximizedPadding();
        };
        Closing += (_, _) =>
        {
            SaveWindowPlacement();
            _settings.Save();
            _mini?.Close();
            _hotkeys?.Dispose();
            _player.Dispose();
        };
    }

    #region Window chrome

    [DllImport("dwmapi.dll")]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    private static void RoundCorners(IntPtr hwnd)
    {
        int round = 2; // DWMWCP_ROUND (Windows 11)
        DwmSetWindowAttribute(hwnd, 33, ref round, sizeof(int));
        int dark = 1;
        DwmSetWindowAttribute(hwnd, 20, ref dark, sizeof(int));
    }

    // Another copy of AztroMidi was started: show this window instead.
    private IntPtr OnWindowMessage(IntPtr hwnd, int msg, IntPtr wParam, IntPtr lParam, ref bool handled)
    {
        if ((uint)msg == App.ShowMessage)
        {
            ExitMiniMode();
            handled = true;
            if (wParam == 1) OpenHandoffFiles();
            else ShowHint("AztroMidi is already running, so this window was brought back.");
        }
        return IntPtr.Zero;
    }

    // Reopens where the user left the window, as long as it is still on a screen.
    private void RestoreWindowPlacement()
    {
        var work = SystemParameters.WorkArea;
        var s = _settings;
        if (!double.IsNaN(s.WindowWidth) && !double.IsNaN(s.WindowHeight))
        {
            Width = Math.Max(MinWidth, s.WindowWidth);
            Height = Math.Max(MinHeight, s.WindowHeight);
        }
        // Fit smaller screens such as 1366x768 laptops.
        Width = Math.Min(Width, work.Width - 16);
        Height = Math.Min(Height, work.Height - 16);

        var screen = new Rect(SystemParameters.VirtualScreenLeft, SystemParameters.VirtualScreenTop,
                              SystemParameters.VirtualScreenWidth, SystemParameters.VirtualScreenHeight);
        if (!double.IsNaN(s.WindowLeft) && !double.IsNaN(s.WindowTop) &&
            screen.Contains(new Point(s.WindowLeft + 80, s.WindowTop + 20)))
        {
            WindowStartupLocation = WindowStartupLocation.Manual;
            Left = s.WindowLeft;
            Top = s.WindowTop;
        }
        if (s.WindowMaximized) WindowState = WindowState.Maximized;
    }

    private void SaveWindowPlacement()
    {
        var bounds = WindowState == WindowState.Normal ? new Rect(Left, Top, Width, Height) : RestoreBounds;
        if (bounds.IsEmpty) return;
        _settings.WindowLeft = bounds.Left;
        _settings.WindowTop = bounds.Top;
        _settings.WindowWidth = bounds.Width;
        _settings.WindowHeight = bounds.Height;
        _settings.WindowMaximized = WindowState == WindowState.Maximized;
    }

    // Files from a second copy started with "Open with" while this one was already running.
    private void OpenHandoffFiles()
    {
        try
        {
            var files = File.ReadAllLines(App.HandoffFile).Where(File.Exists).ToList();
            File.Delete(App.HandoffFile);
            if (files.Count > 0) AddFiles(files);
        }
        catch (IOException) { }
    }

    [DllImport("user32.dll")] private static extern int GetSystemMetrics(int index);

    // A maximized window with a custom title bar is placed slightly outside the screen (by the
    // invisible resize frame). Pad the content by that amount so nothing is cut off and the window
    // sits exactly inside the work area.
    private void ApplyMaximizedPadding()
    {
        if (WindowState != WindowState.Maximized)
        {
            RootGrid.Margin = new Thickness(0);
            return;
        }
        const int SM_CXSIZEFRAME = 32, SM_CYSIZEFRAME = 33, SM_CXPADDEDBORDER = 92;
        double scale = VisualTreeHelper.GetDpi(this).DpiScaleX;
        double x = (GetSystemMetrics(SM_CXSIZEFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER)) / scale;
        double y = (GetSystemMetrics(SM_CYSIZEFRAME) + GetSystemMetrics(SM_CXPADDEDBORDER)) / scale;
        RootGrid.Margin = new Thickness(x, y, x, y);
    }

    private void OnMinimize(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximize(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnClose(object sender, RoutedEventArgs e) => Close();

    private void OnPin(object sender, RoutedEventArgs e)
    {
        _settings.AlwaysOnTop = !_settings.AlwaysOnTop;
        ApplyTopmost();
    }

    private void ApplyTopmost()
    {
        Topmost = _settings.AlwaysOnTop;
        PinButton.Foreground = _settings.AlwaysOnTop ? (Brush)FindResource("Brand") : (Brush)FindResource("InkMuted");
    }

    private void OnNav(object sender, RoutedEventArgs e)
    {
        if (PagePlayer == null) return;
        PagePlayer.Visibility = NavPlayer.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageHumanize.Visibility = NavHumanize.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        PageSettings.Visibility = NavSettings.IsChecked == true ? Visibility.Visible : Visibility.Collapsed;
        _settings.LastPage = NavHumanize.IsChecked == true ? "Humanize" : NavSettings.IsChecked == true ? "Settings" : "Player";
    }

    // Space play/pause, arrows seek and change speed, Ctrl+F search, Ctrl+O open, Ctrl+1..3 pages.
    private void OnShortcut(object sender, KeyEventArgs e)
    {
        bool ctrl = Keyboard.Modifiers.HasFlag(ModifierKeys.Control);
        bool typing = Keyboard.FocusedElement is TextBox;

        if (ctrl)
        {
            switch (e.Key)
            {
                case Key.F: NavPlayer.IsChecked = true; SearchBox.Focus(); SearchBox.SelectAll(); break;
                case Key.O: OnOpenFile(this, e); break;
                case Key.D1: NavPlayer.IsChecked = true; break;
                case Key.D2: NavHumanize.IsChecked = true; break;
                case Key.D3: NavSettings.IsChecked = true; break;
                case Key.M: EnterMiniMode(); break;
                default: return;
            }
            e.Handled = true;
            return;
        }

        if (e.Key == Key.Escape && typing)
        {
            SearchBox.Text = "";
            SongList.Focus();
            e.Handled = true;
            return;
        }
        if (typing || Keyboard.FocusedElement is ComboBox) return;

        switch (e.Key)
        {
            case Key.Space: TogglePlay(); break;
            case Key.Left: SeekBy(-5); break;
            case Key.Right: SeekBy(5); break;
            case Key.OemPlus or Key.Add: ChangeSpeed(0.05); break;
            case Key.OemMinus or Key.Subtract: ChangeSpeed(-0.05); break;
            default: return;
        }
        e.Handled = true;
    }

    private void SeekBy(double seconds)
    {
        if (_player.Song == null) return;
        _player.Seek(_player.Position + seconds);
        ShowHint($"{Format(_player.Position)} / {Format(_player.Length)}");
    }

    private void ShowPage(string page)
    {
        var tab = page switch { "Humanize" => NavHumanize, "Settings" => NavSettings, _ => NavPlayer };
        tab.IsChecked = true;
    }

    #endregion

    #region Mini player

    private void OnMini(object sender, RoutedEventArgs e)
    {
        MiniButton.IsChecked = false; // it is an action, not a page
        EnterMiniMode();
    }

    public void EnterMiniMode()
    {
        _mini ??= new MiniPlayer(this, _settings);
        _mini.Show();
        Hide();
    }

    public void ExitMiniMode()
    {
        _mini?.Hide();
        Show();
        if (WindowState == WindowState.Minimized) WindowState = WindowState.Normal;
        Activate();
    }

    private void ToggleMiniMode()
    {
        if (_mini?.IsVisible == true) ExitMiniMode();
        else EnterMiniMode();
    }

    #endregion
}
