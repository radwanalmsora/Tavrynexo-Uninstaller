using System.Threading;
using System.Windows;
using Tavrynexo.Core;
using Tavrynexo.Views;

namespace Tavrynexo;

public partial class App : Application
{
    private static Mutex? _singleInstance;

    protected override void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);

        _singleInstance = new Mutex(true, "Tavrynexo.Uninstaller.SingleInstance", out var created);
        if (!created)
        {
            MessageBox.Show("Tavrynexo Uninstaller is already running.", "Tavrynexo Uninstaller",
                MessageBoxButton.OK, MessageBoxImage.Information);
            Shutdown();
            return;
        }

        AppSettings.Load();
        L.Init(AppSettings.Current.Language);

        DispatcherUnhandledException += (_, ex) =>
        {
            Logger.Error("Unhandled UI exception", ex.Exception);
            MessageBox.Show(ex.Exception.Message, "Tavrynexo Uninstaller", MessageBoxButton.OK, MessageBoxImage.Error);
            ex.Handled = true;
        };

        new MainWindow().Show();
    }
}
