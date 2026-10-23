using System;
using System.Diagnostics;
using System.IO;
using System.IO.Pipes;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Threading;
using H.NotifyIcon;
using H.NotifyIcon.Core;

namespace IdleSentry
{
    public partial class App : Application
    {
        private static Mutex? _mutex;
        private const string MutexName = "IdleSentry_Mutex_Unique_ID_192837";
        private const string PipeName = "IdleSentry_Pipe_Unique_ID_192837";
        private CancellationTokenSource? _pipeCts;
        private WindowsAccentWatcher? _accentWatcher;
        private DispatcherTimer? _trayRetryTimer;
        public AppState AppState { get; private set; } = null!;
        public TaskbarIcon? TrayIcon { get; private set; }
        public bool IsShuttingDown { get; private set; }

        protected override void OnStartup(StartupEventArgs e)
        {
            // 1. Single Instance Check via Mutex
            //    Request initial ownership. If another instance owns it, signal it to come forward.
            _mutex = new Mutex(initiallyOwned: true, MutexName, out bool isNewInstance);

            if (!isNewInstance)
            {
                // Another instance is running. Signal it to show its window, then exit silently.
                SignalExistingInstance();
                Shutdown();
                return;
            }

            base.OnStartup(e);
            ShutdownMode = ShutdownMode.OnExplicitShutdown;
            _accentWatcher = new WindowsAccentWatcher(Dispatcher);

            // 2. Initialize State
            AppState = new AppState();
            AppState.RequestNotification += OnNotificationRequested;

            // 3. Setup System Tray Icon
            SetupTrayIcon();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged += OnDisplaySettingsChanged;
#if !STORE
            UpdateService.Shared.UpdateFound += version => OnNotificationRequested("IdleSentry update available",
                $"Version {version} is ready to download. Open About to update when you’re ready.");
            _ = UpdateService.Shared.StartAsync();
            // Refresh any 1.0.14 startup path after the first launch of the new installer.
            if (AppState.LaunchAtStartup) AppState.RefreshStartupPath();
#endif
            _ = AppState.RefreshStartupStateAsync();

            // 4. Start listening for subsequent launches (so they can wake this instance)
            StartPipeServer();

            // 5. Handle Window Startup (Hide if launched with --background)
            bool startInBackground = e.Args.Contains("--background");
#if STORE
            startInBackground |= StoreIntegration.IsStartupActivation;
#endif
            if (!startInBackground)
            {
                ShowMainWindow();
            }
        }

