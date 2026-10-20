using System.Diagnostics;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Threading;
using IdleSentry;

internal static class MonitoringLifecycleChecks
{
    [DllImport("user32.dll")]
    private static extern IntPtr GetShellWindow();

    public static void Run(App application, Action<bool, string> check)
    {
        var previous = SynchronizationContext.Current;
        SynchronizationContext.SetSynchronizationContext(new DispatcherSynchronizationContext(application.Dispatcher));
        try
        {
            Task task = RunAsync(check);
            var deadline = Stopwatch.StartNew();
            while (!task.IsCompleted)
            {
                if (deadline.Elapsed > TimeSpan.FromSeconds(30)) throw new TimeoutException("Monitoring regression checks timed out.");
                var frame = new DispatcherFrame();
                application.Dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() => frame.Continue = false));
                Dispatcher.PushFrame(frame);
                Thread.Sleep(10);
            }
            task.GetAwaiter().GetResult();

            check(application.ShutdownMode == ShutdownMode.OnExplicitShutdown, "background lifetime uses explicit shutdown");
            // Hosted CI sessions may not run Explorer. Process/dispatcher checks
            // still run there; actual Shell registration needs a desktop session.
            if (GetShellWindow() == IntPtr.Zero)
            {
                Console.WriteLine("SKIP: live tray registration checks require Windows Explorer.");
                return;
            }
            typeof(App).GetMethod("SetupTrayIcon", BindingFlags.Instance | BindingFlags.NonPublic)!.Invoke(application, null);
            check(application.TrayIcon?.IsCreated == true, "tray icon is registered without a Loaded event or visible window");
            check(application.EnsureTrayIconCreated() && application.TrayIcon!.Visibility == Visibility.Visible,
                "rechecking registration keeps the same tray icon visible");
            application.TrayIcon!.TrayIcon.TryRemove();
            check(!application.TrayIcon.IsCreated && application.EnsureTrayIconCreated(), "lost tray registration can be recreated");
            typeof(App).GetMethod("OnNotificationRequested", BindingFlags.Instance | BindingFlags.NonPublic)!
                .Invoke(application, new object[] { "IdleSentry regression check", "Test notification after tray recovery." });
            check(application.TrayIcon.IsCreated, "close notification succeeds after tray recreation");
        }
        finally { SynchronizationContext.SetSynchronizationContext(previous); }
    }

    private static async Task RunAsync(Action<bool, string> check)
    {
        // Unique copies of a harmless Windows utility isolate process matching from
        // the user's apps. Never monitor or close the user's real browser in tests.
        string fixtures = Path.Combine(Path.GetTempPath(), "IdleSentry-monitor-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtures);
        var processes = new List<Process>();
        try
        {
            string target = Path.Combine(fixtures, "IdleSentryTestTarget.exe");
            string next = Path.Combine(fixtures, "IdleSentryTestNext.exe");
            File.Copy(Path.Combine(Environment.SystemDirectory, "ping.exe"), target);
            File.Copy(target, next);
            Process Start(string executable)
            {
                var process = Process.Start(new ProcessStartInfo(executable, "-t 127.0.0.1")
                {
                    UseShellExecute = false, CreateNoWindow = true, RedirectStandardOutput = true, RedirectStandardError = true
                })!;
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();
                processes.Add(process);
                return process;
            }
            var first = Start(target);
            var helper = Start(target);
            var later = Start(next);
            await Task.Delay(150);
            check(!first.HasExited && !helper.HasExited && !later.HasExited, "isolated multi-process app fixtures are running");

            using var state = new AppState(initialize: false);
            state.MonitoredApps.Add(new MonitoredApp { Name = "Test browser", Path = target, CustomTimeout = 10 });
            state.MonitoredApps.Add(new MonitoredApp { Name = "Next app", Path = next, CustomTimeout = 60 });
            int uiThread = Environment.CurrentManagedThreadId;
            bool uiOnly = true;
            int notifications = 0;
            state.PropertyChanged += (_, _) => uiOnly &= Environment.CurrentManagedThreadId == uiThread;
            state.RequestNotification += (_, _) =>
            {
                notifications++;
                check(!state.RunningStates.Single(app => app.Path == target).IsRunning, "completed state is published before notifications");
                throw new InvalidOperationException("Simulate the original unavailable-tray notification failure.");
            };
            await state.RefreshStatesAsync(AppState.CountdownDelay + 9.5);
            check(state.HasAppsPendingClose && state.CountdownValue == 0.5, "regression starts in the final countdown second");
            var closing = state.RefreshStatesAsync(AppState.CountdownDelay + 11);
            // Simulate a timer firing again while multiple processes are closing.
            await state.RefreshStatesAsync(AppState.CountdownDelay + 11);
            await closing;
            check(first.HasExited && helper.HasExited, "standard quit closes every fixture process");
            check(!later.HasExited && state.RunningStates.Single(app => app.Path == next).IsRunning, "later app remains running");
            check(state.ActivityStatusText == "Next to close · Next app" && state.CountdownValue == 49,
                "countdown advances to the next app after close, despite a notification exception");
            check(notifications == 1 && uiOnly, "overlapping refreshes produce one notification and publish on the UI thread");
            await state.RefreshStatesAsync(AppState.CountdownDelay + 12);
            check(state.CountdownValue == 48, "monitoring continues on the next refresh after notification failure");

            // Set the private field to avoid touching the user's persisted settings.
            typeof(AppState).GetField("_quitMethod", BindingFlags.Instance | BindingFlags.NonPublic)!.SetValue(state, 1);
            await state.RefreshStatesAsync(AppState.CountdownDelay + 61);
            check(later.HasExited && !state.HasAppsPendingClose && state.CountdownValue == 0,
                "force quit clears the last running app and countdown");
            check(state.ActivityStatusText == "No monitored apps running", "empty running state replaces the countdown");
            state.Dispose();
            await state.RefreshStatesAsync(100);
            check(state.ActivityStatusText == "No monitored apps running", "disposed monitoring ignores later callbacks");
        }
        finally
        {
            foreach (var process in processes)
            {
                try { if (!process.HasExited) { process.Kill(entireProcessTree: true); process.WaitForExit(3000); } }
                finally { process.Dispose(); }
            }
            // Exactly the fresh, GUID-named directory created for these fixtures.
            Directory.Delete(fixtures, recursive: true);
        }
    }
}
