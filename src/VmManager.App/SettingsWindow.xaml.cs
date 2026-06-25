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

    private async Task SaveSettingAsync(
        System.Windows.Controls.CheckBox toggle,
        Func<bool> currentValue,
        Func<bool, Task> save) {
        _saving = true;
        toggle.IsEnabled = false;
        try {
            await save(toggle.IsChecked == true);
        } catch (Exception exception) {
            toggle.IsChecked = currentValue();
            MessageDialog.Show(_mainWindow, exception.Message, "Unable to save settings", MessageBoxButton.OK, MessageBoxImage.Error);
        } finally {
            toggle.IsEnabled = toggle != AutoUpdateToggle || _mainWindow.AutoUpdateAvailable;
            _saving = false;
        }
    }

    private void ConfigureAutoUpdateToggle() {
        if (_mainWindow.AutoUpdateAvailable) {
            return;
        }

        AutoUpdateToggle.IsEnabled = false;
        AutoUpdateToggle.IsChecked = false;
        AutoUpdateDescriptionTextBlock.Text = "Automatic updates require VM Manager to be installed.";
    }

}