        /// <summary>
        /// Attempts to notify an already-running instance to bring its window to the foreground.
        /// </summary>
        private static void SignalExistingInstance()
        {
            try
            {
                using var client = new NamedPipeClientStream(".", PipeName, PipeDirection.Out);
                client.Connect(2000); // wait up to 2s for the server to respond
                using var writer = new StreamWriter(client);
                writer.WriteLine("SHOW");
                writer.Flush();
            }
            catch
            {
                // If we can't reach the existing instance, fall back to a message box.
                MessageBox.Show("IdleSentry is already running in the system tray. Double-click its tray icon to open it.",
                    "IdleSentry", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        /// <summary>
        /// Runs a background named-pipe server so subsequent launches can ask this instance to surface.
        /// </summary>
        private void StartPipeServer()
        {
            _pipeCts = new CancellationTokenSource();
            var token = _pipeCts.Token;

            var thread = new Thread(() =>
            {
                while (!token.IsCancellationRequested)
                {
                    NamedPipeServerStream? server = null;
                    try
                    {
                        server = new NamedPipeServerStream(PipeName, PipeDirection.In, 1,
                            PipeTransmissionMode.Byte, PipeOptions.CurrentUserOnly);
                        server.WaitForConnection();

                        using var reader = new StreamReader(server);
                        var message = reader.ReadLine();
                        server.Dispose();
                        server = null;

                        if (string.Equals(message, "SHOW", StringComparison.Ordinal))
                        {
                            // Marshal back to the UI thread.
                            Dispatcher.Invoke(() => ShowMainWindow());
                        }
                    }
                    catch (Exception ex) when (ex is OperationCanceledException || token.IsCancellationRequested)
                    {
                        server?.Dispose();
                        break;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Pipe server error: {ex.Message}");
                        server?.Dispose();
                        // Brief pause to avoid a tight loop if the pipe keeps failing.
                        Thread.Sleep(200);
                    }
                }
            })
            { IsBackground = true };
            thread.Start();
        }

        private void SetupTrayIcon()
        {
            TrayIcon = new TaskbarIcon
            {
                ToolTipText = "IdleSentry",
                MenuActivation = PopupActivationMode.RightClick,
                Visibility = Visibility.Visible,
            };

            // Set dynamic icon based on state
            UpdateTrayIcon(AppState.IsMonitoring);

            // Dynamically listen to monitoring state to update tray icon
            AppState.PropertyChanged += (s, e) =>
            {
                if (e.PropertyName == nameof(AppState.IsMonitoring))
                {
                    UpdateTrayIcon(AppState.IsMonitoring);
                }
            };

            // Double click tray icon opens settings window
            TrayIcon.TrayMouseDoubleClick += (s, e) => ShowMainWindow();

            // Create context menu
            var menu = new ContextMenu { Style = (Style)FindResource("TrayContextMenuStyle") };
            System.Windows.Media.TextOptions.SetTextFormattingMode(menu, System.Windows.Media.TextFormattingMode.Display);
            menu.Opened += ContextMenu_Opened;
            TrayIcon.ContextMenu = menu;

            // A programmatically created icon never receives FrameworkElement.Loaded.
            // Register it explicitly, including when no main window is ever opened.
            EnsureTrayIconCreated();
            _trayRetryTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            _trayRetryTimer.Tick += (_, _) => EnsureTrayIconCreated();
            _trayRetryTimer.Start();
        }

        internal bool EnsureTrayIconCreated()
        {
            if (IsShuttingDown || TrayIcon == null) return false;
            try
            {
                TrayIcon.Visibility = Visibility.Visible;
                if (!TrayIcon.IsCreated) TrayIcon.ForceCreate(enablesEfficiencyMode: false);
                return TrayIcon.IsCreated;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Tray registration failed; will retry: {ex.Message}");
                return false;
            }
        }

        private void UpdateTrayIcon(bool isMonitoring)
        {
            if (TrayIcon == null) return;
            try
            {
                var oldIcon = TrayIcon.Icon;
                using (var icon = TrayArtwork.Create(isMonitoring))
                {
                    TrayIcon.Icon = (System.Drawing.Icon)icon.Clone();
                }
                oldIcon?.Dispose();
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update tray icon: {ex.Message}");
            }
        }

        private void OnDisplaySettingsChanged(object? sender, EventArgs e)
        {
            if (IsShuttingDown) return;
            Dispatcher.BeginInvoke(new Action(() =>
            {
                if (!IsShuttingDown) UpdateTrayIcon(AppState.IsMonitoring);
            }));
        }

        private void ContextMenu_Opened(object sender, RoutedEventArgs e)
        {
            if (sender is not ContextMenu menu) return;
            menu.Items.Clear();

            // Title Header
            var titleItem = new MenuItem
            {
                Header = $"IdleSentry: {(AppState.IsMonitoring ? "Monitoring" : "Paused")}",
                IsEnabled = false
            };
            menu.Items.Add(titleItem);
            menu.Items.Add(new Separator());

            // Pause / Resume Toggle
            var toggleItem = new MenuItem
            {
                Header = AppState.IsMonitoring ? "Pause Monitoring" : "Resume Monitoring"
            };
            toggleItem.Click += (s, ev) => AppState.IsMonitoring = !AppState.IsMonitoring;
            menu.Items.Add(toggleItem);
            menu.Items.Add(new Separator());

            // Monitored Apps and Statuses
            if (AppState.RunningStates.Any())
            {
                if (AppState.HasAppsPendingClose)
                {
                    menu.Items.Add(new MenuItem
                    {
                        Header = $"{AppState.ActivityStatusText}  {SecondsToTimerConverter.Format(AppState.CountdownValue)}",
                        IsEnabled = false
                    });
                }
                var appsHeader = new MenuItem
                {
                    Header = "Monitored Apps:",
                    IsEnabled = false
                };
                menu.Items.Add(appsHeader);

                foreach (var state in AppState.RunningStates)
                {
                    string statusText = state.IsRunning ? "Running" : "Not running";

                    var appItem = new MenuItem
                    {
                        Header = $"  {state.Name} ({statusText})",
                        IsEnabled = false
                    };
                    menu.Items.Add(appItem);
                }
                menu.Items.Add(new Separator());
            }

            // Preferences Window
            var prefItem = new MenuItem { Header = "Preferences..." };
            prefItem.Click += (s, ev) => ShowMainWindow();
            menu.Items.Add(prefItem);
            menu.Items.Add(new Separator());

#if !STORE
            var updateItem = new MenuItem { Header = UpdateService.Shared.CanInstall || UpdateService.Shared.CanDownload
                ? "Update available…" : "Check for Updates…", IsEnabled = !UpdateService.Shared.IsBusy };
            updateItem.Click += async (_, _) =>
            {
                ShowMainWindow();
                if (MainWindow is MainWindow window) window.ShowUpdates();
                await UpdateService.Shared.CheckAsync();
            };
            menu.Items.Add(updateItem);
            menu.Items.Add(new Separator());
#endif

            // Quit Application
            var quitItem = new MenuItem { Header = "Quit IdleSentry" };
            quitItem.Click += (s, ev) => SafeShutdown();
            menu.Items.Add(quitItem);
        }

        public void ShowMainWindow()
        {
            if (MainWindow == null)
            {
                MainWindow = new MainWindow(AppState);
            }

            if (MainWindow.WindowState == WindowState.Minimized)
            {
                MainWindow.WindowState = WindowState.Normal;
            }

            MainWindow.Show();
            MainWindow.Activate();
            MainWindow.Topmost = true;
            MainWindow.Topmost = false;
            MainWindow.Focus();
        }

        private void OnNotificationRequested(string title, string body)
        {
            if (IsShuttingDown || Dispatcher.HasShutdownStarted) return;
            if (!Dispatcher.CheckAccess())
            {
                Dispatcher.BeginInvoke((Action)(() => OnNotificationRequested(title, body)));
                return;
            }
            try
            {
                if (EnsureTrayIconCreated()) TrayIcon!.ShowNotification(title, body, NotificationIcon.Info);
            }
            catch (Exception ex)
            {
                // Explorer or notifications may be unavailable. Closing an app must
                // still finish and monitoring must continue without a notification.
                Debug.WriteLine($"Tray notification unavailable: {ex.Message}");
            }
        }

        public void SafeShutdown()
        {
            IsShuttingDown = true;
            AppState?.Dispose();
            _trayRetryTimer?.Stop();
            _pipeCts?.Cancel();
            TrayIcon?.Dispose();
            MainWindow?.Close();
            Shutdown();
        }

        protected override void OnExit(ExitEventArgs e)
        {
#if !STORE
            if (AppState != null) UpdateService.Shared.Dispose();
#endif
            IsShuttingDown = true;
            AppState?.Dispose();
            _trayRetryTimer?.Stop();
            TrayIcon?.Dispose();
            Microsoft.Win32.SystemEvents.DisplaySettingsChanged -= OnDisplaySettingsChanged;
            _accentWatcher?.Dispose();
            _pipeCts?.Cancel();
            _pipeCts?.Dispose();
            try
            {
                // Release the mutex so a future launch can acquire it cleanly.
                if (_mutex != null)
                {
                    _mutex.ReleaseMutex();
                    _mutex.Dispose();
                    _mutex = null;
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Mutex release error: {ex.Message}");
            }
            base.OnExit(e);
        }

    }
}
