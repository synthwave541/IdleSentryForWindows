#if !STORE
using Velopack;
#endif

namespace IdleSentry;

internal static class Program
{
#if !STORE
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

#endif

    [STAThread]
    private static void Main(string[] args)
    {
#if STORE
        // A Store binary must run with package identity, never as a standalone download.
        try { _ = Windows.ApplicationModel.Package.Current.Id.FullName; }
        catch (InvalidOperationException)
        {
            System.Windows.MessageBox.Show("Install IdleSentry from Microsoft Store to run this build.", "IdleSentry");
            return;
        }
#else
        // Installer/update hooks must finish before WPF and the single-instance mutex.
        // Downloaded updates stay pending until the user chooses Install & Restart.
        VelopackApp.Build().SetAutoApplyOnStartup(false)
            .OnBeforeUninstallFastCallback(_ => RemoveOwnedStartupEntry()).Run();
#endif
        var app = new App();
        app.InitializeComponent();
        app.Run();
    }
}
