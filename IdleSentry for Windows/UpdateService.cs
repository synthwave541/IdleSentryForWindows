using System.ComponentModel;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Windows;
using System.Windows.Threading;
using Velopack;
using Velopack.Sources;

namespace IdleSentry;

public interface IUpdateBackend
{
    bool IsInstalled { get; }
    string? PendingVersion { get; }
    Task<string?> CheckAsync();
    Task DownloadAsync(Action<int> progress, CancellationToken cancellationToken);
    void PrepareRestart();
}

internal sealed class GitHubUpdateBackend : IUpdateBackend
{
    private readonly UpdateManager _manager = new(new GithubSource(
        "https://github.com/synthwave541/IdleSentryForWindows", null, false));
    private UpdateInfo? _candidate;
    public bool IsInstalled => _manager.IsInstalled && !Velopack.Locators.VelopackLocator.Current.IsPortable;
    public string? PendingVersion => _manager.UpdatePendingRestart?.Version.ToString();
    public async Task<string?> CheckAsync()
    {
        _candidate = await _manager.CheckForUpdatesAsync();
        return _candidate?.TargetFullRelease.Version.ToString();
    }
    public Task DownloadAsync(Action<int> progress, CancellationToken token) =>
        _manager.DownloadUpdatesAsync(_candidate ?? throw new InvalidOperationException("Check for updates first."), progress, token);
    public void PrepareRestart() => _manager.WaitExitThenApplyUpdates(null, silent: false, restart: true);
}

public enum UpdatePhase { Idle, Checking, Available, Downloading, Ready, Unavailable }

/// Owns UI state; package selection, hashes and installation are handled by Velopack.
public sealed class UpdateService : INotifyPropertyChanged, IDisposable
{
    private static readonly Lazy<UpdateService> Instance = new(() => new(new GitHubUpdateBackend(),
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IdleSentry", "updates.json")));
    public static UpdateService Shared => Instance.Value;
    private readonly IUpdateBackend _backend;
    private readonly string _preferencesPath;
    private readonly CancellationTokenSource _shutdown = new();
    private DispatcherTimer? _timer;
    private UpdatePreferences _preferences = new();
    public UpdatePhase Phase { get; private set; }
    public string Status { get; private set; } = "Updates come from IdleSentry’s official GitHub releases.";
    public string? AvailableVersion { get; private set; }
    public int Progress { get; private set; }
    public bool IsBusy => Phase is UpdatePhase.Checking or UpdatePhase.Downloading;
    public bool CanCheck => !IsBusy && Phase is not UpdatePhase.Ready and not UpdatePhase.Unavailable;
    public bool CanDownload => Phase == UpdatePhase.Available;
    public bool CanInstall => Phase == UpdatePhase.Ready;
    public bool AutomaticChecks
    {
        get => _preferences.AutomaticChecks;
        set { _preferences.AutomaticChecks = value; SavePreferences(); Notify(); }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
    public event Action<string>? UpdateFound;

    public UpdateService(IUpdateBackend backend, string preferencesPath)
    {
        _backend = backend;
        _preferencesPath = preferencesPath;
        try { _preferences = JsonSerializer.Deserialize<UpdatePreferences>(File.ReadAllText(preferencesPath)) ?? new(); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException) { }
        if (!backend.IsInstalled)
        {
            Phase = UpdatePhase.Unavailable;
            Status = "Install the 1.0.15 or newer Windows Setup to enable in-app updates. Portable and older installations update manually.";
        }
        else if (backend.PendingVersion is string version)
        {
            AvailableVersion = version;
            Phase = UpdatePhase.Ready;
            Status = $"Version {version} is downloaded. Install whenever you’re ready.";
        }
    }

    public async Task StartAsync()
    {
        if (Phase == UpdatePhase.Unavailable || _timer != null) return;
        _timer = new DispatcherTimer { Interval = TimeSpan.FromHours(6) };
        _timer.Tick += async (_, _) => await CheckAutomaticallyAsync();
        _timer.Start();
        try { await Task.Delay(TimeSpan.FromSeconds(15), _shutdown.Token); }
        catch (OperationCanceledException) { return; }
        await CheckAutomaticallyAsync();
    }

    public Task CheckAutomaticallyAsync() => AutomaticChecks &&
        DateTimeOffset.UtcNow - _preferences.LastCheckedUtc >= TimeSpan.FromDays(1)
        ? CheckAsync() : Task.CompletedTask;

    public async Task CheckAsync()
    {
        if (!CanCheck) return;
        string? previous = AvailableVersion;
        Phase = UpdatePhase.Checking;
        Status = "Checking GitHub for updates…";
        NotifyAll();
        try
        {
            AvailableVersion = await _backend.CheckAsync();
            _preferences.LastCheckedUtc = DateTimeOffset.UtcNow;
            SavePreferences();
            Phase = AvailableVersion == null ? UpdatePhase.Idle : UpdatePhase.Available;
            Status = AvailableVersion == null ? "You’re up to date." : $"Version {AvailableVersion} is available.";
            if (AvailableVersion != null && AvailableVersion != previous) UpdateFound?.Invoke(AvailableVersion);
        }
        catch (Exception)
        {
            // A failed recheck must not leave a stale candidate enabled for download.
            AvailableVersion = null;
            Phase = UpdatePhase.Idle;
            Status = "Couldn’t check for updates. Check your connection and try again later.";
        }
        NotifyAll();
    }

    public async Task DownloadAsync()
    {
        if (!CanDownload) return;
        Phase = UpdatePhase.Downloading;
        Progress = 0;
        Status = $"Downloading version {AvailableVersion}…";
        NotifyAll();
        var progress = new Progress<int>(value => { if (Phase != UpdatePhase.Downloading) return; Progress = Math.Clamp(value, 0, 100); Notify(nameof(Progress)); });
        try
        {
            await _backend.DownloadAsync(((IProgress<int>)progress).Report, _shutdown.Token);
            Phase = UpdatePhase.Ready;
            Progress = 100;
            Status = $"Version {AvailableVersion} is downloaded and verified. Install whenever you’re ready.";
        }
        catch (Exception)
        {
            Phase = UpdatePhase.Available;
            Status = "The download didn’t finish or couldn’t be verified. Try downloading again.";
        }
        NotifyAll();
    }

    public void InstallAndRestart(Action gracefulShutdown)
    {
        if (!CanInstall) return;
        try
        {
            _backend.PrepareRestart();
            gracefulShutdown();
        }
        catch (Exception)
        {
            Status = "Couldn’t start the installer. Your current app is unchanged; please try again.";
            NotifyAll();
        }
    }

    private void SavePreferences()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(_preferencesPath)!);
            string temporary = _preferencesPath + ".tmp";
            File.WriteAllText(temporary, JsonSerializer.Serialize(_preferences));
            File.Move(temporary, _preferencesPath, overwrite: true);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { }
    }
    private void Notify([CallerMemberName] string? name = null) => PropertyChanged?.Invoke(this, new(name));
    private void NotifyAll() => Notify(string.Empty);
    public void Dispose() { _timer?.Stop(); _shutdown.Cancel(); _shutdown.Dispose(); }
    public sealed class UpdatePreferences
    {
        public bool AutomaticChecks { get; set; } = true;
        public DateTimeOffset LastCheckedUtc { get; set; }
    }
}
