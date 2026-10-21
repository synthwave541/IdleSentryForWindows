#if STORE
using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using IdleSentry;
using Windows.ApplicationModel;

internal static class StoreChecks
{
    internal static void Run(App app, Action<bool, string> check, string? previewDirectory)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(app.Dispatcher));
        try
        {
            Task task = RunAsync(app, check);
            var timeout = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                if (timeout.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Store checks timed out.");
                var frame = new DispatcherFrame();
                app.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(10);
            }
            task.GetAwaiter().GetResult();
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static async Task RunAsync(App application, Action<bool, string> check)
    {
        var identity = Package.Current.Id;
        check(identity.Name == "Zennoris.IdleSentry" && identity.Publisher == "CN=DE14F2F2-0FBD-412F-9C6A-ABD8C19C96E4",
            "installed package matches the reserved Partner Center identity");
        check(identity.FamilyName == "Zennoris.IdleSentry_xbrdk7na38hv0", "publisher hash matches the Partner Center package family");
        check(!StoreIntegration.IsStartupActivation, "ordinary packaged launches safely handle absent startup activation arguments");
        check(AppDistribution.IsStore && AppDistribution.SettingsDirectory == Windows.Storage.ApplicationData.Current.LocalFolder.Path,
            "Store preferences use package-owned LocalState");
        check(!typeof(App).Assembly.GetReferencedAssemblies().Any(a => a.Name!.StartsWith("Velopack")),
            "Store executable has no Velopack assembly dependency");
        check(AppDistribution.StandaloneUpdatesVisibility == Visibility.Collapsed && AppDistribution.StoreUpdatesVisibility == Visibility.Visible,
            "Store About shows Microsoft Store updates instead of standalone controls");
        var service = UpdateService.Shared;
        service.AutomaticChecks = true;
        await service.StartAsync();
        await service.CheckAsync();
        await service.DownloadAsync();
        bool restarted = false;
        service.InstallAndRestart(() => restarted = true);
        check(!service.AutomaticChecks && !service.CanCheck && !service.CanDownload && !service.CanInstall && !restarted,
            "Store channel cannot check, download, auto-update or restart through the standalone updater");
        check(service.Status.Contains("Microsoft Store") && !File.Exists(Path.Combine(AppDistribution.SettingsDirectory, "updates.json")),
            "Store update service does not persist standalone update preferences");

        var startup = await StartupTask.GetAsync(StoreIntegration.StartupTaskId);
        check(startup.State == StartupTaskState.Disabled, "fresh Store installation does not enable startup automatically");
        try
        {
            var enabled = await StoreIntegration.SetStartupAsync(true);
            check(enabled.Enabled && await StoreIntegration.IsStartupEnabledAsync(), "packaged startup can be explicitly enabled");
            using var observed = new AppState(initialize: false);
            await observed.RefreshStartupStateAsync();
            check(observed.LaunchAtStartup, "startup toggle reflects Windows' actual enabled state");
            var disabled = await StoreIntegration.SetStartupAsync(false);
            await observed.RefreshStartupStateAsync();
            check(!disabled.Enabled && !observed.LaunchAtStartup, "packaged startup can be disabled and refreshed");
        }
        finally { await StoreIntegration.SetStartupAsync(false); }

        string file = Path.Combine(AppDistribution.SettingsDirectory, "settings.json");
        byte[]? backup = File.Exists(file) ? File.ReadAllBytes(file) : null;
        string directFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IdleSentry", "settings.json");
        byte[]? directBefore = File.Exists(directFile) ? File.ReadAllBytes(directFile) : null;
        try
        {
            using (var settings = new AppState(initialize: false))
            {
                settings.IsMonitoring = false;
                settings.GlobalTimeout = 420;
            }
            using var restored = new AppState();
            check(restored.GlobalTimeout == 420 && !restored.IsMonitoring, "Store preferences persist across state reconstruction");
            check(File.Exists(file), "settings are saved under package-owned LocalState");
            byte[]? directAfter = File.Exists(directFile) ? File.ReadAllBytes(directFile) : null;
            check(directBefore == null ? directAfter == null : directAfter != null && directBefore.SequenceEqual(directAfter),
                "Store preference writes leave standalone settings unchanged");
        }
        finally
        {
            if (backup == null) File.Delete(file);
            else File.WriteAllBytes(file, backup);
        }

        var menu = new ContextMenu();
        typeof(App).GetMethod("ContextMenu_Opened", BindingFlags.Instance | BindingFlags.NonPublic)!
            .Invoke(application, new object[] { menu, new RoutedEventArgs() });
        check(!menu.Items.OfType<MenuItem>().Any(item => item.Header?.ToString()?.Contains("Update", StringComparison.OrdinalIgnoreCase) == true),
            "Store tray menu has no standalone update action");
        var quit = menu.Items.OfType<MenuItem>().Single(item => item.Header?.ToString() == "Quit IdleSentry");
        quit.RaiseEvent(new RoutedEventArgs(MenuItem.ClickEvent));
        check(application.IsShuttingDown && application.TrayIcon?.IsCreated == false,
            "Store tray Quit follows graceful shutdown and removes its tray icon");
    }
}
#endif
