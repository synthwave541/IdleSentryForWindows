using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Threading;
using System.Windows.Automation.Peers;
using System.Windows.Automation.Provider;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IdleSentry;

internal static class StatusPresentationChecks
{
    // Frame navigation needs the dispatcher, but tests must never start monitoring
    // or load the real user's settings when Application's queued Startup runs.
    private sealed class SnapshotApplication : App
    {
        protected override void OnStartup(StartupEventArgs e) { }
    }
    // Snapshot construction deliberately avoids settings, timers and process termination.
    private static AppState Snapshot()
    {
        var state = (AppState)RuntimeHelpers.GetUninitializedObject(typeof(AppState));
        SetField(state, "_monitoredApps", new ObservableCollection<MonitoredApp>
        {
            new() { Name = "Example Editor", Path = @"C:\Preview\editor.exe" }
        });
        SetField(state, "_runningStates", new ObservableCollection<AppRunningState>
        {
            new() { Name = "Example Editor", Path = @"C:\Preview\editor.exe", IsRunning = true,
                IsCountingDown = true, TimeRemaining = 49.8 }
        });
        SetField(state, "_globalTimeout", 60d);
        SetField(state, "_isMonitoring", true);
        SetField(state, "_isCountdownActive", true);
        return state;
    }

    private static void SetField(AppState state, string name, object value) =>
        typeof(AppState).GetField(name, BindingFlags.NonPublic | BindingFlags.Instance)!.SetValue(state, value);

    private static string Time(double seconds) => (string)new SecondsToTimerConverter()
        .Convert(seconds, typeof(string), null!, CultureInfo.InvariantCulture);

