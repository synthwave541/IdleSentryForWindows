using System.IO;
using IdleSentry;
using System.Security.Cryptography;
using System.Text.Json;
using Velopack;
using Velopack.Locators;

internal static class UpdateChecks
{
    internal static async Task Run(Action<bool, string> check, string fixture)
    {
        await CheckFramework(check, fixture);
        var fake = new FakeBackend();
        string prefs = Path.Combine(fixture, "updates.json");
        using var service = new UpdateService(fake, prefs);
        check(service.CanCheck && !service.CanInstall, "updates never install before checking and downloading");
        await service.DownloadAsync();
        check(fake.Downloads == 0, "download requires a discovered release");
        int exits = 0;
        service.InstallAndRestart(() => exits++);
        check(exits == 0 && fake.Installs == 0, "no restart without a verified package");
        service.AutomaticChecks = false;
        await service.CheckAutomaticallyAsync();
        check(fake.Checks == 0, "automatic checks respect opt-out");
        using (var restored = new UpdateService(fake, prefs))
            check(!restored.AutomaticChecks, "automatic update preference survives restart");
        fake.Error = true;
        await service.CheckAsync();
        check(!service.IsBusy && service.CanCheck && !service.CanDownload, "offline check recovers without enabling a stale download");
        fake.Error = false;
        await service.CheckAsync();
        check(service.Status.Contains("up to date") && !service.CanDownload, "current version doesn't offer an update");
        fake.Version = "1.0.16";
        int notices = 0;
        service.UpdateFound += _ => notices++;
        await service.CheckAsync();
        check(service.CanDownload && !service.CanInstall && notices == 1, "new release requires an explicit download");
        await service.CheckAsync();
        check(notices == 1, "same version doesn't repeat notifications");
        service.AutomaticChecks = true;
        int checkedBefore = fake.Checks;
        await service.CheckAutomaticallyAsync();
        check(fake.Checks == checkedBefore, "successful checks are limited to once per day automatically");
        fake.Error = true;
        await service.DownloadAsync();
        check(service.CanDownload && !service.CanInstall, "failed or corrupt download can retry but cannot install");
        fake.Error = false;
        fake.Gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var download = service.DownloadAsync();
        await service.CheckAsync();
        await service.DownloadAsync();
        check(service.IsBusy && fake.Checks == checkedBefore && fake.Downloads == 2, "busy update blocks overlapping checks and downloads");
        fake.Gate.SetResult();
        await download;
        check(service.CanInstall && !service.CanCheck && service.Progress == 100, "verified download waits for restart approval");
        service.InstallAndRestart(() => exits++);
        check(fake.Installs == 1 && exits == 1, "installer is prepared before graceful application shutdown");
        fake.InstallError = true;
        service.InstallAndRestart(() => exits++);
        check(exits == 1 && service.CanInstall, "failed installer launch keeps the app open and the update retryable");
        fake.PendingVersion = "1.0.16";
        using var resumed = new UpdateService(fake, Path.Combine(fixture, "resumed.json"));
        check(resumed.CanInstall && !resumed.CanDownload, "downloaded updates survive relaunch without silently installing");
        fake.IsInstalled = false;
        using var portable = new UpdateService(fake, Path.Combine(fixture, "portable.json"));
        await portable.CheckAsync();
        check(!portable.CanCheck && !portable.CanInstall, "unpackaged app offers manual installer guidance");
        File.WriteAllText(prefs, "malformed JSON");
        using var recovered = new UpdateService(fake, prefs);
        check(recovered.AutomaticChecks, "damaged update preferences recover safely");
    }

    private static async Task CheckFramework(Action<bool, string> check, string fixture)
    {
        string source = Path.Combine(fixture, "update-source");
        Directory.CreateDirectory(source);
        string filename = "Zennoris.IdleSentry-1.0.16-full.nupkg";
        byte[] payload = "package download fixture"u8.ToArray();
        File.WriteAllBytes(Path.Combine(source, filename), payload);
        string feed = JsonSerializer.Serialize(new { Assets = new[] { new {
            PackageId = "Zennoris.IdleSentry", Version = "1.0.16", Type = "Full", FileName = filename,
            SHA1 = Convert.ToHexString(SHA1.HashData(payload)), SHA256 = Convert.ToHexString(SHA256.HashData(payload)), Size = payload.Length
        } } });
        File.WriteAllText(Path.Combine(source, "releases.win-x64.json"), feed);
        UpdateManager Manager(string version, string name, string channel = "win-x64")
        {
            string packages = Path.Combine(fixture, name);
            Directory.CreateDirectory(packages);
            return new(source, locator: new TestVelopackLocator("Zennoris.IdleSentry", version,
                packages, fixture, fixture, Path.Combine(fixture, "unused-update.exe"), channel));
        }
        var manager = Manager("1.0.15", "verified-packages");
        var update = await manager.CheckForUpdatesAsync();
        check(update?.TargetFullRelease.Version.ToString() == "1.0.16", "framework discovers a newer release in the installed architecture channel");
        await manager.DownloadUpdatesAsync(update!);
        check(File.ReadAllBytes(Path.Combine(fixture, "verified-packages", filename)).SequenceEqual(payload),
            "framework downloads and verifies the complete package");
        check(await Manager("1.0.16", "current-packages").CheckForUpdatesAsync() == null,
            "framework does not reinstall the current release");
        check(await Manager("1.0.17", "newer-packages").CheckForUpdatesAsync() == null,
            "framework refuses a downgrade");
        // Keep the size identical so this exercises SHA256, rather than just a size check.
        File.WriteAllBytes(Path.Combine(source, filename), Enumerable.Repeat((byte)0, payload.Length).ToArray());
        var corruptManager = Manager("1.0.15", "corrupt-packages");
        var corrupt = await corruptManager.CheckForUpdatesAsync();
        bool rejected = false;
        try { await corruptManager.DownloadUpdatesAsync(corrupt!); }
        catch (Exception) { rejected = true; }
        check(rejected && !File.Exists(Path.Combine(fixture, "corrupt-packages", filename)),
            "framework rejects a corrupt download before it becomes an installable package");
    }

    private sealed class FakeBackend : IUpdateBackend
    {
        public bool IsInstalled { get; set; } = true;
        public string? PendingVersion { get; set; }
        public string? Version;
        public bool Error, InstallError;
        public int Checks, Downloads, Installs;
        public TaskCompletionSource? Gate;
        public Task<string?> CheckAsync() { Checks++; if (Error) throw new IOException(); return Task.FromResult(Version); }
        public async Task DownloadAsync(Action<int> progress, CancellationToken token)
        {
            Downloads++;
            if (Error) throw new IOException();
            if (Gate != null) await Gate.Task;
            progress(100);
        }
        public void PrepareRestart() { if (InstallError) throw new IOException(); Installs++; }
    }
}
