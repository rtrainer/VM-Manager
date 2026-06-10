using System.ComponentModel;
using System.Reflection;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using Velopack;

using VmManager.Core.Models;
using VmManager.Core.Services;

using WpfMenuItem = System.Windows.Controls.MenuItem;

namespace VmManager.App;

public partial class MainWindow : Window {
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(6);
    private readonly IHyperVService _hyperVService;
    private readonly VmGroupCatalog _groupCatalog;
    private readonly IAppSettingsRepository _settingsRepository;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _updateTimer;
    private AppSettings _settings;
    private IReadOnlyList<VirtualMachine> _virtualMachines = [];
    private UpdateInfo? _availableUpdate;
    private VelopackAsset? _readyUpdate;
    private string? _lastNotifiedUpdateVersion;
    private SettingsWindow? _settingsWindow;
    private bool _allowClose;
    private bool _operationInProgress;
    private bool _updateCheckInProgress;

    public MainWindow(
        IHyperVService hyperVService,
        VmGroupCatalog groupCatalog,
        IAppSettingsRepository settingsRepository,
        AppSettings settings) {
        _hyperVService = hyperVService;
        _groupCatalog = groupCatalog;
        _settingsRepository = settingsRepository;
        _settings = settings;

        InitializeComponent();
        RebuildGroupSelectors();

        _refreshTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(5) };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync(silent: true);
        _refreshTimer.Start();

