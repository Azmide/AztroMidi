using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace AztroMidi;

public partial class App : Application
{
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern uint RegisterWindowMessage(string name);
    [DllImport("user32.dll")] private static extern bool PostMessage(IntPtr hWnd, uint msg, IntPtr wParam, IntPtr lParam);

    private static readonly IntPtr HWND_BROADCAST = (IntPtr)0xFFFF;

    // A second copy of the app posts this to ask the running one to show itself.
    // wParam = 1 means "and open the files listed in HandoffFile".
    public static readonly uint ShowMessage = RegisterWindowMessage("AztroMidi.ShowRunningInstance");

    public static string HandoffFile => Path.Combine(Path.GetTempPath(), "AztroMidi-open.txt");

    // MIDI files passed on the command line ("Open with", or dropped on the exe).
    public static string[] StartupFiles { get; private set; } = Array.Empty<string>();

    private Mutex? _single;

    protected override void OnStartup(StartupEventArgs e)
    {
        StartupFiles = e.Args.Where(File.Exists).ToArray();

        // One copy at a time: global hotkeys belong to whoever registered them first,
        // so a second copy would silently lose F1/F7.
#if DEBUG
        const string lockName = "AztroMidi.SingleInstance.Debug"; // dev builds can run next to the installed app
#else
        const string lockName = "AztroMidi.SingleInstance";
#endif
        _single = new Mutex(true, lockName, out bool first);
        if (!first)
        {
            bool handoff = false;
            if (StartupFiles.Length > 0)
            {
                try
                {
                    File.WriteAllLines(HandoffFile, StartupFiles);
                    handoff = true;
                }
                catch (IOException) { }
            }
            PostMessage(HWND_BROADCAST, ShowMessage, handoff ? 1 : 0, IntPtr.Zero);
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
