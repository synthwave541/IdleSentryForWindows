using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Threading;
using System.Windows.Data;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;

namespace IdleSentry
{
    public class MonitoredApp : INotifyPropertyChanged
    {
        private Guid _id = Guid.NewGuid();
        private string _name = string.Empty;
        private string _path = string.Empty;
        private double? _customTimeout;

        public Guid Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Path
        {
            get => _path;
            set { _path = value; OnPropertyChanged(); }
        }

        public double? CustomTimeout
        {
            get => _customTimeout;
            set { _customTimeout = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class AppRunningState : INotifyPropertyChanged
    {
        private string _id = string.Empty;
        private string _name = string.Empty;
        private string _path = string.Empty;
        private bool _isRunning;
        private bool _isFrontmost;
        private bool _isCountingDown;
        private double _timeRemaining;
        private ImageSource? _icon;

        public string Id
        {
            get => _id;
            set { _id = value; OnPropertyChanged(); }
        }

        public string Name
        {
            get => _name;
            set { _name = value; OnPropertyChanged(); }
        }

        public string Path
        {
            get => _path;
            set { _path = value; OnPropertyChanged(); }
        }

        public bool IsRunning
        {
            get => _isRunning;
            set { _isRunning = value; OnPropertyChanged(); }
        }

        public bool IsFrontmost
        {
            get => _isFrontmost;
            set { _isFrontmost = value; OnPropertyChanged(); }
        }

        public bool IsCountingDown
        {
            get => _isCountingDown;
            set { _isCountingDown = value; OnPropertyChanged(); }
        }

        public double TimeRemaining
        {
            get => _timeRemaining;
            set { _timeRemaining = value; OnPropertyChanged(); }
        }

        public ImageSource? Icon
        {
            get => _icon;
            set { _icon = value; OnPropertyChanged(); }
        }

        public event PropertyChangedEventHandler? PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string? name = null) =>
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
    }

    public class AppState : INotifyPropertyChanged, IDisposable
    {
        private static readonly string SettingsDir = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IdleSentry");
        private static readonly string SettingsFile = Path.Combine(SettingsDir, "settings.json");

        // Win32 API structures and methods for User Inactivity (Idle Time)
        [StructLayout(LayoutKind.Sequential)]
        private struct LASTINPUTINFO
        {
            public uint cbSize;
            public uint dwTime;
        }

        [DllImport("user32.dll")]
        private static extern bool GetLastInputInfo(ref LASTINPUTINFO plii);

        [DllImport("user32.dll")]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [DllImport("user32.dll")]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool IsWindowVisible(IntPtr hWnd);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr OpenProcess(uint processAccess, bool bInheritHandle, int processId);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
        private static extern bool QueryFullProcessImageName(IntPtr hProcess, int dwFlags, System.Text.StringBuilder lpExeName, ref int lpdwSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool CloseHandle(IntPtr hObject);

        [DllImport("gdi32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool DeleteObject(IntPtr hObject);

        private const uint PROCESS_QUERY_LIMITED_INFORMATION = 0x1000;

        // Icon Cache
        private static readonly Dictionary<string, ImageSource?> _iconCache = new(StringComparer.OrdinalIgnoreCase);

        // Properties
        private ObservableCollection<MonitoredApp> _monitoredApps = new();
        private double _globalTimeout = 300;
        private int _quitMethod = 0; // 0 = Standard, 1 = Force Quit
        private bool _isMonitoring = true;
        private double _systemIdleTime;
        private bool _isCountdownActive;
        private bool _launchAtStartup;
        private ObservableCollection<AppRunningState> _runningStates = new();
        private ListCollectionView? _closingQueueView;
        private ObservableCollection<AppRunningState>? _closingQueueSource;

        public ObservableCollection<MonitoredApp> MonitoredApps
        {
            get => _monitoredApps;
            set
            {
                _monitoredApps = value;
                OnPropertyChanged();
                SaveSettings();
                ReEvaluateTimerState();
            }
        }

        public double GlobalTimeout
        {
            get => _globalTimeout;
            set
            {
                // Round to nearest 10 seconds
                double rounded = Math.Round(value / 10.0) * 10.0;
                if (rounded < 10) rounded = 10;
                if (rounded != _globalTimeout)
                {
                    _globalTimeout = rounded;
                    OnPropertyChanged();
                    SaveSettings();
                }
            }
        }

        public int QuitMethod
        {
            get => _quitMethod;
            set
            {
                _quitMethod = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(QuitMethodName));
                OnPropertyChanged(nameof(QuitMethodDescription));
                SaveSettings();
            }
        }

        public string QuitMethodName => QuitMethod switch
        {
            1 => "Force Quit",
            _ => "Standard Quit"
        };

        public string QuitMethodDescription => QuitMethod == 1
            ? "Closes immediately"
            : "Tries closing, then forces exit";

        public string MonitoringTitle => !IsMonitoring
            ? "Monitoring paused"
            : MonitoredApps.Count == 0 ? "Choose your first app" : "Monitoring enabled";

        public string MonitoringDescription => !IsMonitoring
            ? "Automatic closing is paused. Resume whenever you're ready."
            : MonitoredApps.Count == 0
                ? "Add the apps you want IdleSentry to close when you step away."
                : "Selected apps close after keyboard and mouse inactivity reaches their timeout.";

        public bool IsMonitoring
        {
            get => _isMonitoring;
            set
            {
                _isMonitoring = value;
                OnPropertyChanged();
                SaveSettings();
                ReEvaluateTimerState();
            }
        }

        public double SystemIdleTime
        {
            get => _systemIdleTime;
            private set { _systemIdleTime = value; OnPropertyChanged(); }
        }

        public bool IsCountdownActive
        {
            get => _isCountdownActive;
            private set { _isCountdownActive = value; OnPropertyChanged(); }
        }

        public bool LaunchAtStartup
        {
            get => _launchAtStartup;
            set
            {
                if (_launchAtStartup != value)
                {
                    _launchAtStartup = value;
                    OnPropertyChanged();
                    SaveSettings();
                    ToggleLaunchAtStartup(value);
                }
            }
        }

        public ObservableCollection<AppRunningState> RunningStates
        {
            get => _runningStates;
            set { _runningStates = value; OnPropertyChanged(); OnPropertyChanged(nameof(ClosingQueue)); }
        }

        // Computed properties
        public IEnumerable<AppRunningState> AppsPendingClose
        {
            get
            {
                if (!IsMonitoring || !IsCountdownActive) return Enumerable.Empty<AppRunningState>();
                return RunningStates
                    .Where(s => s.IsRunning && s.TimeRemaining < GetAppTimeout(s.Path))
                    .OrderBy(s => s.TimeRemaining);
            }
        }

        // An independent live view keeps stopped apps in the main list and preserves
        // the popup's scroll position when input resets or a timer ticks.
        public ICollectionView ClosingQueue
        {
            get
            {
                if (_closingQueueView == null || !ReferenceEquals(_closingQueueSource, RunningStates))
                {
                    _closingQueueSource = RunningStates;
                    _closingQueueView = new ListCollectionView(RunningStates)
                    {
                        Filter = item => item is AppRunningState { IsRunning: true }
                    };
                    _closingQueueView.SortDescriptions.Add(new SortDescription(nameof(AppRunningState.TimeRemaining), ListSortDirection.Ascending));
                    _closingQueueView.LiveSortingProperties.Add(nameof(AppRunningState.TimeRemaining));
                    _closingQueueView.IsLiveSorting = true;
                    _closingQueueView.LiveFilteringProperties.Add(nameof(AppRunningState.IsRunning));
                    _closingQueueView.IsLiveFiltering = true;
                }
                return _closingQueueView;
            }
        }

        public bool HasClosingQueue => IsMonitoring && RunningAppsCount > 0;
        public int ClosingQueueCount => IsMonitoring ? RunningAppsCount : 0;
        public int ClosingQueueAdditionalCount => Math.Max(0, ClosingQueueCount - 1);
        public string ClosingQueueAdditionalText => $"+{ClosingQueueAdditionalCount} more";
        public string ClosingQueueDescription => $"{ClosingQueueCount} monitored {(ClosingQueueCount == 1 ? "app" : "apps")} · soonest first";
        public string NextClosingAppName => AppsPendingClose.FirstOrDefault()?.Name ?? string.Empty;
        public string ClosingQueueHint => $"View closing queue · {ClosingQueueCount} monitored {(ClosingQueueCount == 1 ? "app" : "apps")}";

        private void NotifyClosingQueueProperties()
        {
            OnPropertyChanged(nameof(HasClosingQueue));
            OnPropertyChanged(nameof(ClosingQueueCount));
            OnPropertyChanged(nameof(ClosingQueueAdditionalCount));
            OnPropertyChanged(nameof(ClosingQueueAdditionalText));
            OnPropertyChanged(nameof(ClosingQueueDescription));
            OnPropertyChanged(nameof(NextClosingAppName));
            OnPropertyChanged(nameof(ClosingQueueHint));
        }

        public bool HasAppsPendingClose => AppsPendingClose.Any();

        public int RunningAppsCount => RunningStates.Count(s => s.IsRunning);

        public string MonitoredAppsSummary => $"{MonitoredApps.Count} {(MonitoredApps.Count == 1 ? "app" : "apps")}";

        public string ActivityStatusText
        {
            get
            {
                if (!IsMonitoring) return "Monitoring paused";
                if (MonitoredApps.Count == 0) return "Add an app to get started";
                var pending = AppsPendingClose.ToList();
                if (pending.Count > 0)
                {
                    string others = pending.Count > 1 ? $" (+{pending.Count - 1} more)" : string.Empty;
                    return $"Next to close · {pending[0].Name}{others}";
                }
                return RunningAppsCount == 0 ? "No monitored apps running"
                    : $"Monitoring · {RunningAppsCount} {(RunningAppsCount == 1 ? "app" : "apps")} running";
            }
        }

        public bool IsCloseImminent => HasAppsPendingClose && CountdownValue <= 10;

        public double CountdownValue
        {
            get
            {
                var pending = AppsPendingClose.ToList();
                if (!pending.Any()) return 0;
                return pending.Min(s => s.TimeRemaining);
            }
        }

        public const double CountdownDelay = 1.25;
        private readonly Dictionary<string, DateTime> _recentlyQuitApps = new();
        private DispatcherTimer? _timer;
        private int _currentInterval = 1000;
        private bool _refreshInProgress;
        private bool _refreshRequested;
        private bool _disposed;
        private int _stateVersion;

        public event Action<string, string>? RequestNotification;
        public event PropertyChangedEventHandler? PropertyChanged;

        public AppState() : this(initialize: true) { }

        internal AppState(bool initialize)
        {
            if (initialize)
            {
                LoadSettings();
                ReEvaluateTimerState();
            }
        }

        public void ReEvaluateTimerState()
        {
            if (_disposed) return;
            _stateVersion++;
            NotifyMonitoringStatus();
            bool anyMonitored = MonitoredApps.Any();
            if (IsMonitoring && anyMonitored)
            {
                if (_timer == null)
                {
                    Debug.WriteLine("IdleSentry: Monitored app list is active. Starting timer.");
                    _currentInterval = 1000;
                    _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
                    _timer.Tick += Tick;
                    _timer.Start();
                }
            }
            else
            {
                if (_timer != null)
                {
                    Debug.WriteLine("IdleSentry: Stopping timer to conserve CPU.");
                    _timer.Stop();
                    _timer.Tick -= Tick;
                    _timer = null;
                    SystemIdleTime = 0;
                    IsCountdownActive = false;
                }
            }
            _ = RefreshStatesAsync();
        }

        private void NotifyMonitoringStatus()
        {
            OnPropertyChanged(nameof(MonitoringTitle));
            OnPropertyChanged(nameof(MonitoringDescription));
            OnPropertyChanged(nameof(MonitoredAppsSummary));
            OnPropertyChanged(nameof(ActivityStatusText));
            NotifyClosingQueueProperties();
            OnPropertyChanged(nameof(IsCloseImminent));
        }

        private async void Tick(object? sender, EventArgs e) => await RefreshStatesAsync();

        // Capture settings and publish results on the dispatcher. Only one worker may
        // scan/close processes at a time, even when closing takes longer than a tick.
        internal async Task RefreshStatesAsync(double? idleSeconds = null)
        {
            if (_disposed) return;
            if (_refreshInProgress)
            {
                _refreshRequested = true;
                return;
            }
            _refreshInProgress = true;
            try
            {
                do
                {
                    _refreshRequested = false;
                    int version = _stateVersion;
                    double idle = IsMonitoring ? idleSeconds ?? GetIdleTime() : 0;
                    double effectiveIdle = Math.Max(0, idle - CountdownDelay);
                    bool monitoring = IsMonitoring;
                    int quitMethod = QuitMethod;
                    var apps = MonitoredApps.Select(app => new MonitoredApp
                    {
                        Name = app.Name, Path = app.Path, CustomTimeout = app.CustomTimeout ?? GlobalTimeout
                    }).ToList();
                    var result = await Task.Run(() => UpdateStatesAsync(apps, monitoring, quitMethod, effectiveIdle));
                    if (_disposed) return;
                    if (version != _stateVersion) continue;

                    SystemIdleTime = idle;
                    IsCountdownActive = monitoring && effectiveIdle > 0;
                    ApplyStates(result.States);
                    if (_timer != null)
                    {
                        _currentInterval = result.States.Any(app => app.IsRunning) ? 1000 : 3000;
                        _timer.Interval = TimeSpan.FromMilliseconds(_currentInterval);
                    }
                    // Notifications cannot prevent the completed state from reaching the UI.
                    foreach (string name in result.ClosedApps)
                        SendNotification("App Closed Automatically", $"{name} was quit because it exceeded the idle timeout.");
                } while (_refreshRequested && !_disposed);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"IdleSentry monitoring refresh failed: {ex}");
            }
            finally { _refreshInProgress = false; }
        }

        public void Dispose()
        {
            _disposed = true;
            _timer?.Stop();
            if (_timer != null) _timer.Tick -= Tick;
            _timer = null;
        }

        private static double GetIdleTime()
        {
            var lii = new LASTINPUTINFO();
            lii.cbSize = (uint)Marshal.SizeOf(lii);
            if (GetLastInputInfo(ref lii))
            {
                uint currentTick = (uint)Environment.TickCount64;
                uint elapsedTicks = unchecked(currentTick - lii.dwTime);
                return elapsedTicks / 1000.0;
            }
            return 0;
        }

        private double GetAppTimeout(string path)
        {
            var app = MonitoredApps.FirstOrDefault(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase));
            return app?.CustomTimeout ?? GlobalTimeout;
        }

        internal static string SafeGetProcessPath(Process process)
        {
            try
            {
                return process.MainModule?.FileName ?? string.Empty;
            }
            catch
            {
                try
                {
                    IntPtr hProcess = OpenProcess(PROCESS_QUERY_LIMITED_INFORMATION, false, process.Id);
                    if (hProcess != IntPtr.Zero)
                    {
                        try
                        {
                            var sb = new System.Text.StringBuilder(1024);
                            int size = sb.Capacity;
                            if (QueryFullProcessImageName(hProcess, 0, sb, ref size))
                            {
                                return sb.ToString();
                            }
                        }
                        finally
                        {
                            CloseHandle(hProcess);
                        }
                    }
                }
                catch
                {
                    // Ignore access errors on protected system processes
                }
            }
            return string.Empty;
        }

        public static ImageSource? GetAppIcon(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;

            lock (_iconCache)
            {
                if (_iconCache.TryGetValue(path, out var cached))
                {
                    return cached;
                }

                try
                {
                    using var sysIcon = System.Drawing.Icon.ExtractAssociatedIcon(path);
                    if (sysIcon != null)
                    {
                        using var bitmap = sysIcon.ToBitmap();
                        IntPtr hBitmap = bitmap.GetHbitmap();
                        try
                        {
                            var wpfBitmap = System.Windows.Interop.Imaging.CreateBitmapSourceFromHBitmap(
                                hBitmap,
                                IntPtr.Zero,
                                Int32Rect.Empty,
                                BitmapSizeOptions.FromEmptyOptions());
                            wpfBitmap.Freeze(); // Freeze allows cross-thread WPF access
                            _iconCache[path] = wpfBitmap;
                            return wpfBitmap;
                        }
                        finally
                        {
                            DeleteObject(hBitmap);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"Failed to extract icon for {path}: {ex.Message}");
                }

                _iconCache[path] = null;
                return null;
            }
        }

        private async Task<(List<AppRunningState> States, List<string> ClosedApps)> UpdateStatesAsync(
            List<MonitoredApp> appsList, bool monitoring, int quitMethod, double effectiveIdle)
        {
            // This cache belongs exclusively to the serialized worker.
            var now = DateTime.UtcNow;
            foreach (var key in _recentlyQuitApps.Where(kv => (now - kv.Value).TotalSeconds >= 5).Select(kv => kv.Key).ToList())
                _recentlyQuitApps.Remove(key);
            // Gather foreground process ID
            uint foregroundProcessId = 0;
            IntPtr foregroundWindow = GetForegroundWindow();
            if (foregroundWindow != IntPtr.Zero)
            {
                GetWindowThreadProcessId(foregroundWindow, out foregroundProcessId);
            }

            var updatedStates = new List<AppRunningState>();
            var closedApps = new List<string>();

            foreach (var app in appsList)
            {
                string path = app.Path;
                double limit = app.CustomTimeout!.Value;
                var icon = GetAppIcon(path);

                // Handle recently quit apps to avoid zombie re-detection
                if (_recentlyQuitApps.ContainsKey(path))
                {
                    updatedStates.Add(new AppRunningState
                    {
                        Id = path,
                        Name = app.Name,
                        Path = path,
                        Icon = icon,
                        IsRunning = false,
                        IsFrontmost = false,
                        IsCountingDown = false,
                        TimeRemaining = limit
                    });
                    continue;
                }

                // Check running processes optimized by executable name
                string procName = Path.GetFileNameWithoutExtension(path);
                Process[] processes = Array.Empty<Process>();
                try
                {
                    processes = Process.GetProcessesByName(procName);
                }
                catch
                {
                    // Ignore search errors
                }

                var matchingProcesses = new List<Process>();
                foreach (var p in processes)
                {
                    string processPath = SafeGetProcessPath(p);
                    if (string.Equals(processPath, path, StringComparison.OrdinalIgnoreCase))
                    {
                        matchingProcesses.Add(p);
                    }
                    else
                    {
                        p.Dispose();
                    }
                }

                bool isRunning = matchingProcesses.Count > 0;

                bool isFrontmost = false;
                if (isRunning && foregroundWindow != IntPtr.Zero)
                {
                    if (matchingProcesses.Any(p => p.Id == foregroundProcessId))
                    {
                        isFrontmost = IsWindowVisible(foregroundWindow);
                    }
                }

                double timeRemaining;
                bool isCountingDown = false;

                if (isRunning && monitoring)
                {
                    timeRemaining = Math.Max(0, limit - effectiveIdle);
                    isCountingDown = effectiveIdle > 0 && timeRemaining < limit;
                }
                else
                {
                    timeRemaining = limit;
                }

                try
                {
                    if (isRunning && monitoring && timeRemaining <= 0)
                    {
                        bool closed = await QuitAppAsync(matchingProcesses, app.Name, quitMethod);
                        // Recheck the actual exit state rather than publishing the
                        // pre-close snapshot with a stale 00:00/00:01 countdown.
                        isRunning = matchingProcesses.Any(IsProcessRunning);
                        isFrontmost &= isRunning;
                        isCountingDown = isRunning;
                        if (!isRunning)
                        {
                            _recentlyQuitApps[path] = DateTime.UtcNow;
                            timeRemaining = limit;
                            if (closed) closedApps.Add(app.Name);
                        }
                    }
                }
                finally
                {
                    foreach (var p in matchingProcesses) p.Dispose();
                }

                updatedStates.Add(new AppRunningState
                {
                    Id = path,
                    Name = app.Name,
                    Path = path,
                    Icon = icon,
                    IsRunning = isRunning,
                    IsFrontmost = isFrontmost,
                    IsCountingDown = isCountingDown,
                    TimeRemaining = timeRemaining
                });
            }

            return (updatedStates, closedApps);
        }

        private void ApplyStates(List<AppRunningState> updatedStates)
        {
            for (int i = 0; i < updatedStates.Count; i++)
            {
                var updated = updatedStates[i];
                if (i < RunningStates.Count)
                {
                    var current = RunningStates[i];
                    if (current.Path != updated.Path)
                    {
                        RunningStates[i] = updated;
                    }
                    else
                    {
                        current.Name = updated.Name;
                        current.Icon = updated.Icon;
                        current.IsRunning = updated.IsRunning;
                        current.IsFrontmost = updated.IsFrontmost;
                        current.IsCountingDown = updated.IsCountingDown;
                        current.TimeRemaining = updated.TimeRemaining;
                    }
                }
                else
                {
                    RunningStates.Add(updated);
                }
            }

            while (RunningStates.Count > updatedStates.Count)
            {
                RunningStates.RemoveAt(RunningStates.Count - 1);
            }

            // Notify changes for computed properties
            OnPropertyChanged(nameof(AppsPendingClose));
            OnPropertyChanged(nameof(CountdownValue));
            OnPropertyChanged(nameof(HasAppsPendingClose));
            OnPropertyChanged(nameof(RunningAppsCount));
            OnPropertyChanged(nameof(ActivityStatusText));
            NotifyClosingQueueProperties();
            OnPropertyChanged(nameof(IsCloseImminent));
        }

        private static bool IsProcessRunning(Process process)
        {
            try { return !process.HasExited; }
            catch (InvalidOperationException) { return false; }
        }

        private static async Task<bool> QuitAppAsync(List<Process> processes, string name, int quitMethod)
        {
            Debug.WriteLine($"IdleSentry: Quitting idle app {name} ({processes.Count} instances)");
            bool anyClosed = false;

            if (quitMethod == 1) // Force Quit
            {
                foreach (var p in processes)
                {
                    try
                    {
                        p.Kill(entireProcessTree: true);
                        anyClosed = true;
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"Force quit failed: {ex.Message}");
                    }
                }
            }
            else // Standard Quit with escalation
            {
                // 1. Send WM_CLOSE to all instances with windows
                foreach (var p in processes)
                {
                    try
                    {
                        if (p.MainWindowHandle != IntPtr.Zero)
                        {
                            p.CloseMainWindow();
                            anyClosed = true;
                        }
                    }
                    catch { }
                }

                // Share one grace period across every browser/helper process.
                await WaitForProcessesAsync(processes, TimeSpan.FromMilliseconds(2500));
                bool allExited = !processes.Any(IsProcessRunning);

                // 3. Fallback for tray-minimizing apps (Discord, Telegram, Spotify, etc.)
                // If processes did not exit after grace period, terminate to guarantee privacy
                if (!allExited)
                {
                    foreach (var p in processes)
                    {
                        try
                        {
                            if (!p.HasExited)
                            {
                                p.Kill(entireProcessTree: true);
                                anyClosed = true;
                            }
                        }
                        catch { }
                    }
                }
                else
                {
                    anyClosed = true;
                }
            }

            // Kill requests are asynchronous; allow exit handles to signal before
            // publishing the result, using a single deadline for the whole app.
            await WaitForProcessesAsync(processes, TimeSpan.FromSeconds(1));
            return anyClosed && !processes.Any(IsProcessRunning);
        }

        private static async Task WaitForProcessesAsync(List<Process> processes, TimeSpan timeout)
        {
            using var deadline = new CancellationTokenSource(timeout);
            await Task.WhenAll(processes.Select(async process =>
            {
                try { await process.WaitForExitAsync(deadline.Token); }
                catch (OperationCanceledException) { }
                catch (InvalidOperationException) { }
                catch (System.ComponentModel.Win32Exception) { }
            }));
        }

        public void AddApp(string path, string? displayName = null)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return;

            string name = Path.GetFileNameWithoutExtension(path);
            if (!string.IsNullOrEmpty(name))
            {
                name = char.ToUpper(name[0]) + name.Substring(1);
            }

            if (MonitoredApps.Any(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase)))
            {
                return;
            }

            MonitoredApps.Add(new MonitoredApp
            {
                Name = string.IsNullOrWhiteSpace(displayName) ? name : displayName.Trim(),
                Path = path,
                CustomTimeout = null
            });

            SaveSettings();
            ReEvaluateTimerState();
        }