        _updateTimer = new DispatcherTimer { Interval = UpdateCheckInterval };
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync();
    }

    public event EventHandler? DataChanged;

    public event EventHandler<UpdateAvailableEventArgs>? UpdateAvailable;

    public IReadOnlyList<VirtualMachine> VirtualMachines => _virtualMachines;

    public IReadOnlyList<VmGroup> Groups => _groupCatalog.Groups;

    public bool StartMinimized => _settings.StartMinimized;

    public bool AutoUpdateEnabled => _settings.AutoUpdateEnabled;

    public bool AutoUpdateAvailable => CreateUpdateManager() is not null;

    public bool HasAvailableUpdate => _availableUpdate is not null || _readyUpdate is not null;

    public string? AvailableUpdateVersion =>
        _readyUpdate?.Version.ToString() ?? _availableUpdate?.TargetFullRelease.Version.ToString();

    public void ShowFromTray() {
        if (!Dispatcher.CheckAccess()) {
            Dispatcher.BeginInvoke(ShowFromTray);
            return;
        }

        if (!IsVisible) {
            Show();
        }

        if (WindowState == WindowState.Minimized) {
            WindowState = WindowState.Normal;
        }

        Activate();
        Topmost = true;
        Topmost = false;
        Focus();
    }

    public void ExitApplication() {
        _allowClose = true;
        Close();
        System.Windows.Application.Current.Shutdown();
    }

    public void ShowSettings() {
        if (!Dispatcher.CheckAccess()) {
            Dispatcher.BeginInvoke(ShowSettings);
            return;
        }

        if (_settingsWindow is not null) {
            ShowFromTray();
            _settingsWindow.Activate();
            return;
        }

        ShowFromTray();
        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        _settingsWindow.Owner = this;

        _settingsWindow.Show();
        _settingsWindow.Activate();
    }

    public async Task RefreshAsync(bool silent = false) {
        if (_operationInProgress) {
            return;
        }

        try {
            if (!silent) {
                SetStatus("Refreshing virtual machines...");
            }

            _virtualMachines = await _hyperVService.GetVirtualMachinesAsync();
            ApplyGroupFilter();
            SetStatus($"{_virtualMachines.Count} virtual machine(s) found. Last refreshed {DateTime.Now:t}.");
            DataChanged?.Invoke(this, EventArgs.Empty);
        } catch (Exception exception) {
            SetStatus($"Unable to query Hyper-V: {exception.Message}");
        }
    }

    public Task StartVmAsync(Guid vmId) =>
        RunOperationAsync("Starting virtual machine...", () => _hyperVService.StartAsync(vmId));

    public Task ShutDownVmAsync(Guid vmId) =>
        RunOperationAsync("Shutting down virtual machine...", () => _hyperVService.ShutDownAsync(vmId));

    public Task StartGroupAsync(Guid groupId) =>
        RunGroupOperationAsync(groupId, "Starting group...", vm => vm.CanStart, _hyperVService.StartAsync);

    public Task ShutDownGroupAsync(Guid groupId) =>
        RunGroupOperationAsync(groupId, "Shutting down group...", vm => vm.CanStop, _hyperVService.ShutDownAsync);

    public async Task SetStartMinimizedAsync(bool startMinimized) {
        if (_settings.StartMinimized == startMinimized) {
            return;
        }

        AppSettings updatedSettings = _settings with { StartMinimized = startMinimized };
        await _settingsRepository.SaveAsync(updatedSettings);
        _settings = updatedSettings;
    }

    public async Task SetAutoUpdateEnabledAsync(bool autoUpdateEnabled) {
        if (!AutoUpdateAvailable && autoUpdateEnabled) {
            throw new InvalidOperationException("Automatic updates require an installed Velopack app and a configured update feed URL.");
        }

        if (_settings.AutoUpdateEnabled == autoUpdateEnabled) {
            return;
        }

        AppSettings updatedSettings = _settings with { AutoUpdateEnabled = autoUpdateEnabled };
        await _settingsRepository.SaveAsync(updatedSettings);
        _settings = updatedSettings;

        if (autoUpdateEnabled) {
            StartBackgroundUpdateChecks(checkNow: true);
        } else {
            StopBackgroundUpdateChecks();
        }
    }

    public void StartBackgroundUpdateChecks(bool checkNow) {
        if (!_settings.AutoUpdateEnabled || !AutoUpdateAvailable) {
            return;
        }

        if (!_updateTimer.IsEnabled) {
            _updateTimer.Start();
        }

        if (checkNow) {
            _ = CheckForUpdatesAsync();
        }
    }

    public async Task CheckForUpdatesAsync(bool silent = true) {
        if (!_settings.AutoUpdateEnabled || _updateCheckInProgress) {
            return;
        }

        UpdateManager? updateManager = CreateUpdateManager();
        if (updateManager is null) {
            if (!silent) {
                SetStatus("Automatic updates are not configured for this build.");
            }

            return;
        }

        _updateCheckInProgress = true;
        try {
            VelopackAsset? pendingUpdate = updateManager.UpdatePendingRestart;
            if (pendingUpdate is not null) {
                _readyUpdate = pendingUpdate;
                NotifyUpdateAvailable(pendingUpdate.Version.ToString(), isReadyToInstall: true);
                DataChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            if (!silent) {
                SetStatus("Checking for updates...");
            }

            UpdateInfo? update = await updateManager.CheckForUpdatesAsync();
            if (update is null) {
                _availableUpdate = null;
                _readyUpdate = null;
                _lastNotifiedUpdateVersion = null;
                DataChanged?.Invoke(this, EventArgs.Empty);
                if (!silent) {
                    SetStatus("VM Manager is up to date.");
                }

                return;
            }

            _availableUpdate = update;
            SetStatus($"Update {update.TargetFullRelease.Version} is available.");
            NotifyUpdateAvailable(update.TargetFullRelease.Version.ToString(), isReadyToInstall: false);
            DataChanged?.Invoke(this, EventArgs.Empty);
        } catch (Exception exception) {
            AppLog.Write(exception);
            SetStatus($"Unable to check for updates: {exception.Message}");
            if (!silent) {
                MessageDialog.Show(IsVisible ? this : null, exception.Message, "Unable to check for updates", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        } finally {
            _updateCheckInProgress = false;
        }
    }

    public async Task InstallAvailableUpdateAsync() {
        UpdateManager? updateManager = CreateUpdateManager();
        if (updateManager is null) {
            SetStatus("Automatic updates are not configured for this build.");
            return;
        }

        try {
            VelopackAsset? pendingUpdate = updateManager.UpdatePendingRestart;
            if (pendingUpdate is not null) {
                PromptToRestartForUpdate(updateManager, pendingUpdate);
                return;
            }

            UpdateInfo? update = _availableUpdate ?? await updateManager.CheckForUpdatesAsync();
            if (update is null) {
                _availableUpdate = null;
                DataChanged?.Invoke(this, EventArgs.Empty);
                SetStatus("VM Manager is up to date.");
                return;
            }

            SetStatus($"Downloading update {update.TargetFullRelease.Version}...");
            await updateManager.DownloadUpdatesAsync(update, progress =>
                Dispatcher.BeginInvoke(() => SetStatus($"Downloading update {update.TargetFullRelease.Version}... {progress}%")));
            _availableUpdate = null;
            _readyUpdate = update.TargetFullRelease;
            DataChanged?.Invoke(this, EventArgs.Empty);
            PromptToRestartForUpdate(updateManager, update.TargetFullRelease);
        } catch (Exception exception) {
            AppLog.Write(exception);
            SetStatus($"Unable to install update: {exception.Message}");
            MessageDialog.Show(IsVisible ? this : null, exception.Message, "Unable to install update", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private VmListRow? SelectedVm => VmGrid.SelectedItem as VmListRow;

    private GroupOption? SelectedFilter => GroupFilterComboBox.SelectedItem as GroupOption;

    private VmGroup? SelectedFilterGroup =>
        SelectedFilter?.Id is Guid groupId
            ? _groupCatalog.Groups.FirstOrDefault(group => group.Id == groupId)
            : null;

    private VmGroup? SelectedMembershipGroup =>
        MembershipGroupComboBox.SelectedItem is VmGroup group
            ? _groupCatalog.Groups.FirstOrDefault(candidate => candidate.Id == group.Id)
            : null;

    private void RebuildGroupSelectors(Guid? selectedFilterId = null, Guid? selectedMembershipGroupId = null) {
        IReadOnlyList<VmGroup> groups = _groupCatalog.Groups;
        var filterOptions = new List<GroupOption> { new(null, "All virtual machines") };
        filterOptions.AddRange(groups.Select(group => new GroupOption(group.Id, group.Name)));

        GroupFilterComboBox.ItemsSource = filterOptions;
        GroupFilterComboBox.SelectedItem = filterOptions.FirstOrDefault(option => option.Id == selectedFilterId)
            ?? filterOptions[0];
        MembershipGroupComboBox.ItemsSource = groups;
        MembershipGroupComboBox.SelectedItem = groups.FirstOrDefault(group => group.Id == selectedMembershipGroupId)
            ?? groups.FirstOrDefault();
    }

    private void ApplyGroupFilter() {
        Guid? selectedVmId = SelectedVm?.VirtualMachine.Id;
        Guid? selectedGroupId = SelectedFilter?.Id;
        IReadOnlyList<VirtualMachine> visibleVms = selectedGroupId is null
            ? _virtualMachines
            : _virtualMachines.Where(vm => _groupCatalog.Groups
                .First(group => group.Id == selectedGroupId)
                .VmIds.Contains(vm.Id))
                .ToList();

        VmGrid.ItemsSource = visibleVms
            .Select(vm => new VmListRow(
                vm,
                string.Join(", ", _groupCatalog.Groups
                    .Where(group => group.VmIds.Contains(vm.Id))
                    .Select(group => group.Name))))
            .ToList();
        VmGrid.SelectedItem = (VmGrid.ItemsSource as IReadOnlyList<VmListRow>)
            ?.FirstOrDefault(row => row.VirtualMachine.Id == selectedVmId);
        UpdateActionButtons();
    }

    private async Task RunOperationAsync(string status, Func<Task> operation) {
        if (_operationInProgress) {
            return;
        }

        _operationInProgress = true;
        SetStatus(status);
        UpdateActionButtons();
        try {
            await operation();
            SetStatus("Operation completed.");
        } catch (Exception exception) {
            SetStatus($"Operation failed: {exception.Message}");
            MessageDialog.Show(this, exception.Message, "VM Manager", MessageBoxButton.OK, MessageBoxImage.Error);
        } finally {
            _operationInProgress = false;
            await RefreshAsync(silent: true);
        }
    }

    private Task RunGroupOperationAsync(
        Guid groupId,
        string status,
        Func<VirtualMachine, bool> predicate,
        Func<Guid, CancellationToken, Task> operation) {
        VmGroup group = _groupCatalog.Groups.First(group => group.Id == groupId);
        var targets = _virtualMachines.Where(vm => group.VmIds.Contains(vm.Id) && predicate(vm)).ToList();
        return RunOperationAsync(status, async () => {
            foreach (VirtualMachine? vm in targets) {
                await operation(vm.Id, CancellationToken.None);
            }
        });
    }

    private void SetStatus(string message) => StatusTextBlock.Text = message.ReplaceLineEndings(" ");

    private void UpdateActionButtons() {
        VmGroup? filterGroup = SelectedFilterGroup;
        bool groupSelected = filterGroup is not null;
        DeleteGroupButton.IsEnabled = groupSelected && !_operationInProgress;
        StartGroupButton.IsEnabled = filterGroup is not null
            && _virtualMachines.Any(vm => filterGroup.VmIds.Contains(vm.Id) && vm.CanStart)
            && !_operationInProgress;
        ShutDownGroupButton.IsEnabled = filterGroup is not null
            && _virtualMachines.Any(vm => filterGroup.VmIds.Contains(vm.Id) && vm.CanStop)
            && !_operationInProgress;

        StartVmButton.IsEnabled = SelectedVm?.VirtualMachine.CanStart == true && !_operationInProgress;
        ShutDownVmButton.IsEnabled = SelectedVm?.VirtualMachine.CanStop == true && !_operationInProgress;
        TurnOffVmButton.IsEnabled = SelectedVm?.VirtualMachine.CanStop == true && !_operationInProgress;
        VmGroup? membershipGroup = SelectedMembershipGroup;
        bool selectedVmIsGroupMember = SelectedVm is not null
            && membershipGroup?.VmIds.Contains(SelectedVm.VirtualMachine.Id) == true;
        AddToGroupButton.IsEnabled = membershipGroup is not null
            && (SelectedVm is null || !selectedVmIsGroupMember)
            && !_operationInProgress;
        RemoveFromGroupButton.IsEnabled = SelectedVm is not null
            && selectedVmIsGroupMember
            && !_operationInProgress;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => ShowSettings();

    private void GroupFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (VmGrid is not null) {
            if (SelectedFilter?.Id is Guid groupId) {
                SelectMembershipGroup(groupId);
            }

            ApplyGroupFilter();
        }
    }

    private void VmGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateActionButtons();

    private void MembershipGroupComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateActionButtons();

    private void SelectMembershipGroup(Guid groupId) {
        if (MembershipGroupComboBox.ItemsSource is not IEnumerable<VmGroup> groups) {
            return;
        }

        VmGroup? group = groups.FirstOrDefault(candidate => candidate.Id == groupId);
        if (group is not null) {
            MembershipGroupComboBox.SelectedItem = group;
        }
    }

    private void VmGrid_PreviewMouseRightButtonDown(object sender, MouseButtonEventArgs e) {
        DataGridRow? row = FindVisualParent<DataGridRow>(e.OriginalSource as DependencyObject);
        if (row is null) {
            VmGrid.SelectedItem = null;
            VmGrid.ContextMenu.IsOpen = false;
            return;
        }

        row.IsSelected = true;
        row.Focus();
        VmGrid.ContextMenu.PlacementTarget = row;
        VmGrid.ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void VmContextMenu_Opened(object sender, RoutedEventArgs e) {
        VirtualMachine? vm = SelectedVm?.VirtualMachine;
        ContextStartVmMenuItem.IsEnabled = vm?.CanStart == true && !_operationInProgress;
        ContextShutDownVmMenuItem.IsEnabled = vm?.CanStop == true && !_operationInProgress;
        ContextTurnOffVmMenuItem.IsEnabled = vm?.CanStop == true && !_operationInProgress;
        RebuildContextAddToGroupMenu(vm);
    }

    private void RebuildContextAddToGroupMenu(VirtualMachine? vm) {
        ContextAddToGroupMenuItem.Items.Clear();
        if (vm is null || _operationInProgress) {
            ContextAddToGroupMenuItem.IsEnabled = false;
            return;
        }

        IReadOnlyList<VmGroup> availableGroups = _groupCatalog.Groups
            .Where(group => !group.VmIds.Contains(vm.Id))
            .ToList();
        ContextAddToGroupMenuItem.IsEnabled = availableGroups.Count > 0;

        foreach (VmGroup group in availableGroups) {
            var groupMenuItem = new WpfMenuItem {
                Header = group.Name,
                Tag = group.Id
            };
            groupMenuItem.Click += ContextAddToGroupMenuItem_Click;
            ContextAddToGroupMenuItem.Items.Add(groupMenuItem);
        }

        if (availableGroups.Count == 0) {
            ContextAddToGroupMenuItem.Items.Add(new WpfMenuItem {
                Header = "No available groups",
                IsEnabled = false
            });
        }
    }

    private async void ContextStartVmMenuItem_Click(object sender, RoutedEventArgs e) {
        if (SelectedVm is not null) {
            await StartVmAsync(SelectedVm.VirtualMachine.Id);
        }
    }

    private async void ContextShutDownVmMenuItem_Click(object sender, RoutedEventArgs e) {
        if (SelectedVm is not null) {
            await ShutDownVmAsync(SelectedVm.VirtualMachine.Id);
        }
    }

    private async void ContextTurnOffVmMenuItem_Click(object sender, RoutedEventArgs e) =>
        await TurnOffSelectedVmAsync();

    private async void ContextAddToGroupMenuItem_Click(object sender, RoutedEventArgs e) {
        VmListRow? selectedVm = SelectedVm;
        if (selectedVm is null || sender is not WpfMenuItem { Tag: Guid groupId }) {
            return;
        }

        VmGroup group = _groupCatalog.Groups.First(group => group.Id == groupId);
        await _groupCatalog.AddVmAsync(groupId, selectedVm.VirtualMachine.Id);
        RebuildGroupSelectors(SelectedFilter?.Id, groupId);
        ApplyGroupFilter();
        SetStatus($"Added {selectedVm.Name} to {group.Name}.");
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void NewGroupButton_Click(object sender, RoutedEventArgs e) {
        var dialog = new GroupNameDialog { Owner = this };
        if (dialog.ShowDialog() != true) {
            return;
        }

        try {
            VmGroup group = await _groupCatalog.CreateAsync(dialog.GroupName);
            RebuildGroupSelectors(group.Id, group.Id);
            ApplyGroupFilter();
            DataChanged?.Invoke(this, EventArgs.Empty);
        } catch (Exception exception) {
            MessageDialog.Show(this, exception.Message, "VM Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void DeleteGroupButton_Click(object sender, RoutedEventArgs e) {
        if (SelectedFilter?.Id is not Guid groupId) {
            return;
        }

        string groupName = SelectedFilter.Name;
        if (MessageDialog.Show(this, $"Delete group '{groupName}'? Virtual machines will not be deleted.",
                "Delete group", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) {
            return;
        }

        await _groupCatalog.DeleteAsync(groupId);
        RebuildGroupSelectors();
        ApplyGroupFilter();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void AddToGroupButton_Click(object sender, RoutedEventArgs e) {
        if (MembershipGroupComboBox.SelectedItem is not VmGroup group) {
            SetStatus("Create or select a group before adding a virtual machine.");
            return;
        }

        VmListRow? selectedVm = SelectedVm;
        if (selectedVm is null) {
            await AddSelectedVmsToGroupAsync(group);
            return;
        }

        VmGroup currentGroup = _groupCatalog.Groups.First(candidate => candidate.Id == group.Id);
        bool alreadyInGroup = currentGroup.VmIds.Contains(selectedVm.VirtualMachine.Id);
        if (alreadyInGroup) {
            SetStatus($"{selectedVm.Name} is already in {currentGroup.Name}.");
            return;
        }

        await _groupCatalog.AddVmAsync(group.Id, selectedVm.VirtualMachine.Id);
        RebuildGroupSelectors(SelectedFilter?.Id, group.Id);
        ApplyGroupFilter();
        SetStatus($"Added {selectedVm.Name} to {currentGroup.Name}.");
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private async Task AddSelectedVmsToGroupAsync(VmGroup group) {
        VmGroup currentGroup = _groupCatalog.Groups.First(candidate => candidate.Id == group.Id);
        IReadOnlyList<VirtualMachine> availableVms = _virtualMachines
            .Where(vm => !currentGroup.VmIds.Contains(vm.Id))
            .OrderBy(vm => vm.Name, StringComparer.OrdinalIgnoreCase)
            .ToList();

        if (availableVms.Count == 0) {
            SetStatus($"All virtual machines are already in {currentGroup.Name}.");
            return;
        }

        var dialog = new VmSelectionDialog(currentGroup.Name, availableVms) { Owner = this };
        if (dialog.ShowDialog() != true) {
            return;
        }

        IReadOnlyList<VirtualMachine> selectedVms = dialog.SelectedVirtualMachines;
        foreach (VirtualMachine vm in selectedVms) {
            await _groupCatalog.AddVmAsync(currentGroup.Id, vm.Id);
        }

        RebuildGroupSelectors(SelectedFilter?.Id, currentGroup.Id);
        ApplyGroupFilter();
        SetStatus($"Added {selectedVms.Count} virtual machine(s) to {currentGroup.Name}.");
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void RemoveFromGroupButton_Click(object sender, RoutedEventArgs e) {
        VmListRow? selectedVm = SelectedVm;
        if (selectedVm is null) {
            SetStatus("Select a virtual machine before removing it from a group.");
            return;
        }

        if (MembershipGroupComboBox.SelectedItem is not VmGroup group) {
            SetStatus("Create or select a group before removing a virtual machine.");
            return;
        }

        VmGroup currentGroup = _groupCatalog.Groups.First(candidate => candidate.Id == group.Id);
        bool wasInGroup = currentGroup.VmIds.Contains(selectedVm.VirtualMachine.Id);
        await _groupCatalog.RemoveVmAsync(group.Id, selectedVm.VirtualMachine.Id);
        RebuildGroupSelectors(SelectedFilter?.Id, group.Id);
        ApplyGroupFilter();
        SetStatus(wasInGroup
            ? $"Removed {selectedVm.Name} from {group.Name}."
            : $"{selectedVm.Name} is not in {group.Name}.");
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void StartVmButton_Click(object sender, RoutedEventArgs e) {
        if (SelectedVm is not null) {
            await StartVmAsync(SelectedVm.VirtualMachine.Id);
        }
    }

    private async void ShutDownVmButton_Click(object sender, RoutedEventArgs e) {
        if (SelectedVm is not null) {
            await ShutDownVmAsync(SelectedVm.VirtualMachine.Id);
        }
    }

    private async void TurnOffVmButton_Click(object sender, RoutedEventArgs e) {
        await TurnOffSelectedVmAsync();
    }

    private async Task TurnOffSelectedVmAsync() {
        if (SelectedVm is null) {
            return;
        }

        var confirmation = new TurnOffConfirmationDialog(SelectedVm.Name) { Owner = this };
        if (confirmation.ShowDialog() != true) {
            return;
        }

        await RunOperationAsync("Turning off virtual machine...",
            () => _hyperVService.TurnOffAsync(SelectedVm.VirtualMachine.Id));
    }

    private async void StartGroupButton_Click(object sender, RoutedEventArgs e) {
        if (SelectedFilter?.Id is Guid groupId) {
            await StartGroupAsync(groupId);
        }
    }

    private async void ShutDownGroupButton_Click(object sender, RoutedEventArgs e) {
        if (SelectedFilter?.Id is Guid groupId) {
            await ShutDownGroupAsync(groupId);
        }
    }

    private void Window_Closing(object? sender, CancelEventArgs e) {
        if (_allowClose) {
            return;
        }

        e.Cancel = true;
        Hide();
    }

    private static T? FindVisualParent<T>(DependencyObject? child) where T : DependencyObject {
        while (child is not null) {
            if (child is T parent) {
                return parent;
            }

            child = VisualTreeHelper.GetParent(child);
        }

        return null;
    }

    private static UpdateManager? CreateUpdateManager() {
        string? feedUrl = GetConfiguredUpdateFeedUrl();
        if (string.IsNullOrWhiteSpace(feedUrl)) {
            return null;
        }

        try {
            var updateManager = new UpdateManager(feedUrl);
            return updateManager.IsInstalled ? updateManager : null;
        } catch {
            return null;
        }
    }

    private static string? GetConfiguredUpdateFeedUrl() =>
        typeof(App).Assembly.GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "VelopackUpdateUrl")
            ?.Value;

    private void StopBackgroundUpdateChecks() {
        _updateTimer.Stop();
        _availableUpdate = null;
        _readyUpdate = null;
        _lastNotifiedUpdateVersion = null;
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private void NotifyUpdateAvailable(string version, bool isReadyToInstall) {
        if (_lastNotifiedUpdateVersion == version) {
            return;
        }

        _lastNotifiedUpdateVersion = version;
        UpdateAvailable?.Invoke(this, new UpdateAvailableEventArgs(version, isReadyToInstall));
    }

    private void PromptToRestartForUpdate(UpdateManager updateManager, VelopackAsset update) {
        SetStatus($"Update {update.Version} is ready to install.");
        if (MessageDialog.Show(IsVisible ? this : null, $"Update {update.Version} is ready. Restart VM Manager now to install it?",
                "Update ready", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) {
            return;
        }

        _allowClose = true;
        updateManager.ApplyUpdatesAndRestart(update);
    }

    private sealed record GroupOption(Guid? Id, string Name);

    private sealed record VmListRow(VirtualMachine VirtualMachine, string GroupsDisplay) {
        public string Name => VirtualMachine.Name;
        public VirtualMachineState State => VirtualMachine.State;
        public string CpuDisplay => $"{VirtualMachine.CpuUsage}%";
        public string MemoryDisplay => $"{VirtualMachine.MemoryAssignedBytes / 1024d / 1024d:N0} MB";
    }
}

public sealed class UpdateAvailableEventArgs(string version, bool isReadyToInstall) : EventArgs {
    public string Version { get; } = version;

    public bool IsReadyToInstall { get; } = isReadyToInstall;
}
