#if STORE
using Windows.ApplicationModel;
using Windows.ApplicationModel.Activation;

namespace IdleSentry;

internal static class StoreIntegration
{
    internal const string StartupTaskId = "IdleSentryStartup";

    internal static bool IsStartupActivation =>
        // Direct executable launches have identity but may have no activation arguments.
        AppInstance.GetActivatedEventArgs()?.Kind == ActivationKind.StartupTask;

    internal static async Task<bool> IsStartupEnabledAsync()
    {
        var task = await StartupTask.GetAsync(StartupTaskId);
        return task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
    }

    internal static async Task<(bool Enabled, string? Message)> SetStartupAsync(bool enabled)
    {
        var task = await StartupTask.GetAsync(StartupTaskId);
        if (!enabled)
        {
            if (task.State == StartupTaskState.EnabledByPolicy)
                return (true, "Startup is enabled by your organization and cannot be changed here.");
            task.Disable();
        }
        else if (task.State == StartupTaskState.DisabledByUser)
            return (false, "Windows has disabled startup for IdleSentry. Enable it in Settings → Apps → Startup.");
        else if (task.State == StartupTaskState.DisabledByPolicy)
            return (false, "Startup is disabled by your organization.");
        else if (task.State == StartupTaskState.Disabled)
            await task.RequestEnableAsync();

        bool actual = task.State is StartupTaskState.Enabled or StartupTaskState.EnabledByPolicy;
        return (actual, actual == enabled ? null : "Windows could not change startup. Check Settings → Apps → Startup.");
    }
}
#endif