        public void RemoveApp(string path)
        {
            var app = MonitoredApps.FirstOrDefault(a => string.Equals(a.Path, path, StringComparison.OrdinalIgnoreCase));
            if (app != null)
            {
                MonitoredApps.Remove(app);
                SaveSettings();
                ReEvaluateTimerState();
            }
        }

        private void SendNotification(string title, string body)
        {
            try { RequestNotification?.Invoke(title, body); }
            catch (Exception ex)
            {
                Debug.WriteLine($"Close notification failed: {ex}");
            }
        }

        private void ToggleLaunchAtStartup(bool enable)
        {
            try
            {
                using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", true))
                {
                    if (key == null) return;
                    if (enable)
                    {
                        string? exePath = Environment.ProcessPath ?? Process.GetCurrentProcess().MainModule?.FileName;
                        if (!string.IsNullOrEmpty(exePath))
                        {
                            key.SetValue("IdleSentry", $"\"{exePath}\" --background");
                        }
                    }
                    else
                    {
                        key.DeleteValue("IdleSentry", false);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to update registry: {ex.Message}");
            }
        }

        private void LoadSettings()
        {
            try
            {
                if (File.Exists(SettingsFile))
                {
                    string json = File.ReadAllText(SettingsFile);
                    var settings = JsonSerializer.Deserialize<SettingsPayload>(json);
                    if (settings != null)
                    {
                        _globalTimeout = settings.GlobalTimeout;
                        _quitMethod = settings.QuitMethod;
                        _isMonitoring = settings.IsMonitoring;
                        _launchAtStartup = settings.LaunchAtStartup;

                        if (settings.MonitoredApps != null)
                        {
                            _monitoredApps = new ObservableCollection<MonitoredApp>(settings.MonitoredApps);
                        }
                    }
                }
                else
                {
                    _globalTimeout = 300;
                    _quitMethod = 0;
                    _isMonitoring = true;
                    _launchAtStartup = false;
                    _monitoredApps = new ObservableCollection<MonitoredApp>();
                }

                // Verify if startup registry matches settings
                try
                {
                    using (var key = Registry.CurrentUser.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Run", false))
                    {
                        bool hasRegistry = key?.GetValue("IdleSentry") != null;
                        _launchAtStartup = hasRegistry;
                    }
                }
                catch
                {
                    // Ignore registry check errors on load
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to load settings: {ex.Message}");
            }
        }

        private void SaveSettings()
        {
            try
            {
                if (!Directory.Exists(SettingsDir))
                {
                    Directory.CreateDirectory(SettingsDir);
                }

                var payload = new SettingsPayload
                {
                    GlobalTimeout = GlobalTimeout,
                    QuitMethod = QuitMethod,
                    IsMonitoring = IsMonitoring,
                    LaunchAtStartup = LaunchAtStartup,
                    MonitoredApps = MonitoredApps.ToList()
                };

                string json = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = true });
                File.WriteAllText(SettingsFile, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"Failed to save settings: {ex.Message}");
            }
        }

        protected void OnPropertyChanged([CallerMemberName] string? name = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        private class SettingsPayload
        {
            public double GlobalTimeout { get; set; }
            public int QuitMethod { get; set; }
            public bool IsMonitoring { get; set; }
            public bool LaunchAtStartup { get; set; }
            public List<MonitoredApp>? MonitoredApps { get; set; }
        }
    }
}
