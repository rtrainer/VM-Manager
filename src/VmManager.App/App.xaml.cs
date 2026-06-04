using System.Diagnostics;
using System.Windows;
using System.Windows.Threading;

using VmManager.Core.Models;
using VmManager.Core.Services;
using VmManager.HyperV;

using MessageBox = System.Windows.MessageBox;
using Path = System.IO.Path;

namespace VmManager.App;

public partial class App : System.Windows.Application {
    private const string MutexName = "Local\\LittleBitsSoftware.VmManager";
    private static readonly TimeSpan MinimumSplashDuration = TimeSpan.FromSeconds(1.5);
    private Mutex? _singleInstanceMutex;
    private bool _ownsSingleInstanceMutex;
    private TrayIconService? _trayIcon;
    private SplashWindow? _splashWindow;

    protected override async void OnStartup(StartupEventArgs e) {
        base.OnStartup(e);
        AppLog.Write("Application starting.");

        try {
            _singleInstanceMutex = new Mutex(true, MutexName, out var isFirstInstance);
            _ownsSingleInstanceMutex = isFirstInstance;
            if (!isFirstInstance) {
                MessageBox.Show("VM Manager is already running.", "VM Manager");
                Shutdown();
                return;
            }

            var splashStopwatch = Stopwatch.StartNew();
            _splashWindow = new SplashWindow();
            _splashWindow.Show();
            AppLog.Write("Splash shown.");
            await Dispatcher.InvokeAsync(() => { }, DispatcherPriority.ApplicationIdle);

            _splashWindow.SetStatus("Loading settings and VM groups...");
            var dataDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "VmManager");

            var catalog = new VmGroupCatalog(new JsonVmGroupRepository(Path.Combine(dataDirectory, "groups.json")));
            await catalog.InitializeAsync();

            var settingsRepository = new JsonAppSettingsRepository(Path.Combine(dataDirectory, "settings.json"));
            AppSettings settings = await settingsRepository.LoadAsync();

            _splashWindow.SetStatus("Preparing the system tray...");
            var mainWindow = new MainWindow(new PowerShellHyperVService(), catalog, settingsRepository, settings);
            MainWindow = mainWindow;
            _trayIcon = new TrayIconService(mainWindow);

            _splashWindow.SetStatus("Discovering local Hyper-V virtual machines...");
            await mainWindow.RefreshAsync();

            TimeSpan remainingSplashTime = MinimumSplashDuration - splashStopwatch.Elapsed;
            if (remainingSplashTime > TimeSpan.Zero) {
                await Task.Delay(remainingSplashTime);
            }

            AppLog.Write($"Splash closing after {splashStopwatch.ElapsedMilliseconds} ms.");
            _splashWindow.Close();
            _splashWindow = null;

            if (!settings.StartMinimized) {
                mainWindow.Show();
                AppLog.Write("Dashboard shown.");
            } else {
                AppLog.Write("Started minimized to tray.");
            }
        } catch (Exception exception) {
            AppLog.Write(exception);
            _splashWindow?.Close();
            _splashWindow = null;
            MessageBox.Show(exception.Message, "VM Manager could not start", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown();
        }
    }

    protected override void OnExit(ExitEventArgs e) {
        AppLog.Write("Application exiting.");
        _splashWindow?.Close();
        _trayIcon?.Dispose();
        if (_singleInstanceMutex is not null) {
            if (_ownsSingleInstanceMutex) {
                _singleInstanceMutex.ReleaseMutex();
            }

            _singleInstanceMutex.Dispose();
        }

        base.OnExit(e);
    }
}
