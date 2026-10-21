using System.IO;
using System.Windows;

namespace IdleSentry;

/// <summary>The Store channel is selected at build time, never by a user preference.</summary>
public static class AppDistribution
{
#if STORE
    public static bool IsStore => true;
    // Explicit package-owned storage avoids sharing/virtualizing the standalone settings file.
    public static string SettingsDirectory => Windows.Storage.ApplicationData.Current.LocalFolder.Path;
    public static string SettingsLocationDescription => "Preferences are saved locally for this Microsoft Store installation. Standalone settings are kept separately.";
#else
    public static bool IsStore => false;
    public static string SettingsDirectory => Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IdleSentry");
    public static string SettingsLocationDescription => @"Preferences are saved in %LOCALAPPDATA%\IdleSentry\settings.json with zero cloud transmission.";
#endif
    public static Visibility StandaloneUpdatesVisibility => IsStore ? Visibility.Collapsed : Visibility.Visible;
    public static Visibility StoreUpdatesVisibility => IsStore ? Visibility.Visible : Visibility.Collapsed;
}
