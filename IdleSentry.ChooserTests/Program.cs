using System.IO;
using System.Runtime.InteropServices;
using IdleSentry;

internal static class Program
{
    private static int _checks;

    [STAThread]
    private static int Main(string[] args)
    {
        Velopack.VelopackApp.Build().SetArgs(Array.Empty<string>()).SetAutoApplyOnStartup(false).Run();
        string fixtures = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "IdleSentry-chooser-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(fixtures);
        try
        {
            Run(fixtures);
            UpdateChecks.Run(Check, fixtures).GetAwaiter().GetResult();
            string? previewDirectory = args.Length == 2 && args[0] == "--preview-dir"
                ? System.IO.Path.GetFullPath(args[1]) : null;
            StatusPresentationChecks.Run(Check, previewDirectory);
            Console.WriteLine($"PASS: {_checks} chooser and status checks");
            return 0;
        }
        catch (Exception ex) { Console.Error.WriteLine(ex); return 1; }
        finally
        {
            // The target is exactly the fresh fixture directory created above.
            Directory.Delete(fixtures, recursive: true);
        }
    }

    private static void Check(bool passed, string label)
    {
        if (!passed) throw new InvalidOperationException("FAILED: " + label);
        _checks++;
    }

    private static void Run(string fixtures)
    {
        string executable = System.IO.Path.Combine(fixtures, "ExampleApp.exe");
        File.Copy(Environment.ProcessPath!, executable);
        string document = System.IO.Path.Combine(fixtures, "notes.txt");
        File.WriteAllText(document, "fixture");
        Check(AppDiscovery.NormalizeAppPath(executable) == executable, "accept an existing application");
        Check(AppDiscovery.NormalizeAppPath('"' + executable + '"') == executable, "quoted App Paths registration");
        Check(AppDiscovery.NormalizeAppPath(document) == null, "exclude documents");
        Check(AppDiscovery.NormalizeAppPath(@"\\unreachable.invalid\apps\app.exe") == null,
            "network shortcut targets are rejected without probing the remote file");
        Check(AppDiscovery.NormalizeAppPath(Environment.ProcessPath) == null, "exclude IdleSentry's own running executable");
        Check(AppDiscovery.NormalizeAppPath(System.IO.Path.Combine(fixtures, "missing.exe")) == null, "exclude broken targets");

        var merged = new Dictionary<string, AppChoice>(StringComparer.OrdinalIgnoreCase);
        AppDiscovery.MergeApp(merged, executable, "Friendly App", false);
        AppDiscovery.MergeApp(merged, executable.ToUpperInvariant(), "Unfriendly executable name", true);
        Check(merged.Count == 1, "case-insensitive path deduplication across discovery sources");
        var app = merged.Values.Single();
        Check(app.Name == "Friendly App" && app.IsRunning, "preserve Start menu name and merge running state");
        Check(AppPickerWindow.MatchesSearch(app, "  fRiEnDlY  "), "trimmed case-insensitive search");
        Check(!AppPickerWindow.MatchesSearch(app, "missing"), "unmatched search");
        app.AlreadyMonitored = true;
        Check(!app.CanSelect && app.Status == "Already added", "already monitored apps cannot be selected twice");

        string updater = System.IO.Path.Combine(fixtures, "Update.exe");
        File.Copy(Environment.ProcessPath!, updater);
        foreach (string version in new[] { "1.9.0", "1.10.0" })
        {
            string folder = System.IO.Path.Combine(fixtures, "app-" + version);
            Directory.CreateDirectory(folder);
            File.Copy(executable, System.IO.Path.Combine(folder, "ChatApp.exe"));
        }
        string resolved = System.IO.Path.Combine(fixtures, "app-1.10.0", "ChatApp.exe");
        Check(AppDiscovery.ResolveShortcutTarget(updater, "--processStart \"ChatApp.exe\"") == resolved,
            "Electron shortcut resolves the newest numeric version, not the updater");
        Check(AppDiscovery.ResolveShortcutTarget(updater, "--processStart ..\\ChatApp.exe") == null,
            "do not resolve paths outside versioned app folders");
        Check(AppDiscovery.NormalizeAppPath(updater) == null, "shared updater is not monitorable");

        string shortcuts = System.IO.Path.Combine(fixtures, "Shortcuts");
        Directory.CreateDirectory(shortcuts);
        object shell = Activator.CreateInstance(Type.GetTypeFromProgID("WScript.Shell")!)!;
        try
        {
            CreateShortcut(shell, shortcuts, "Friendly Test App", executable);
            CreateShortcut(shell, shortcuts, "Friendly Test App copy", executable);
            CreateShortcut(shell, shortcuts, "Broken app", System.IO.Path.Combine(fixtures, "missing.exe"));
            CreateShortcut(shell, shortcuts, "A document", document);
            CreateShortcut(shell, shortcuts, "Chat App", updater, "--processStart ChatApp.exe");
            CreateShortcut(shell, shortcuts, "Updater alone", updater);
        }
        finally { Marshal.FinalReleaseComObject(shell); }
        var discovered = AppDiscovery.DiscoverAsync(CancellationToken.None, new[] { shortcuts }, false, false).GetAwaiter().GetResult();
        Check(discovered.Count == 2, "actual shortcut COM discovery excludes broken, duplicate, document and shared targets");
        Check(discovered.Any(candidate => candidate.Name.StartsWith("Friendly Test App") && candidate.Path == executable),
            "real shortcut discovery retains friendly names");
        Check(discovered.Single(candidate => candidate.Name == "Chat App").Path == resolved, "actual shortcut arguments are resolved");
        Check(discovered.All(candidate => !candidate.IsSelected), "discovery never preselects applications");

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        try
        {
            AppDiscovery.DiscoverAsync(cancelled.Token, new[] { shortcuts }, false, false).GetAwaiter().GetResult();
            throw new InvalidOperationException("Discovery ignored cancellation");
        }
        catch (OperationCanceledException) { _checks++; }

        var live = AppDiscovery.DiscoverAsync(CancellationToken.None).GetAwaiter().GetResult();
        Check(live.Select(candidate => candidate.Path).Distinct(StringComparer.OrdinalIgnoreCase).Count() == live.Count,
            "live inventory has no duplicate executables");
        Check(live.All(candidate => AppDiscovery.NormalizeAppPath(candidate.Path) != null), "live inventory contains usable app targets");
        Check(live.SequenceEqual(live.OrderByDescending(candidate => candidate.IsRunning)
            .ThenBy(candidate => candidate.Name, StringComparer.CurrentCultureIgnoreCase)), "live inventory puts running apps first");
        Console.WriteLine($"Live discovery: {live.Count} apps, {live.Count(candidate => candidate.IsRunning)} running");
    }

    private static void CreateShortcut(object shell, string folder, string name, string target, string arguments = "")
    {
        object shortcut = ((dynamic)shell).CreateShortcut(System.IO.Path.Combine(folder, name + ".lnk"));
        try
        {
            ((dynamic)shortcut).TargetPath = target;
            ((dynamic)shortcut).Arguments = arguments;
            ((dynamic)shortcut).Save();
        }
        finally { Marshal.FinalReleaseComObject(shortcut); }
    }
}