    public static void Run(Action<bool, string> check, string? previewDirectory)
    {
        var state = Snapshot();
        check(state.HasAppsPendingClose && state.ActivityStatusText.Contains("Example Editor"), "summary identifies the next app");
        check(!state.IsCloseImminent, "normal countdown stays visually quiet");
        check(Time(state.CountdownValue) == "00:50", "display rounds up to avoid showing zero too early");
        check(Time(60.1) == "01:01" && Time(-1) == "00:00" && Time(double.NaN) == "00:00", "countdown formatting boundaries");
        state.RunningStates[0].TimeRemaining = 9.2;
        check(state.IsCloseImminent, "final ten seconds use the urgent state");
        SetField(state, "_isCountdownActive", false);
        check(!state.HasAppsPendingClose && state.ActivityStatusText == "Monitoring · 1 app running", "activity reset clears countdown summary");
        SetField(state, "_isCountdownActive", true);
        SetField(state, "_isMonitoring", false);
        check(!state.HasAppsPendingClose && !state.IsCloseImminent && state.ActivityStatusText == "Monitoring paused", "pause suppresses stale countdowns");
        state = Snapshot();
        state.MonitoredApps.Add(new MonitoredApp { Name = "Example Browser", Path = @"C:\Preview\browser.exe", CustomTimeout = 120 });
        state.RunningStates.Add(new AppRunningState { Name = "Example Browser", Path = @"C:\Preview\browser.exe", IsRunning = true, TimeRemaining = 20 });
        check(state.CountdownValue == 20 && state.ActivityStatusText == "Next to close · Example Browser (+1 more)", "different app timeouts show the earliest closure once");
        foreach (var app in state.RunningStates) app.IsRunning = false;
        check(!state.HasAppsPendingClose && state.ActivityStatusText == "No monitored apps running", "stopped apps do not produce a countdown");
        state.MonitoredApps.Clear();
        check(state.ActivityStatusText == "Add an app to get started" && state.MonitoredAppsSummary == "0 apps", "empty state has no phantom countdown");
        check(Snapshot().MonitoredAppsSummary == "1 app", "singular monitored-app label");

        VerifyClosingQueue(check);

        // Render the actual view using snapshots; no desktop interaction or live app settings.
        var application = new SnapshotApplication { ShutdownMode = ShutdownMode.OnExplicitShutdown };
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Wpf.Ui;component/Resources/Theme/Dark.xaml")
        });
        application.Resources.MergedDictionaries.Add(new ResourceDictionary
        {
            Source = new Uri("pack://application:,,,/Wpf.Ui;component/Resources/Wpf.Ui.xaml")
        });
        using var accentWatcher = new WindowsAccentWatcher(application.Dispatcher);
        check((Color)application.Resources["SystemAccentColor"] == Wpf.Ui.Appearance.ApplicationAccentColorManager.GetColorizationColor(),
            "controls use the Windows system accent instead of the theme placeholder");
        typeof(App).GetProperty(nameof(App.AppState))!.SetValue(application, state = Snapshot());
        var trayMenu = new ContextMenu();
        var openTrayMenu = typeof(App).GetMethod("ContextMenu_Opened", BindingFlags.Instance | BindingFlags.NonPublic)!;
        openTrayMenu.Invoke(application, new object[] { trayMenu, new RoutedEventArgs() });
        check(trayMenu.Items.OfType<MenuItem>().Count(item => Regex.IsMatch(item.Header?.ToString() ?? string.Empty, @"\d{2,}:\d{2}")) == 1,
            "tray menu uses one shared countdown rather than a timer on every app");
        SetField(state, "_isMonitoring", false);
        openTrayMenu.Invoke(application, new object[] { trayMenu, new RoutedEventArgs() });
        check(!trayMenu.Items.OfType<MenuItem>().Any(item => Regex.IsMatch(item.Header?.ToString() ?? string.Empty, @"\d{2,}:\d{2}")),
            "paused tray menu does not show a countdown");
        var windowState = Snapshot();
        var window = new MainWindow(windowState);
        var root = (FrameworkElement)window.Content;
        Layout(root, 960, 680);
        var timer = (TextBlock)window.FindName("ActivityCountdown");
        var activityBar = (FrameworkElement)window.FindName("ActivityBar");
        var navigation = (Wpf.Ui.Controls.NavigationView)window.FindName("SidebarNavigation");
        var frame = (Wpf.Ui.Controls.NavigationViewContentPresenter)navigation.Template.FindName("PART_NavigationViewContentPresenter", navigation);
        var indicator = (System.Windows.Shapes.Rectangle)navigation.Template.FindName("SelectionIndicator", navigation);
        check(navigation.MenuItems.Count == 5 && navigation.MenuItems.OfType<Wpf.Ui.Controls.NavigationViewItem>().Count() == 5,
            "sidebar uses Fluent NavigationView items rather than custom radio buttons");
        check(navigation.SelectedItem == window.FindName("NavDashboard") && navigation.SelectedItem.IsActive,
            "Dashboard is selected in both the sidebar and page host on startup");
        check(indicator.Fill is SolidColorBrush accent && accent.Color == ((SolidColorBrush)application.Resources["AccentTextFillColorPrimaryBrush"]).Color,
            "sliding selection line follows the Windows accent palette");
        // Updating app resources must also recolor the existing indicator, without recreating the view.
        var originalAccent = application.Resources["AccentTextFillColorPrimaryBrush"];
        application.Resources["AccentTextFillColorPrimaryBrush"] = new SolidColorBrush(Colors.LimeGreen);
        check(indicator.Fill is SolidColorBrush changed && changed.Color == Colors.LimeGreen,
            "selection line responds to an accent change while the window exists");
        application.Resources["AccentTextFillColorPrimaryBrush"] = originalAccent;
        foreach (var item in navigation.MenuItems.OfType<Wpf.Ui.Controls.NavigationViewItem>())
        {
            // Use the same SelectionItem provider that accessibility tools and keyboard navigation use.
            var peer = UIElementAutomationPeer.CreatePeerForElement(item);
            ((ISelectionItemProvider)peer.GetPattern(PatternInterface.SelectionItem)).Select();
            Layout(root, 960, 680);
            check(navigation.SelectedItem == item && frame.Content.GetType() == item.TargetPageType
                && navigation.MenuItems.OfType<Wpf.Ui.Controls.NavigationViewItem>().Count(candidate => candidate.IsActive) == 1,
                $"native selection of {item.Content} updates the page and leaves exactly one active item");
            double markerY = indicator.TranslatePoint(new Point(0, indicator.Height / 2), root).Y;
            double itemY = item.TranslatePoint(new Point(0, item.ActualHeight / 2), root).Y;
            check(Math.Abs(markerY - itemY) < 1, $"selection line is centered beside {item.Content}");
        }
        check(!navigation.Navigate("unknown") && frame.Content is AboutPage, "unknown navigation preserves the current page");
        navigation.Navigate("0");
        Layout(root, 960, 680);
        var dashboard = frame.Content;
        var manageApps = Descendants<Wpf.Ui.Controls.Button>(frame).Single(button => button.Content?.ToString() == "Manage Apps");
        manageApps.RaiseEvent(new RoutedEventArgs(System.Windows.Controls.Primitives.ButtonBase.ClickEvent));
        Layout(root, 960, 680);
        check(frame.Content is MonitoredAppsPage && navigation.SelectedItem == window.FindName("NavApps"),
            "dashboard shortcuts update both the native sidebar selection and the visible page");
        navigation.Navigate("0");
        Layout(root, 960, 680);
        check(ReferenceEquals(dashboard, frame.Content), "returning to a page reuses its existing view and state");
        var appsItem = (Wpf.Ui.Controls.NavigationViewItem)window.FindName("NavApps");
        var badge = (FrameworkElement)appsItem.InfoBadge!;
        check(badge.TranslatePoint(new Point(), appsItem).X > appsItem.ActualWidth / 2,
            "app count badge remains on the right side of the open navigation item");
        var circle = (System.Windows.Shapes.Ellipse)((Control)badge).Template.FindName("BadgeCircle", badge);
        check(circle.ActualWidth == 16 && circle.ActualHeight == 16,
            "running-app badge is a true 16 by 16 circle");
        check(circle.Fill is SolidColorBrush badgeFill && badgeFill.Color == Color.FromRgb(220, 38, 38)
            && ((Wpf.Ui.Controls.InfoBadge)badge).Foreground is SolidColorBrush badgeText && badgeText.Color == Colors.White,
            "running-app badge has a red background and white number");
        var applyStates = typeof(AppState).GetMethod("ApplyStates", BindingFlags.NonPublic | BindingFlags.Instance)!;
        applyStates.Invoke(windowState, new object[] { new List<AppRunningState>
        {
            new() { Name = "Example Editor", Path = @"C:\Preview\editor.exe", IsRunning = false },
        } });
        Layout(root, 960, 680);
        check(windowState.MonitoredApps.Count == 1 && badge.Visibility == Visibility.Collapsed,
            "configured but stopped apps leave no sidebar badge");
        applyStates.Invoke(windowState, new object[] { new List<AppRunningState>
        {
            new() { Name = "Example Editor", Path = @"C:\Preview\editor.exe", IsRunning = true },
            new() { Name = "Example Browser", Path = @"C:\Preview\browser.exe", IsRunning = true },
        } });
        Layout(root, 960, 680);
        check(badge.Visibility == Visibility.Visible && ((Wpf.Ui.Controls.InfoBadge)badge).Value == "2",
            "badge reappears and counts running apps rather than configured entries");
        applyStates.Invoke(windowState, new object[] { Snapshot().RunningStates.ToList() });
        Layout(root, 960, 680);
        check(((Wpf.Ui.Controls.InfoBadge)badge).Value == "1" && circle.ActualWidth == circle.ActualHeight,
            "badge returns to one running app without changing its circular shape");
        check(activityBar.TransformToAncestor(root).Transform(new Point()).X < 220,
            "countdown is attached to the sidebar rather than the content canvas");
        check(timer.Foreground is SolidColorBrush timerBrush && timerBrush.Color == ((SolidColorBrush)application.Resources["TextFillColorPrimaryBrush"]).Color,
            "countdown uses neutral primary text like the macOS sidebar");
        var chevron = (Wpf.Ui.Controls.SymbolIcon)window.FindName("ActivityQueueChevron");
        var queueButton = (Button)window.FindName("ActivityQueueButton");
        double chevronRight = chevron.TranslatePoint(new Point(chevron.ActualWidth, 0), queueButton).X;
        check(Math.Abs(queueButton.ActualWidth - chevronRight - 13) < 2 && chevron.ActualWidth >= 10
            && Math.Abs(queueButton.ActualWidth - activityBar.ActualWidth) < 2,
            "queue chevron is a complete icon at the right edge of the card");
        var cardSurface = (Border)queueButton.Template.FindName("CardSurface", queueButton);
        check(Math.Abs(cardSurface.ActualWidth - activityBar.ActualWidth) < 1
            && Math.Abs(cardSurface.ActualHeight - activityBar.ActualHeight) < 1,
            "the button surface and hit target cover the whole sidebar card");
        check(frame.Content is DashboardPage && Descendants<TextBlock>(frame).Any(text => text.Text == "Dashboard"),
            "dashboard content is visible on first launch without navigating");
        check(timer.Text == "00:50" && timer.Visibility == Visibility.Visible, "shared timer binds to the live view model");
        check(frame.BorderThickness == new Thickness(0) && frame.FocusVisualStyle == null && !frame.IsTabStop,
            "page host has no stock tab frame or pane-sized focus target");
        var pagePeer = UIElementAutomationPeer.CreatePeerForElement(frame);
        check((pagePeer.GetChildren()?.Count ?? 0) > 0,
            "visible page controls remain exposed to accessibility tools");
        foreach (int page in new[] { 0, 1, 2, 3, 4 })
        {
            navigation.Navigate(page.ToString(CultureInfo.InvariantCulture));
            Layout(root, 960, 680);
            int timerCount = Descendants<TextBlock>(root).Count(text => Regex.IsMatch(text.Text, @"^\d{2,}:\d{2}$"));
            check(timerCount == 1, $"page {page} has exactly one visible countdown");
            if (page == 0) SavePreview(root, previewDirectory, "dashboard-countdown.png", 960, 680);
            if (page == 2) SavePreview(root, previewDirectory, "rules-countdown.png", 960, 680);
        }
        VerifyQueuePopup(check, previewDirectory);
        var urgent = Snapshot();
        urgent.RunningStates[0].TimeRemaining = 5;
        window.DataContext = urgent;
        navigation.Navigate("0");
        Layout(root, 840, 580);
        check(timer.Text == "00:05", "urgent state updates the existing indicator without an extra alert");
        check(queueButton.Background is SolidColorBrush imminentBackground && imminentBackground.Color.A <= 10,
            "imminent countdown retains the quiet neutral card background");
        SavePreview(root, previewDirectory, "dashboard-imminent-small.png", 840, 580);
        SetField(urgent, "_isMonitoring", false);
        window.DataContext = null;
        window.DataContext = urgent;
        Layout(root, 960, 680);
        check(timer.Visibility == Visibility.Collapsed, "paused view hides the time readout");
        SavePreview(root, previewDirectory, "dashboard-paused.png", 960, 680);
        // MainWindow normally hides on close; detach the snapshot instead of starting its app lifecycle.
        window.Content = null;
        MonitoringLifecycleChecks.Run(application, check);
