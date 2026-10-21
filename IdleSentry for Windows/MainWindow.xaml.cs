using System;
using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Input;
using System.Windows.Media.Animation;
using Wpf.Ui.Controls;
using Button = System.Windows.Controls.Button;

namespace IdleSentry
{
    public partial class MainWindow : FluentWindow
    {
        private readonly AppState _appState;

        public MainWindow(AppState appState)
        {
            _appState = appState;
            DataContext = _appState;
            InitializeComponent();
            SidebarNavigation.SetServiceProvider(new NavigationPageProvider(this));
            SidebarNavigation.ApplyTemplate();
            SidebarNavigation.Navigate("0");
            Loaded += (_, _) => UpdateSelectionIndicator(animate: false);
            _appState.PropertyChanged += (_, args) =>
            {
                if (args.PropertyName == nameof(AppState.HasClosingQueue) && !_appState.HasClosingQueue)
                    ClosingQueuePopup.IsOpen = false;
            };
            IsVisibleChanged += (_, _) => { if (!IsVisible) ClosingQueuePopup.IsOpen = false; };
        }

        private void SidebarNavigation_SelectionChanged(NavigationView sender, RoutedEventArgs e)
        {
            UpdateSelectionIndicator(animate: IsLoaded);
        }

        private void NavigationRegion_SizeChanged(object sender, SizeChangedEventArgs e) =>
            UpdateSelectionIndicator(animate: false);

        private void UpdateSelectionIndicator(bool animate)
        {
            if (SidebarNavigation?.SelectedItem is not NavigationViewItem item
                || SidebarNavigation.Template.FindName("NavigationRegion", SidebarNavigation) is not FrameworkElement region
                || SidebarNavigation.Template.FindName("SelectionIndicator", SidebarNavigation) is not System.Windows.Shapes.Rectangle indicator
                || item.ActualHeight == 0 || !item.IsDescendantOf(region)) return;

            var translation = (TranslateTransform)indicator.RenderTransform;
            if (translation.IsFrozen)
            {
                translation = translation.Clone();
                indicator.RenderTransform = translation;
            }
            double destination = item.TranslatePoint(new Point(0, item.ActualHeight / 2), region).Y - indicator.Height / 2;
            double current = translation.Y;
            bool wasVisible = indicator.Visibility == Visibility.Visible;
            translation.BeginAnimation(TranslateTransform.YProperty, null);
            translation.Y = destination;
            indicator.Visibility = Visibility.Visible;

            if (animate && wasVisible && SystemParameters.ClientAreaAnimation)
            {
                translation.BeginAnimation(TranslateTransform.YProperty,
                    new DoubleAnimation(current, destination, TimeSpan.FromMilliseconds(180))
                    {
                        EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                        FillBehavior = FillBehavior.Stop
                    });
            }
        }

        private sealed class NavigationPageProvider(MainWindow window) : IServiceProvider
        {
            public object GetService(Type serviceType)
            {
                var page = (FrameworkElement)window.FindResource(serviceType.Name);
                page.SetBinding(DataContextProperty, new System.Windows.Data.Binding(nameof(DataContext)) { Source = window });
                return page;
            }
        }

        private void ActivityQueue_Click(object sender, RoutedEventArgs e)
        {
            if (_appState.HasClosingQueue) ClosingQueuePopup.IsOpen = true;
        }

        private void ClosingQueuePopup_Opened(object sender, EventArgs e) => ClosingQueueScroll.Focus();

        private void ClosingQueue_PreviewKeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key != Key.Escape) return;
            ClosingQueuePopup.IsOpen = false;
            ActivityQueueButton.Focus();
            e.Handled = true;
        }

        // Choose apps by name; manual executable browsing lives inside the picker.
        private void AddApp_Click(object sender, RoutedEventArgs e)
        {
            var dialog = new AppPickerWindow(_appState.MonitoredApps.Select(app => app.Path)) { Owner = this };
            if (dialog.ShowDialog() == true)
            {
                foreach (var app in dialog.SelectedApps)
                    _appState.AddApp(app.Path, app.Name);
            }
        }

        // Remove application from monitored list
        private void DeleteApp_Click(object sender, RoutedEventArgs e)
        {
            if (sender is Button btn && btn.Tag is string path)
            {
                _appState.RemoveApp(path);
            }
        }

        // Quick navigation buttons from within tab pages
        private void NavigateTab_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is string tagStr && int.TryParse(tagStr, out int index))
            {
                SidebarNavigation.Navigate(index.ToString(System.Globalization.CultureInfo.InvariantCulture));
            }
        }

        // Inactivity timeout quick preset button handler
        private void SetPresetTimeout_Click(object sender, RoutedEventArgs e)
        {
            if (sender is FrameworkElement elem && elem.Tag is string tagStr && double.TryParse(tagStr, out double seconds))
            {
                _appState.GlobalTimeout = seconds;
            }
        }

        public void ShowUpdates() => SidebarNavigation.Navigate("4");

        private async void CheckUpdates_Click(object sender, RoutedEventArgs e) => await UpdateService.Shared.CheckAsync();
        private async void DownloadUpdate_Click(object sender, RoutedEventArgs e) => await UpdateService.Shared.DownloadAsync();
        private void InstallUpdate_Click(object sender, RoutedEventArgs e)
        {
            if (System.Windows.MessageBox.Show("IdleSentry will close, install the update and reopen. Your monitored apps and settings will be kept.",
                "Install update?", System.Windows.MessageBoxButton.OKCancel, MessageBoxImage.Information) == System.Windows.MessageBoxResult.OK)
                UpdateService.Shared.InstallAndRestart(() => ((App)Application.Current).SafeShutdown());
        }

        // Open local settings storage folder in Windows Explorer
        private void OpenSettingsFolder_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                string folder = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "IdleSentry");
                if (!Directory.Exists(folder))
                {
                    Directory.CreateDirectory(folder);
                }
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = folder,
                    UseShellExecute = true
                });
            }
            catch
            {
                // Fallback gracefully if explorer cannot be launched
            }
        }

        // Open official developer website in default web browser
        private void OpenWebsite_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "https://zennoris.com",
                    UseShellExecute = true
                });
            }
            catch
            {
                // Fallback gracefully if web browser cannot be launched
            }
        }

        // Override window closing to hide the window instead of exiting
        protected override void OnClosing(CancelEventArgs e)
        {
            if (Application.Current is App app && app.IsShuttingDown)
            {
                base.OnClosing(e);
            }
            else
            {
                e.Cancel = true;
                Hide();
            }
        }
    }
}
