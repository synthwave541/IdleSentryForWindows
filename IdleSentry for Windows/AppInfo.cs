using System.Reflection;

namespace IdleSentry;

/// <summary>Shared display labels sourced from the built application's version.</summary>
public static class AppInfo
{
    public static string Version { get; } =
        typeof(AppInfo).Assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
            ?.InformationalVersion.Split('+')[0]
        ?? typeof(AppInfo).Assembly.GetName().Version?.ToString(3)
        ?? "Unknown";

    public static string SidebarVersion => $"v{Version}";
    public static string AboutVersion => $"Version {Version}";
}
