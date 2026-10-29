using System.Windows;
using System.Windows.Threading;

namespace AztroMidi;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        DispatcherUnhandledException += OnUnhandled;
        base.OnStartup(e);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(e.Exception.Message, "AztroMidi", MessageBoxButton.OK, MessageBoxImage.Warning);
        e.Handled = true;
    }
}
