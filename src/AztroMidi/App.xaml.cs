using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace AztroMidi;

public partial class App : Application
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static readonly IntPtr HWND_BROADCAST = (IntPtr)0xFFFF;

    /// <summary>Broadcast by a second copy of the app to ask the running one to show itself.</summary>
    public static readonly uint ShowMessage = RegisterWindowMessage("AztroMidi.ShowRunningInstance");

    private Mutex? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        // Only one AztroMidi at a time: global hotkeys can belong to a single app,
        // so a second copy would silently lose F1/F7 to the first one.
#if DEBUG
        const string lockName = "AztroMidi.SingleInstance.Debug"; // dev builds can run next to the installed app
#else
        const string lockName = "AztroMidi.SingleInstance";
#endif
        _single = new Mutex(true, lockName, out bool first);
        if (!first)
        {
            PostMessage(HWND_BROADCAST, ShowMessage, IntPtr.Zero, IntPtr.Zero);
            Shutdown();
            return;
        }

        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);
    }

    protected override void OnExit(ExitEventArgs e)
    {
        _single?.Dispose();
        base.OnExit(e);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "AztroMidi", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
