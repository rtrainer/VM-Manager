using System.Windows;

namespace VmManager.App;

public partial class SettingsWindow : Window {
    private readonly MainWindow _mainWindow;
    private bool _saving;

    public SettingsWindow(MainWindow mainWindow) {
        _mainWindow = mainWindow;
        InitializeComponent();
        StartAtLoginToggle.IsChecked = mainWindow.StartAtLogin;
        StartMinimizedToggle.IsChecked = mainWindow.StartMinimized;
        AutoUpdateToggle.IsChecked = mainWindow.AutoUpdateEnabled;
        ConfigureAutoUpdateToggle();
        _mainWindow.DataChanged += MainWindow_DataChanged;
        Closed += SettingsWindow_Closed;
        RefreshUpdateControls();
        VersionTextBlock.Text = $"Version {ApplicationVersion.Display}";
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) {
        DialogPlacement.CenterOverOwner(this);
    }

    private async void StartAtLoginToggle_Click(object sender, RoutedEventArgs e) {
        if (_saving) {
            return;
        }

        await SaveSettingAsync(
            StartAtLoginToggle,
            () => _mainWindow.StartAtLogin,
            enabled => _mainWindow.SetStartAtLoginAsync(enabled));
    }

    private async void StartMinimizedToggle_Click(object sender, RoutedEventArgs e) {
        if (_saving) {
            return;
        }

        await SaveSettingAsync(
            StartMinimizedToggle,
            () => _mainWindow.StartMinimized,
            enabled => _mainWindow.SetStartMinimizedAsync(enabled));
    }

    private async void AutoUpdateToggle_Click(object sender, RoutedEventArgs e) {
        if (_saving) {
            return;
        }

        await SaveSettingAsync(
            AutoUpdateToggle,
            () => _mainWindow.AutoUpdateEnabled,
            enabled => _mainWindow.SetAutoUpdateEnabledAsync(enabled));
    }

    private async void CheckForUpdatesButton_Click(object sender, RoutedEventArgs e) {
        if (_saving) {
            return;
        }

        _saving = true;
        CheckForUpdatesButton.IsEnabled = false;
        AutoUpdateToggle.IsEnabled = false;
        try {
            await _mainWindow.CheckForUpdatesNowAsync();
        } finally {
            _saving = false;
            RefreshUpdateControls();
        }
    }

    private async void InstallUpdateButton_Click(object sender, RoutedEventArgs e) {
        if (_saving) {
            return;
        }

        _saving = true;
        CheckForUpdatesButton.IsEnabled = false;
        InstallUpdateButton.IsEnabled = false;
        AutoUpdateToggle.IsEnabled = false;
        try {
            await _mainWindow.InstallAvailableUpdateAsync();
        } finally {
            _saving = false;
            RefreshUpdateControls();
        }
    }

    private async Task SaveSettingAsync(
        System.Windows.Controls.CheckBox toggle,
        Func<bool> currentValue,
        Func<bool, Task> save) {
        _saving = true;
        toggle.IsEnabled = false;
        CheckForUpdatesButton.IsEnabled = false;
        try {
            await save(toggle.IsChecked == true);
        } catch (Exception exception) {
            toggle.IsChecked = currentValue();
            MessageDialog.Show(_mainWindow, exception.Message, "Unable to save settings", MessageBoxButton.OK, MessageBoxImage.Error);
        } finally {
            _saving = false;
            RefreshUpdateControls();
        }
    }

    private void ConfigureAutoUpdateToggle() {
        if (_mainWindow.AutoUpdateAvailable) {
            return;
        }

        AutoUpdateToggle.IsEnabled = false;
        AutoUpdateToggle.IsChecked = false;
        CheckForUpdatesButton.IsEnabled = false;
        AutoUpdateDescriptionTextBlock.Text = "Update checks require VM Manager to be installed.";
    }

    private void MainWindow_DataChanged(object? sender, EventArgs e) {
        if (!Dispatcher.CheckAccess()) {
            Dispatcher.BeginInvoke(RefreshUpdateControls);
            return;
        }

        RefreshUpdateControls();
    }

    private void SettingsWindow_Closed(object? sender, EventArgs e) {
        _mainWindow.DataChanged -= MainWindow_DataChanged;
    }

    private void RefreshUpdateControls() {
        if (!_saving) {
            AutoUpdateToggle.IsEnabled = _mainWindow.AutoUpdateAvailable;
            CheckForUpdatesButton.IsEnabled = _mainWindow.AutoUpdateAvailable;
        }

        bool hasAvailableUpdate = _mainWindow.HasAvailableUpdate;
        AvailableUpdatePanel.Visibility = hasAvailableUpdate ? Visibility.Visible : Visibility.Collapsed;
        InstallUpdateButton.IsEnabled = !_saving && _mainWindow.AutoUpdateAvailable && hasAvailableUpdate;

        string? version = _mainWindow.AvailableUpdateVersion;
        UpdateAvailableTextBlock.Text = string.IsNullOrWhiteSpace(version)
            ? "An update is available."
            : $"Update {version} is available.";
    }

}
