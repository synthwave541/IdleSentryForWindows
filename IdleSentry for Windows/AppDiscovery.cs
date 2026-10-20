using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Windows.Media;
using Microsoft.Win32;

namespace IdleSentry;

internal sealed class AppChoice : INotifyPropertyChanged
{
    private bool _isSelected;
    public required string Name { get; init; }
    public required string Path { get; init; }
    public ImageSource? Icon { get; init; }
    public bool HasIcon => Icon != null;
    public bool IsRunning { get; set; }
    public bool IsInstalled { get; set; }
    public bool AlreadyMonitored { get; set; }
    public bool CanSelect => !AlreadyMonitored;
    public string Status => AlreadyMonitored ? "Already added" : IsRunning ? "Running now" : IsInstalled ? "Installed" : "Available";
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (_isSelected == value) return;
            _isSelected = value;
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(IsSelected)));
        }
    }
    public event PropertyChangedEventHandler? PropertyChanged;
}

/// <summary>Reads app registrations and shortcuts; never launches their targets.</summary>
internal static class AppDiscovery
{
    private static readonly HashSet<string> SharedHosts = new(StringComparer.OrdinalIgnoreCase)
    {
        "explorer.exe", "rundll32.exe", "msiexec.exe", "wscript.exe", "cscript.exe",
        "cmd.exe", "powershell.exe", "pwsh.exe", "update.exe", "applicationframehost.exe",
        "svchost.exe", "taskhostw.exe", "dllhost.exe", "runtimebroker.exe",
        "shellexperiencehost.exe", "startmenuexperiencehost.exe", "searchhost.exe",
        "uninstall.exe", "unins000.exe", "setup.exe", "control.exe", "mmc.exe",
        "textinputhost.exe", "lockapp.exe", "shellhost.exe"
    };

    [DllImport("user32.dll")]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool IsWindowVisible(IntPtr window);

