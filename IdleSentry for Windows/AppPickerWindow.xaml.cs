using System.ComponentModel;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Data;
using Wpf.Ui.Controls;

namespace IdleSentry;

public partial class AppPickerWindow : FluentWindow
{
    private readonly HashSet<string> _monitoredPaths;
    private readonly CancellationTokenSource _lifetime = new();
    private List<AppChoice> _apps = new();
    private ICollectionView? _view;
    private bool _isLoading;
    internal IReadOnlyList<AppChoice> SelectedApps { get; private set; } = Array.Empty<AppChoice>();

    public AppPickerWindow(IEnumerable<string> monitoredPaths)
    {
        _monitoredPaths = new HashSet<string>(monitoredPaths, StringComparer.OrdinalIgnoreCase);
        InitializeComponent();
    }

    private async void Window_Loaded(object sender, RoutedEventArgs e)
    {
        SearchBox.Focus();
        await LoadAppsAsync();
    }

    private async Task LoadAppsAsync()
    {
        if (_isLoading) return;
        _isLoading = true;
        RefreshButton.IsEnabled = false;
        BrowseButton.IsEnabled = false;
        AppList.IsEnabled = false;
        AddButton.IsEnabled = false;
        LoadingBar.Visibility = Visibility.Visible;
        ResultsText.Text = "Finding installed and running apps…";
        EmptyText.Visibility = Visibility.Collapsed;
        try
        {
            var selectedApps = _apps.Where(app => app.IsSelected).ToList();
            var selectedPaths = selectedApps.Select(app => app.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            var discovered = await AppDiscovery.DiscoverAsync(_lifetime.Token);
            if (_lifetime.IsCancellationRequested) return;
            // Keep manually chosen portable apps selected even if discovery cannot find them.
            var foundPaths = discovered.Select(app => app.Path).ToHashSet(StringComparer.OrdinalIgnoreCase);
            discovered.AddRange(selectedApps.Where(app => !foundPaths.Contains(app.Path) && File.Exists(app.Path)));
            foreach (var app in _apps) app.PropertyChanged -= Selection_Changed;
            _apps = discovered.OrderByDescending(app => app.IsRunning)
                .ThenBy(app => app.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
            foreach (var app in _apps)
            {
                app.AlreadyMonitored = _monitoredPaths.Contains(app.Path);
                app.IsSelected = app.CanSelect && selectedPaths.Contains(app.Path);
                app.PropertyChanged += Selection_Changed;
            }
            _view = CollectionViewSource.GetDefaultView(_apps);
            _view.Filter = item => item is AppChoice app && MatchesSearch(app, SearchBox.Text);
            AppList.ItemsSource = _view;
            UpdateResults();
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine($"App discovery failed: {ex.Message}");
            ResultsText.Text = "Couldn't load the app list. Try Refresh or Browse.";
        }
        finally
        {
            _isLoading = false;
            if (!_lifetime.IsCancellationRequested)
            {
                RefreshButton.IsEnabled = true;
                BrowseButton.IsEnabled = true;
                AppList.IsEnabled = true;
                LoadingBar.Visibility = Visibility.Collapsed;
                UpdateSelection();
            }
        }
    }

    internal static bool MatchesSearch(AppChoice app, string search) =>
        app.Name.Contains(search.Trim(), StringComparison.CurrentCultureIgnoreCase);

    private void Search_TextChanged(object sender, TextChangedEventArgs e)
    {
        if (_view == null) return;
        _view.Refresh();
        UpdateResults();
    }

    private void UpdateResults()
    {
        int count = _view?.Cast<AppChoice>().Count() ?? 0;
        ResultsText.Text = $"{count} {(count == 1 ? "app" : "apps")} · Running apps first";
        EmptyText.Visibility = count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void Selection_Changed(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(AppChoice.IsSelected)) UpdateSelection();
    }

    private void UpdateSelection()
    {
        int selected = _apps.Count(app => app.IsSelected && app.CanSelect);
        AddButton.IsEnabled = !_isLoading && selected > 0;
        AddButton.Content = selected == 0 ? "Add apps" : $"Add {selected} {(selected == 1 ? "app" : "apps")}";
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadAppsAsync();

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Applications (*.exe)|*.exe", Title = "Choose an application",
            InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
            Multiselect = true
        };
        if (dialog.ShowDialog(this) != true) return;
        string? notice = null;
        foreach (var file in dialog.FileNames)
        {
            string? path = AppDiscovery.NormalizeAppPath(file);
            if (path == null) { notice = "A file could not be selected. Choose the app itself rather than a shared launcher."; continue; }
            if (_monitoredPaths.Contains(path)) { notice = "An app you chose is already in your monitored list."; continue; }
            var app = _apps.FirstOrDefault(candidate => string.Equals(candidate.Path, path, StringComparison.OrdinalIgnoreCase));
            if (app == null)
            {
                app = new AppChoice { Name = AppDiscovery.GetDisplayName(path), Path = path, Icon = AppState.GetAppIcon(path) };
                app.PropertyChanged += Selection_Changed;
                _apps.Add(app);
            }
            app.IsSelected = true;
        }
        // Clear the search so manually chosen apps are visible for review before adding.
        SearchBox.Text = string.Empty;
        _view ??= CollectionViewSource.GetDefaultView(_apps);
        _view.Filter = item => item is AppChoice app && MatchesSearch(app, SearchBox.Text);
        AppList.ItemsSource = _view;
        _view.Refresh();
        UpdateResults();
        if (notice != null) ResultsText.Text = notice;
        UpdateSelection();
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        var selected = _apps.Where(app => app.IsSelected && app.CanSelect).ToList();
        if (selected.Count == 0) return;
        if (selected.Any(app => !File.Exists(app.Path)))
        {
            ResultsText.Text = "An app is no longer available. Refresh the list and select it again.";
            return;
        }
        SelectedApps = selected;
        DialogResult = true;
    }

    private void Window_Closed(object? sender, EventArgs e)
    {
        _lifetime.Cancel();
        foreach (var app in _apps) app.PropertyChanged -= Selection_Changed;
    }
}
