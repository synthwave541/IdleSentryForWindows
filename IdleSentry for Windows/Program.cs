using Velopack;

namespace IdleSentry;

internal static class Program
{
    private static void RemoveOwnedStartupEntry()
    {
        try
        {
            string? root = Velopack.Locators.VelopackLocator.Current.RootAppDir;
            if (root == null) return;
            using var key = Microsoft.Win32.Registry.CurrentUser.OpenSubKey(
                @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", writable: true);
            string? command = key?.GetValue("IdleSentry") as string;
            string owned = "\"" + System.IO.Path.Combine(root, "IdleSentry.exe") + "\"";
            if (command?.StartsWith(owned, StringComparison.OrdinalIgnoreCase) == true)
                key!.DeleteValue("IdleSentry", throwOnMissingValue: false);
        }
        catch (Exception ex) { System.Diagnostics.Debug.WriteLine(ex.Message); }
    }

    [STAThread]
    private static void Main(string[] args)
    {
        // Installer/update hooks must finish before WPF and the single-instance mutex.
        // Downloaded updates stay pending until the user chooses Install & Restart.
        VelopackApp.Build().SetAutoApplyOnStartup(false)
            .OnBeforeUninstallFastCallback(_ => RemoveOwnedStartupEntry()).Run();
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
