using System.Windows;
using System.Windows.Threading;
using Recall.Theming;

namespace Recall;

public partial class App : Application
{
    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        DispatcherUnhandledException += OnUnhandled;

        // Palette before the first window is built, so nothing flashes the default.
        ThemeManager.Apply(Services.Store.Instance.Data.Theme, persist: false);
    }

    private void OnUnhandled(object sender, DispatcherUnhandledExceptionEventArgs e)
    {
        MessageBox.Show(
            e.Exception.Message,
            "Recall hit an error",
            MessageBoxButton.OK,
            MessageBoxImage.Warning);
        e.Handled = true;
    }
}
