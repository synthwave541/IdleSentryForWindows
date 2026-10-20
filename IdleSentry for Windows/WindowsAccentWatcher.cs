using System.ComponentModel;
using System.Windows;
using System.Windows.Threading;
using Microsoft.Win32;
using Wpf.Ui.Appearance;

namespace IdleSentry;

/// <summary>Keeps application resources in sync with the user's Windows accent.</summary>
internal sealed class WindowsAccentWatcher : IDisposable
{
    private readonly Dispatcher _dispatcher;
    private bool _disposed;

    public WindowsAccentWatcher(Dispatcher dispatcher)
    {
        _dispatcher = dispatcher;
        ApplyCurrentAccent();
        SystemEvents.UserPreferenceChanged += PreferencesChanged;
        SystemParameters.StaticPropertyChanged += SystemPropertyChanged;
    }

    internal static void ApplyCurrentAccent()
    {
        if (Application.Current == null) return;
        ApplicationAccentColorManager.Apply(ApplicationAccentColorManager.GetColorizationColor(),
            ApplicationTheme.Dark, systemAccentColor: true);
    }

    private void PreferencesChanged(object sender, UserPreferenceChangedEventArgs e) => QueueRefresh();

    private void SystemPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (string.IsNullOrEmpty(e.PropertyName) || e.PropertyName is "WindowGlassColor" or "WindowGlassBrush")
            QueueRefresh();
    }

    private void QueueRefresh()
    {
        if (_disposed || _dispatcher.HasShutdownStarted) return;
        _dispatcher.BeginInvoke(DispatcherPriority.Background, new Action(() =>
        {
            if (!_disposed && !_dispatcher.HasShutdownStarted) ApplyCurrentAccent();
        }));
    }

    public void Dispose()
    {
        _disposed = true;
        SystemEvents.UserPreferenceChanged -= PreferencesChanged;
        SystemParameters.StaticPropertyChanged -= SystemPropertyChanged;
    }
}
