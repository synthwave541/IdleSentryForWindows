using System.Windows.Controls;

namespace IdleSentry;

// Distinct, cached page targets let NavigationView manage its normal selection,
// keyboard and automation behavior. Their views are declared in MainWindow.xaml.
public sealed class DashboardPage : UserControl { }
public sealed class MonitoredAppsPage : UserControl { }
public sealed class InactivityRulesPage : UserControl { }
public sealed class SystemStartupPage : UserControl { }
public sealed class AboutPage : UserControl { }