    [StructLayout(LayoutKind.Sequential)]
    private struct WindowRect { public int Left, Top, Right, Bottom; }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr window, out WindowRect rect);

    [DllImport("user32.dll", EntryPoint = "GetWindowLongW")]
    private static extern int GetWindowLong(IntPtr window, int index);

    [DllImport("dwmapi.dll")]
    private static extern int DwmGetWindowAttribute(IntPtr window, int attribute, out int value, int size);

    private static bool IsAppWindow(IntPtr window)
    {
        if (window == IntPtr.Zero || !IsWindowVisible(window)) return false;
        // Omit utility/tool windows, cloaked shell windows and invisible service surfaces.
        if ((GetWindowLong(window, -20) & 0x80) != 0) return false; // WS_EX_TOOLWINDOW
        if (DwmGetWindowAttribute(window, 14, out int cloaked, sizeof(int)) == 0 && cloaked != 0) return false;
        return GetWindowRect(window, out var rect) && rect.Right - rect.Left >= 64 && rect.Bottom - rect.Top >= 32;
    }

    public static Task<List<AppChoice>> DiscoverAsync(CancellationToken cancellationToken,
        IEnumerable<string>? shortcutRoots = null, bool includeRunning = true, bool includeRegistered = true)
    {
        // Windows shortcut COM objects require an STA. Keep discovery off the UI thread.
        var completion = new TaskCompletionSource<List<AppChoice>>(TaskCreationOptions.RunContinuationsAsynchronously);
        var thread = new Thread(() =>
        {
            try
            {
                var apps = new Dictionary<string, AppChoice>(StringComparer.OrdinalIgnoreCase);
                ReadShortcuts(apps, shortcutRoots ?? DefaultShortcutRoots(), cancellationToken);
                if (includeRegistered) ReadRegisteredApps(apps, cancellationToken);
                if (includeRunning) ReadRunningApps(apps, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();
                completion.TrySetResult(apps.Values.OrderByDescending(app => app.IsRunning)
                    .ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList());
            }
            catch (OperationCanceledException) { completion.TrySetCanceled(cancellationToken); }
            catch (Exception ex) { completion.TrySetException(ex); }
        }) { IsBackground = true, Name = "IdleSentry app discovery" };
        thread.SetApartmentState(ApartmentState.STA);
        thread.Start();
        return completion.Task;
    }

    private static IEnumerable<string> DefaultShortcutRoots() => new[]
    {
        Environment.GetFolderPath(Environment.SpecialFolder.Programs),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonPrograms),
        Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
        Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
    }.Where(path => !string.IsNullOrWhiteSpace(path)).Distinct(StringComparer.OrdinalIgnoreCase);

    internal static string? NormalizeAppPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return null;
        try
        {
            path = Environment.ExpandEnvironmentVariables(path.Trim().Trim('"'));
            if (!IsLocalPath(path) || !File.Exists(path)
                || !string.Equals(System.IO.Path.GetExtension(path), ".exe", StringComparison.OrdinalIgnoreCase)) return null;
            path = System.IO.Path.GetFullPath(path);
            if (SharedHosts.Contains(System.IO.Path.GetFileName(path))
                || string.Equals(path, Environment.ProcessPath, StringComparison.OrdinalIgnoreCase)) return null;
            return path;
        }
        catch (Exception ex) when (ex is ArgumentException or IOException or UnauthorizedAccessException or NotSupportedException)
        { return null; }
    }

    private static bool IsLocalPath(string path)
    {
        if (path.StartsWith(@"\\", StringComparison.Ordinal) || !System.IO.Path.IsPathFullyQualified(path)) return false;
        string? root = System.IO.Path.GetPathRoot(path);
        return !string.IsNullOrEmpty(root) && new DriveInfo(root).DriveType != DriveType.Network;
    }

    internal static void MergeApp(Dictionary<string, AppChoice> apps, string? path, string? name, bool isRunning)
    {
        var normalized = NormalizeAppPath(path);
        if (normalized == null) return;
        if (apps.TryGetValue(normalized, out var existing))
        {
            existing.IsRunning |= isRunning;
            existing.IsInstalled |= !isRunning;
            return;
        }
        apps.Add(normalized, new AppChoice
        {
            Name = string.IsNullOrWhiteSpace(name) ? GetDisplayName(normalized) : name.Trim(),
            Path = normalized,
            IsRunning = isRunning,
            IsInstalled = !isRunning,
            Icon = AppState.GetAppIcon(normalized)
        });
    }

    internal static string GetDisplayName(string path)
    {
        try
        {
            string? description = FileVersionInfo.GetVersionInfo(path).FileDescription;
            if (!string.IsNullOrWhiteSpace(description))
            {
                description = description.Trim();
                return description.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)
                    ? System.IO.Path.GetFileNameWithoutExtension(description) : description;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException) { }
        return System.IO.Path.GetFileNameWithoutExtension(path);
    }

    private static void ReadShortcuts(Dictionary<string, AppChoice> apps, IEnumerable<string> roots, CancellationToken token)
    {
        object? shell = null;
        try
        {
            var shellType = Type.GetTypeFromProgID("WScript.Shell");
            if (shellType == null) return;
            shell = Activator.CreateInstance(shellType);
            if (shell == null) return;
            foreach (string root in roots.Distinct(StringComparer.OrdinalIgnoreCase))
            {
                token.ThrowIfCancellationRequested();
                if (string.IsNullOrWhiteSpace(root) || !IsLocalPath(root) || !Directory.Exists(root)) continue;
                try
                {
                    var options = new EnumerationOptions
                    {
                        RecurseSubdirectories = true, IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.ReparsePoint | FileAttributes.System
                    };
                    foreach (string shortcutPath in Directory.EnumerateFiles(root, "*.lnk", options))
                    {
                        token.ThrowIfCancellationRequested();
                        object? shortcut = null;
                        try
                        {
                            shortcut = ((dynamic)shell).CreateShortcut(shortcutPath);
                            string target = ((dynamic)shortcut).TargetPath;
                            string arguments = ((dynamic)shortcut).Arguments;
                            MergeApp(apps, ResolveShortcutTarget(target, arguments),
                                System.IO.Path.GetFileNameWithoutExtension(shortcutPath), false);
                        }
                        catch (Exception ex) when (ex is not OperationCanceledException)
                        { Debug.WriteLine($"Skipped app shortcut: {ex.Message}"); }
                        finally { ReleaseComObject(shortcut); }
                    }
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                { Debug.WriteLine($"Skipped shortcut folder: {ex.Message}"); }
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        { Debug.WriteLine($"Shortcut discovery unavailable: {ex.Message}"); }
        finally { ReleaseComObject(shell); }
    }

    internal static string? ResolveShortcutTarget(string target, string arguments)
    {
        target = Environment.ExpandEnvironmentVariables(target.Trim().Trim('"'));
        if (!IsLocalPath(target)) return null;
        // Squirrel/Electron shortcuts point to a shared updater. Resolve their real app.
        if (!string.Equals(System.IO.Path.GetFileName(target), "Update.exe", StringComparison.OrdinalIgnoreCase))
            return NormalizeAppPath(target);
        var match = Regex.Match(arguments, "(?:^|\\s)--processStart(?:=|\\s+)(?:\"([^\"]+\\.exe)\"|([^\\s\"]+\\.exe))",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (!match.Success) return null;
        string executable = match.Groups[1].Success ? match.Groups[1].Value : match.Groups[2].Value;
        if (System.IO.Path.GetFileName(executable) != executable) return null;
        string? folder = System.IO.Path.GetDirectoryName(target);
        if (string.IsNullOrEmpty(folder) || !Directory.Exists(folder)) return null;
        try
        {
            return Directory.EnumerateDirectories(folder, "app-*")
                .OrderByDescending(path => Version.TryParse(System.IO.Path.GetFileName(path)[4..], out var version) ? version : new Version())
                .Select(path => NormalizeAppPath(System.IO.Path.Combine(path, executable)))
                .FirstOrDefault(path => path != null);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException) { return null; }
    }

    private static void ReadRegisteredApps(Dictionary<string, AppChoice> apps, CancellationToken token)
    {
        foreach (var hive in new[] { RegistryHive.CurrentUser, RegistryHive.LocalMachine })
        foreach (var view in new[] { RegistryView.Registry64, RegistryView.Registry32 })
        {
            token.ThrowIfCancellationRequested();
            try
            {
                using var baseKey = RegistryKey.OpenBaseKey(hive, view);
                using var appPaths = baseKey.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\App Paths");
                if (appPaths == null) continue;
                foreach (var keyName in appPaths.GetSubKeyNames())
                {
                    token.ThrowIfCancellationRequested();
                    try
                    {
                        using var key = appPaths.OpenSubKey(keyName);
                        MergeApp(apps, key?.GetValue(null) as string, null, false);
                    }
                    catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
                }
            }
            catch (Exception ex) when (ex is System.Security.SecurityException or UnauthorizedAccessException or IOException) { }
        }
    }

    private static void ReadRunningApps(Dictionary<string, AppChoice> apps, CancellationToken token)
    {
        foreach (var process in Process.GetProcesses())
        {
            using (process)
            {
                token.ThrowIfCancellationRequested();
                try
                {
                    if (process.Id == Environment.ProcessId || !IsAppWindow(process.MainWindowHandle)) continue;
                    MergeApp(apps, AppState.SafeGetProcessPath(process), null, true);
                }
                catch (Exception ex) when (ex is InvalidOperationException or System.ComponentModel.Win32Exception or NotSupportedException) { }
            }
        }
    }

    private static void ReleaseComObject(object? value)
    {
        if (value != null && Marshal.IsComObject(value)) Marshal.FinalReleaseComObject(value);
    }
}
