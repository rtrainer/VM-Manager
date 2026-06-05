using System.Reflection;
using System.Windows;

namespace VmManager.App;

public partial class SettingsWindow : Window {
    private readonly MainWindow _mainWindow;
    private bool _saving;

    public SettingsWindow(MainWindow mainWindow) {
        _mainWindow = mainWindow;
        InitializeComponent();
        StartMinimizedToggle.IsChecked = mainWindow.StartMinimized;
        VersionTextBlock.Text = $"Version {GetApplicationVersion()}";
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) {
        DialogPlacement.CenterOverOwner(this);
    }

    private async void StartMinimizedToggle_Click(object sender, RoutedEventArgs e) {
        if (_saving) {
            return;
        }

        _saving = true;
        StartMinimizedToggle.IsEnabled = false;
        try {
            await _mainWindow.SetStartMinimizedAsync(StartMinimizedToggle.IsChecked == true);
        } catch (Exception exception) {
            StartMinimizedToggle.IsChecked = _mainWindow.StartMinimized;
            MessageDialog.Show(_mainWindow, exception.Message, "Unable to save settings", MessageBoxButton.OK, MessageBoxImage.Error);
        } finally {
            StartMinimizedToggle.IsEnabled = true;
            _saving = false;
        }
    }

    private static string GetApplicationVersion() {
        Assembly assembly = typeof(App).Assembly;
        return assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
            ?? assembly.GetName().Version?.ToString(3)
            ?? "Unknown";
    }
}