#if STORE
        StoreChecks.Run(application, check, previewDirectory);
#endif
        application.Shutdown();
    }

    private static void VerifyClosingQueue(Action<bool, string> check)
    {
        var state = Snapshot();
        for (int index = 1; index < 30; index++)
            state.RunningStates.Add(new AppRunningState
            {
                Name = $"Sample Application {index + 1}", Path = $@"C:\Preview\sample{index}.exe",
                IsRunning = true, TimeRemaining = 50 + index
            });
        var view = state.ClosingQueue;
        check(state.HasClosingQueue && state.ClosingQueueCount == 30 && view.Cast<AppRunningState>().Count() == 30,
            "closing queue includes all running monitored apps without a display cap");
        check(state.ClosingQueueAdditionalText == "+29 more", "compact queue reports all additional apps");
        state.RunningStates[10].TimeRemaining = 2;
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        check(ReferenceEquals(view.Cast<AppRunningState>().First(), state.RunningStates[10]),
            "closing queue reorders live when an app deadline changes");
        SetField(state, "_isCountdownActive", false);
        check(state.HasClosingQueue && ReferenceEquals(view, state.ClosingQueue),
            "input reset retains the same queue view for inspection and scroll stability");
        state.RunningStates[10].IsRunning = false;
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        check(view.Cast<AppRunningState>().Count() == 29 && state.RunningStates.Count == 30,
            "live queue filtering removes stopped apps without filtering the main collection");
        SetField(state, "_isMonitoring", false);
        check(!state.HasClosingQueue && state.ClosingQueueCount == 0, "pausing makes the closing queue unavailable");
    }

    private static void VerifyQueuePopup(Action<bool, string> check, string? previewDirectory)
    {
        var state = Snapshot();
        var window = new MainWindow(state);
        var applyStates = typeof(AppState).GetMethod("ApplyStates", BindingFlags.NonPublic | BindingFlags.Instance)!;
        var samples = Enumerable.Range(0, 30).Select(index => new AppRunningState
        {
            Name = index == 0 ? "Example Browser — Research, Reading and Work Profiles" : $"Sample Application {index + 1}",
            Path = $@"C:\Preview\queue{index}.exe", IsRunning = true,
            IsCountingDown = true, TimeRemaining = 20 + index
        }).ToList();
        applyStates.Invoke(state, new object[] { samples });
        window.Show();
        var root = (FrameworkElement)window.Content;
        Layout(root, 960, 680);
        var button = (Button)window.FindName("ActivityQueueButton");
        var popup = (Popup)window.FindName("ClosingQueuePopup");
        var scroll = (ScrollViewer)window.FindName("ClosingQueueScroll");
        var items = (ItemsControl)window.FindName("ClosingQueueItems");
        var next = (TextBlock)window.FindName("NextClosingApp");
        check(next.TextTrimming == TextTrimming.CharacterEllipsis && next.TextWrapping == TextWrapping.NoWrap
            && next.ToolTip?.ToString() == samples[0].Name, "long names stay on one line with the full name on hover");
        check(button.IsEnabled && popup.Placement == PlacementMode.Right && !popup.StaysOpen && scroll.MaxHeight == 320,
            "closing queue has a bounded native popup with outside-click dismissal");
        button.RaiseEvent(new RoutedEventArgs(ButtonBase.ClickEvent));
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        popup.Child.UpdateLayout();
        check(popup.IsOpen && items.Items.Count == 30, "clicking the sidebar opens the complete queue");
        var surface = (Border)window.FindName("ClosingQueueSurface");
        check(surface.Background is SolidColorBrush surfaceBrush && surfaceBrush.Color.A == 255 && surfaceBrush.Opacity == 1,
            "queue popup has an opaque surface even outside the main window");
        scroll.ScrollToEnd();
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        check(scroll.VerticalOffset > 0, "large closing queues scroll to the last apps");
        double offset = scroll.VerticalOffset;
        SetField(state, "_isCountdownActive", false);
        applyStates.Invoke(state, new object[] { samples.Select(app => new AppRunningState
        {
            Name = app.Name, Path = app.Path, IsRunning = true, TimeRemaining = 60
        }).ToList() });
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        check(popup.IsOpen && scroll.VerticalOffset >= offset - 1,
            "input reset keeps the popup open and preserves its scroll position");
        SavePreview((FrameworkElement)popup.Child, previewDirectory, "closing-queue-many-apps.png",
            (int)Math.Ceiling(((FrameworkElement)popup.Child).ActualWidth), (int)Math.Ceiling(((FrameworkElement)popup.Child).ActualHeight));
        SetField(state, "_isMonitoring", false);
        applyStates.Invoke(state, new object[] { samples });
        Dispatcher.CurrentDispatcher.Invoke(() => { }, DispatcherPriority.Background);
        check(!popup.IsOpen && !button.IsEnabled, "pausing closes and disables the active queue");
        window.Content = null;
        window.Hide();
    }

    private static void Layout(FrameworkElement root, int width, int height)
    {
        root.Dispatcher.Invoke(() => { }, System.Windows.Threading.DispatcherPriority.Background);
        root.Measure(new Size(width, height));
        root.Arrange(new Rect(0, 0, width, height));
        root.UpdateLayout();
    }

    private static IEnumerable<T> Descendants<T>(DependencyObject root) where T : DependencyObject
    {
        for (int i = 0; i < VisualTreeHelper.GetChildrenCount(root); i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is T match) yield return match;
            foreach (var descendant in Descendants<T>(child)) yield return descendant;
        }
    }

    private static void SavePreview(FrameworkElement root, string? directory, string name, int width, int height)
    {
        if (directory == null) return;
        Directory.CreateDirectory(directory);
        var bitmap = new RenderTargetBitmap(width, height, 96, 96, PixelFormats.Pbgra32);
        var backdrop = new DrawingVisual();
        using (var drawing = backdrop.RenderOpen())
            drawing.DrawRectangle(new SolidColorBrush(Color.FromRgb(30, 32, 35)), null, new Rect(0, 0, width, height));
        bitmap.Render(backdrop);
        bitmap.Render(root);
        var encoder = new PngBitmapEncoder();
        encoder.Frames.Add(BitmapFrame.Create(bitmap));
        using var stream = File.Create(System.IO.Path.Combine(directory, name));
        encoder.Save(stream);
    }
}
