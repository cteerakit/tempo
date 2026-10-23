using Velopack;

namespace Tempo.App;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        // Must run first: the installer and updater launch Tempo.exe with hook arguments and expect it to exit quickly.
        VelopackApp.Build()
            .OnBeforeUninstallFastCallback(_ => App.PrepareForUninstall())
            .Run();

        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
