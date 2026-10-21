using Velopack;

namespace IdleSentry;

internal static class Program
{
    [STAThread]
    private static void Main(string[] args)
    {
        // Installer/update hooks must finish before WPF and the single-instance mutex.
        // Downloaded updates stay pending until the user chooses Install & Restart.
        VelopackApp.Build().SetAutoApplyOnStartup(false).Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
