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
    private static readonly TimeSpan UpdateCheckInterval = TimeSpan.FromHours(1);
    private static readonly TimeSpan VmRefreshInterval = TimeSpan.FromSeconds(5);
    private readonly IHyperVService _hyperVService;
    private readonly VmGroupCatalog _groupCatalog;
    private readonly IAppSettingsRepository _settingsRepository;
    private readonly LoginStartupService _loginStartupService;
    private readonly DispatcherTimer _refreshTimer;
    private readonly DispatcherTimer _updateTimer;
    private AppSettings _settings;
    private IReadOnlyList<VirtualMachine> _virtualMachines = [];
    private UpdateInfo? _availableUpdate;
    private VelopackAsset? _readyUpdate;
    private string? _lastNotifiedUpdateKey;
    private SettingsWindow? _settingsWindow;
    private bool _allowClose;
    private bool _refreshInProgress;
    private bool _updateCheckInProgress;
    private readonly HashSet<Guid> _powerOperationVmIds = [];
    private readonly Dictionary<Guid, VirtualMachineState> _dashboardStateOverrides = [];

    public MainWindow(
        IHyperVService hyperVService,
        VmGroupCatalog groupCatalog,
        IAppSettingsRepository settingsRepository,
        LoginStartupService loginStartupService,
        AppSettings settings) {
        _hyperVService = hyperVService;
        _groupCatalog = groupCatalog;
        _settingsRepository = settingsRepository;
        _loginStartupService = loginStartupService;
        _settings = settings;

        InitializeComponent();
        RebuildGroupSelectors();

        _refreshTimer = new DispatcherTimer { Interval = VmRefreshInterval };
        _refreshTimer.Tick += async (_, _) => await RefreshAsync(silent: true);
        _refreshTimer.Start();

        _updateTimer = new DispatcherTimer { Interval = UpdateCheckInterval };
        _updateTimer.Tick += async (_, _) => await CheckForUpdatesAsync();
    }

    public event EventHandler? DataChanged;

    public event EventHandler<UpdateAvailableEventArgs>? UpdateAvailable;

    public event EventHandler<VmPowerOperationFinishedEventArgs>? VmPowerOperationFinished;

    public IReadOnlyList<VirtualMachine> VirtualMachines => _virtualMachines;

    public IReadOnlyList<VmGroup> Groups => _groupCatalog.Groups;

    public bool StartMinimized => _settings.StartMinimized;

    public bool AutoUpdateEnabled => _settings.AutoUpdateEnabled;

    public bool StartAtLogin => _settings.StartAtLogin;

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
        if (_refreshInProgress) {
            if (!silent) {
                SetStatus("A virtual machine refresh is already in progress.");
            }

            return;
        }

        _refreshInProgress = true;
        try {
            if (!silent) {
                SetStatus("Refreshing virtual machines...");
            }

            IReadOnlyList<VirtualMachine> refreshedVirtualMachines = await _hyperVService.GetVirtualMachinesAsync();
            bool hasChanges = !VmSnapshotsEqual(_virtualMachines, refreshedVirtualMachines);

            _virtualMachines = refreshedVirtualMachines;
            ApplyGroupFilter();
            SetStatus($"{_virtualMachines.Count} virtual machine(s) found. Last refreshed {DateTime.Now:t}.");

            if (hasChanges || !silent) {
                DataChanged?.Invoke(this, EventArgs.Empty);
            }
        } catch (Exception exception) {
            SetStatus($"Unable to query Hyper-V: {exception.Message}");
        } finally {
            _refreshInProgress = false;
        }
    }

    public Task StartVmAsync(Guid vmId) =>
        RunVmPowerOperationsAsync(
            [vmId],
            "Starting virtual machine...",
            "Starting {0} virtual machine(s)...",
            "started",
            "Unable to start virtual machine",
            VmPowerOperationKind.Start,
            CanStart,
            _hyperVService.StartAsync);

    public Task ShutDownVmAsync(Guid vmId) =>
        RunVmPowerOperationsAsync(
            [vmId],
            "Shutting down virtual machine...",
            "Shutting down {0} virtual machine(s)...",
            "shut down",
            "Unable to shut down virtual machine",
            VmPowerOperationKind.ShutDown,
            CanStop,
            _hyperVService.ShutDownAsync);

    public Task StartGroupAsync(Guid groupId) {
        VmGroup group = _groupCatalog.Groups.First(group => group.Id == groupId);
        IReadOnlyList<Guid> targets = _virtualMachines
            .Where(vm => group.VmIds.Contains(vm.Id) && CanStart(vm))
            .Select(vm => vm.Id)
            .ToList();

        return RunVmPowerOperationsAsync(
            targets,
            "Starting virtual machine...",
            "Starting {0} virtual machine(s)...",
            "started",
            "Unable to start virtual machine",
            VmPowerOperationKind.Start,
            CanStart,
            _hyperVService.StartAsync);
    }

    public Task ShutDownGroupAsync(Guid groupId) {
        VmGroup group = _groupCatalog.Groups.First(group => group.Id == groupId);
        IReadOnlyList<Guid> targets = _virtualMachines
            .Where(vm => group.VmIds.Contains(vm.Id) && CanStop(vm))
            .Select(vm => vm.Id)
            .ToList();

        return RunVmPowerOperationsAsync(
            targets,
            "Shutting down virtual machine...",
            "Shutting down {0} virtual machine(s)...",
            "shut down",
            "Unable to shut down virtual machine",
            VmPowerOperationKind.ShutDown,
            CanStop,
            _hyperVService.ShutDownAsync);
    }

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
        }

        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    public async Task SetStartAtLoginAsync(bool startAtLogin) {
        if (_settings.StartAtLogin == startAtLogin) {
            return;
        }

        _loginStartupService.SetEnabled(startAtLogin);
        AppSettings updatedSettings = _settings with { StartAtLogin = startAtLogin };
        await _settingsRepository.SaveAsync(updatedSettings);
        _settings = updatedSettings;
    }

    public void StartBackgroundUpdateChecks(bool checkNow) {
        if (!AutoUpdateAvailable) {
            return;
        }

        if (!_updateTimer.IsEnabled) {
            _updateTimer.Start();
        }

        if (checkNow) {
            _ = CheckForUpdatesAsync();
        }
    }

    public Task CheckForUpdatesNowAsync() => CheckForUpdatesAsync(silent: false);

    public async Task CheckForUpdatesAsync(bool silent = true) {
        if (_updateCheckInProgress) {
            if (!silent && _updateCheckInProgress) {
                SetStatus("An update check is already in progress.");
            }

            return;
        }

        UpdateManager? updateManager = CreateUpdateManager();
        if (updateManager is null) {
            if (!silent) {
                SetStatus("Updates are not configured for this build.");
            }

            return;
        }

        _updateCheckInProgress = true;
        try {
            VelopackAsset? pendingUpdate = updateManager.UpdatePendingRestart;
            if (pendingUpdate is not null) {
                _availableUpdate = null;
                _readyUpdate = pendingUpdate;
                DataChanged?.Invoke(this, EventArgs.Empty);
                if (_settings.AutoUpdateEnabled) {
                    NotifyUpdateAvailable(pendingUpdate.Version.ToString(), UpdateNotificationKind.Installing);
                    ApplyUpdateAndRestart(updateManager, pendingUpdate);
                    return;
                }

                NotifyUpdateAvailable(pendingUpdate.Version.ToString(), UpdateNotificationKind.ReadyToInstall);
                return;
            }

            if (!silent) {
                SetStatus("Checking for updates...");
            }

            UpdateInfo? update = await updateManager.CheckForUpdatesAsync();
            if (update is null) {
                _availableUpdate = null;
                _readyUpdate = null;
                _lastNotifiedUpdateKey = null;
                DataChanged?.Invoke(this, EventArgs.Empty);
                if (!silent) {
                    SetStatus("VM Manager is up to date.");
                }

                return;
            }

            _availableUpdate = update;
            _readyUpdate = null;
            DataChanged?.Invoke(this, EventArgs.Empty);
            if (_settings.AutoUpdateEnabled) {
                await DownloadUpdateAsync(updateManager, update, automatic: true);
                return;
            }

            SetStatus($"Update {update.TargetFullRelease.Version} is available.");
            NotifyUpdateAvailable(update.TargetFullRelease.Version.ToString(), UpdateNotificationKind.Available);
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

            await DownloadUpdateAsync(updateManager, update, automatic: false);
        } catch (Exception exception) {
            AppLog.Write(exception);
            SetStatus($"Unable to install update: {exception.Message}");
            MessageDialog.Show(IsVisible ? this : null, exception.Message, "Unable to install update", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private IReadOnlyList<VmListRow> SelectedVms => VmGrid.SelectedItems.Cast<VmListRow>().ToList();

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
        HashSet<Guid> selectedVmIds = SelectedVms
            .Select(row => row.VirtualMachine.Id)
            .ToHashSet();
        Guid? selectedGroupId = SelectedFilter?.Id;
        IReadOnlyList<VirtualMachine> visibleVms = selectedGroupId is null
            ? _virtualMachines
            : _virtualMachines.Where(vm => _groupCatalog.Groups
                .First(group => group.Id == selectedGroupId)
                .VmIds.Contains(vm.Id))
                .ToList();

        IReadOnlyList<VmListRow> rows = visibleVms
            .Select(vm => new VmListRow(
                vm,
                string.Join(", ", _groupCatalog.Groups
                    .Where(group => group.VmIds.Contains(vm.Id))
                    .Select(group => group.Name)),
                GetDashboardState(vm)))
            .ToList();

        VmGrid.ItemsSource = rows;
        foreach (VmListRow row in rows.Where(row => selectedVmIds.Contains(row.VirtualMachine.Id))) {
            VmGrid.SelectedItems.Add(row);
        }

        UpdateActionButtons();
    }

    private async Task RunVmPowerOperationsAsync(
        IEnumerable<Guid> vmIds,
        string singularStatus,
        string pluralStatusFormat,
        string completedVerb,
        string failureTitle,
        VmPowerOperationKind operationKind,
        Func<VirtualMachine, bool> predicate,
        Func<Guid, CancellationToken, Task> operation) {
        IReadOnlyList<VirtualMachine> targets = vmIds
            .Distinct()
            .Select(id => _virtualMachines.FirstOrDefault(vm => vm.Id == id))
            .Where(vm => vm is not null && predicate(vm))
            .Select(vm => vm!)
            .ToList();

        if (targets.Count == 0) {
            SetStatus($"No virtual machines can be {completedVerb}.");
            UpdateActionButtons();
            return;
        }

        foreach (VirtualMachine vm in targets) {
            _powerOperationVmIds.Add(vm.Id);
        }

        if (operationKind == VmPowerOperationKind.Start) {
            foreach (VirtualMachine vm in targets) {
                _dashboardStateOverrides[vm.Id] = VirtualMachineState.Starting;
            }
        }

        SetStatus(targets.Count == 1 ? singularStatus : string.Format(pluralStatusFormat, targets.Count));
        ApplyGroupFilter();

        VmPowerOperationResult[] results = await Task.WhenAll(targets.Select(vm =>
            RunTrackedVmPowerOperationAsync(vm, operation)));
        int succeededCount = results.Count(result => result.Exception is null);
        IReadOnlyList<VmPowerOperationResult> failures = results.Where(result => result.Exception is not null).ToList();
        string completedMessage = $"{CapitalizeFirst(completedVerb)} {succeededCount} virtual machine(s).";

        if (failures.Count == 0) {
            SetStatus(_powerOperationVmIds.Count == 0
                ? completedMessage
                : $"{completedMessage} {_powerOperationVmIds.Count} power operation(s) still running.");
        } else {
            SetStatus($"{completedMessage} {failures.Count} failed.");
            string errorMessage = string.Join(Environment.NewLine, failures.Select(result =>
                $"{result.VirtualMachine.Name}: {result.Exception!.Message}"));
            MessageDialog.Show(this, errorMessage, failureTitle, MessageBoxButton.OK, MessageBoxImage.Error);
        }

        VmPowerOperationFinished?.Invoke(this, new VmPowerOperationFinishedEventArgs(
            operationKind,
            targets.Select(vm => vm.Name).ToList(),
            targets.Count,
            succeededCount,
            failures.Count));

        await RefreshAsync(silent: true);
        foreach (VirtualMachine vm in targets) {
            _dashboardStateOverrides.Remove(vm.Id);
        }

        ApplyGroupFilter();
        UpdateActionButtons();
    }

    private async Task<VmPowerOperationResult> RunTrackedVmPowerOperationAsync(
        VirtualMachine vm,
        Func<Guid, CancellationToken, Task> operation) {
        try {
            await operation(vm.Id, CancellationToken.None);
            return new VmPowerOperationResult(vm, null);
        } catch (Exception exception) {
            AppLog.Write(exception);
            return new VmPowerOperationResult(vm, exception);
        } finally {
            _powerOperationVmIds.Remove(vm.Id);
            UpdateActionButtons();
        }
    }

    private void SetStatus(string message) => StatusTextBlock.Text = message.ReplaceLineEndings(" ");

    private VirtualMachineState GetDashboardState(VirtualMachine vm) =>
        _dashboardStateOverrides.TryGetValue(vm.Id, out VirtualMachineState state)
            ? state
            : vm.State;

    private static bool VmSnapshotsEqual(IReadOnlyList<VirtualMachine> left, IReadOnlyList<VirtualMachine> right) =>
        left.Count == right.Count
        && left.Zip(right).All(pair => pair.First == pair.Second);

    private static string CapitalizeFirst(string value) =>
        string.IsNullOrEmpty(value) ? value : $"{char.ToUpperInvariant(value[0])}{value[1..]}";

    private bool CanStart(VirtualMachine vm) => vm.CanStart && !_powerOperationVmIds.Contains(vm.Id);

    private bool CanStop(VirtualMachine vm) => vm.CanStop && !_powerOperationVmIds.Contains(vm.Id);

    private void UpdateActionButtons() {
        VmGroup? filterGroup = SelectedFilterGroup;
        bool groupSelected = filterGroup is not null;
        DeleteGroupButton.IsEnabled = groupSelected;
        StartGroupButton.IsEnabled = filterGroup is not null
            && _virtualMachines.Any(vm => filterGroup.VmIds.Contains(vm.Id) && CanStart(vm));
        ShutDownGroupButton.IsEnabled = filterGroup is not null
            && _virtualMachines.Any(vm => filterGroup.VmIds.Contains(vm.Id) && CanStop(vm));

        IReadOnlyList<VmListRow> selectedVms = SelectedVms;
        StartVmButton.IsEnabled = selectedVms.Any(row => CanStart(row.VirtualMachine));
        ShutDownVmButton.IsEnabled = selectedVms.Any(row => CanStop(row.VirtualMachine));
        TurnOffVmButton.IsEnabled = selectedVms.Any(row => CanStop(row.VirtualMachine));
        VmGroup? membershipGroup = SelectedMembershipGroup;
        bool anySelectedVmIsGroupMember = selectedVms.Any(row =>
            membershipGroup?.VmIds.Contains(row.VirtualMachine.Id) == true);
        bool anySelectedVmCanBeAdded = selectedVms.Any(row =>
            membershipGroup?.VmIds.Contains(row.VirtualMachine.Id) == false);
        AddToGroupButton.IsEnabled = membershipGroup is not null
            && (selectedVms.Count == 0 || anySelectedVmCanBeAdded);
        RemoveFromGroupButton.IsEnabled = selectedVms.Count > 0
            && anySelectedVmIsGroupMember;
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
            VmGrid.SelectedItems.Clear();
            VmGrid.ContextMenu.IsOpen = false;
            return;
        }

        if (!row.IsSelected) {
            VmGrid.SelectedItems.Clear();
            row.IsSelected = true;
        }

        row.Focus();
        VmGrid.ContextMenu.PlacementTarget = row;
        VmGrid.ContextMenu.IsOpen = true;
        e.Handled = true;
    }

    private void VmContextMenu_Opened(object sender, RoutedEventArgs e) {
        IReadOnlyList<VmListRow> selectedVms = SelectedVms;
        ContextStartVmMenuItem.IsEnabled = selectedVms.Any(row => CanStart(row.VirtualMachine));
        ContextShutDownVmMenuItem.IsEnabled = selectedVms.Any(row => CanStop(row.VirtualMachine));
        ContextTurnOffVmMenuItem.IsEnabled = selectedVms.Any(row => CanStop(row.VirtualMachine));
        RebuildContextAddToGroupMenu(selectedVms);
    }

    private void RebuildContextAddToGroupMenu(IReadOnlyList<VmListRow> selectedVms) {
        ContextAddToGroupMenuItem.Items.Clear();
        if (selectedVms.Count == 0) {
            ContextAddToGroupMenuItem.IsEnabled = false;
            return;
        }

        IReadOnlyList<VmGroup> availableGroups = _groupCatalog.Groups
            .Where(group => selectedVms.Any(row => !group.VmIds.Contains(row.VirtualMachine.Id)))
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
        await StartSelectedVmsAsync();
    }

    private async void ContextShutDownVmMenuItem_Click(object sender, RoutedEventArgs e) {
        await ShutDownSelectedVmsAsync();
    }

    private async void ContextTurnOffVmMenuItem_Click(object sender, RoutedEventArgs e) =>
        await TurnOffSelectedVmsAsync();

    private async void ContextAddToGroupMenuItem_Click(object sender, RoutedEventArgs e) {
        IReadOnlyList<VmListRow> selectedVms = SelectedVms;
        if (selectedVms.Count == 0 || sender is not WpfMenuItem { Tag: Guid groupId }) {
            return;
        }

        VmGroup group = _groupCatalog.Groups.First(group => group.Id == groupId);
        int addedCount = 0;
        foreach (VmListRow selectedVm in selectedVms.Where(row => !group.VmIds.Contains(row.VirtualMachine.Id))) {
            await _groupCatalog.AddVmAsync(groupId, selectedVm.VirtualMachine.Id);
            addedCount++;
        }

        RebuildGroupSelectors(SelectedFilter?.Id, groupId);
        ApplyGroupFilter();
        SetStatus($"Added {addedCount} virtual machine(s) to {group.Name}.");
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

        IReadOnlyList<VmListRow> selectedVms = SelectedVms;
        if (selectedVms.Count == 0) {
            await AddSelectedVmsToGroupAsync(group);
            return;
        }

        VmGroup currentGroup = _groupCatalog.Groups.First(candidate => candidate.Id == group.Id);
        IReadOnlyList<VmListRow> targets = selectedVms
            .Where(row => !currentGroup.VmIds.Contains(row.VirtualMachine.Id))
            .ToList();
        if (targets.Count == 0) {
            SetStatus("Selected virtual machine(s) are already in the group.");
            return;
        }

        foreach (VmListRow selectedVm in targets) {
            await _groupCatalog.AddVmAsync(group.Id, selectedVm.VirtualMachine.Id);
        }

        RebuildGroupSelectors(SelectedFilter?.Id, group.Id);
        ApplyGroupFilter();
        SetStatus($"Added {targets.Count} virtual machine(s) to {currentGroup.Name}.");
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
        IReadOnlyList<VmListRow> selectedVms = SelectedVms;
        if (selectedVms.Count == 0) {
            SetStatus("Select a virtual machine before removing it from a group.");
            return;
        }

        if (MembershipGroupComboBox.SelectedItem is not VmGroup group) {
            SetStatus("Create or select a group before removing a virtual machine.");
            return;
        }

        VmGroup currentGroup = _groupCatalog.Groups.First(candidate => candidate.Id == group.Id);
        IReadOnlyList<VmListRow> targets = selectedVms
            .Where(row => currentGroup.VmIds.Contains(row.VirtualMachine.Id))
            .ToList();
        foreach (VmListRow selectedVm in targets) {
            await _groupCatalog.RemoveVmAsync(group.Id, selectedVm.VirtualMachine.Id);
        }

        RebuildGroupSelectors(SelectedFilter?.Id, group.Id);
        ApplyGroupFilter();
        SetStatus(targets.Count > 0
            ? $"Removed {targets.Count} virtual machine(s) from {group.Name}."
            : "Selected virtual machine(s) are not in the group.");
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void StartVmButton_Click(object sender, RoutedEventArgs e) {
        await StartSelectedVmsAsync();
    }

    private async void ShutDownVmButton_Click(object sender, RoutedEventArgs e) {
        await ShutDownSelectedVmsAsync();
    }

    private async void TurnOffVmButton_Click(object sender, RoutedEventArgs e) {
        await TurnOffSelectedVmsAsync();
    }

    private Task StartSelectedVmsAsync() =>
        RunVmPowerOperationsAsync(
            SelectedVms.Select(row => row.VirtualMachine.Id),
            "Starting virtual machine...",
            "Starting {0} virtual machine(s)...",
            "started",
            "Unable to start virtual machine",
            VmPowerOperationKind.Start,
            CanStart,
            _hyperVService.StartAsync);

    private Task ShutDownSelectedVmsAsync() =>
        RunVmPowerOperationsAsync(
            SelectedVms.Select(row => row.VirtualMachine.Id),
            "Shutting down virtual machine...",
            "Shutting down {0} virtual machine(s)...",
            "shut down",
            "Unable to shut down virtual machine",
            VmPowerOperationKind.ShutDown,
            CanStop,
            _hyperVService.ShutDownAsync);

    private async Task TurnOffSelectedVmsAsync() {
        IReadOnlyList<VirtualMachine> targets = SelectedVms
            .Select(row => row.VirtualMachine)
            .Where(CanStop)
            .ToList();
        if (targets.Count == 0) {
            return;
        }

        string targetName = targets.Count == 1
            ? targets[0].Name
            : $"{targets.Count} virtual machines";
        var confirmation = new TurnOffConfirmationDialog(targetName, targets.Count > 1) { Owner = this };
        if (confirmation.ShowDialog() != true) {
            return;
        }

        await RunVmPowerOperationsAsync(
            targets.Select(vm => vm.Id),
            "Turning off virtual machine...",
            "Turning off {0} virtual machine(s)...",
            "turned off",
            "Unable to turn off virtual machine",
            VmPowerOperationKind.TurnOff,
            CanStop,
            _hyperVService.TurnOffAsync);
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

    private async Task DownloadUpdateAsync(UpdateManager updateManager, UpdateInfo update, bool automatic) {
        string version = update.TargetFullRelease.Version.ToString();
        if (automatic) {
            NotifyUpdateAvailable(version, UpdateNotificationKind.Installing);
        }

        SetStatus($"Downloading update {version}...");
        await updateManager.DownloadUpdatesAsync(update, progress =>
            Dispatcher.BeginInvoke(() => SetStatus($"Downloading update {version}... {progress}%")));

        _availableUpdate = null;
        _readyUpdate = update.TargetFullRelease;
        DataChanged?.Invoke(this, EventArgs.Empty);

        if (automatic) {
            ApplyUpdateAndRestart(updateManager, update.TargetFullRelease);
            return;
        }

        NotifyUpdateAvailable(version, UpdateNotificationKind.ReadyToInstall);
        PromptToRestartForUpdate(updateManager, update.TargetFullRelease);
    }

    private void NotifyUpdateAvailable(string version, UpdateNotificationKind kind) {
        string notificationKey = $"{kind}:{version}";
        if (_lastNotifiedUpdateKey == notificationKey) {
            return;
        }

        _lastNotifiedUpdateKey = notificationKey;
        UpdateAvailable?.Invoke(this, new UpdateAvailableEventArgs(version, kind));
    }

    private void ApplyUpdateAndRestart(UpdateManager updateManager, VelopackAsset update) {
        SetStatus($"Applying update {update.Version}...");
        _allowClose = true;
        updateManager.ApplyUpdatesAndRestart(update);
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

    private sealed record VmPowerOperationResult(VirtualMachine VirtualMachine, Exception? Exception);

    private sealed record VmListRow(VirtualMachine VirtualMachine, string GroupsDisplay, VirtualMachineState State) {
        public string Name => VirtualMachine.Name;
        public bool IsRunning => State == VirtualMachineState.Running;
        public string CpuDisplay => $"{VirtualMachine.CpuUsage}%";
        public string MemoryDisplay => $"{VirtualMachine.MemoryAssignedBytes / 1024d / 1024d:N0} MB";
    }
}

public sealed class UpdateAvailableEventArgs(string version, UpdateNotificationKind kind) : EventArgs {
    public string Version { get; } = version;

    public UpdateNotificationKind Kind { get; } = kind;

    public bool IsReadyToInstall => Kind == UpdateNotificationKind.ReadyToInstall;
}

public sealed class VmPowerOperationFinishedEventArgs(
    VmPowerOperationKind kind,
    IReadOnlyList<string> vmNames,
    int totalCount,
    int succeededCount,
    int failedCount) : EventArgs {
    public VmPowerOperationKind Kind { get; } = kind;

    public IReadOnlyList<string> VmNames { get; } = vmNames;

    public int TotalCount { get; } = totalCount;

    public int SucceededCount { get; } = succeededCount;

    public int FailedCount { get; } = failedCount;

    public bool HasFailures => FailedCount > 0;
}

public enum UpdateNotificationKind {
    Available,
    Installing,
    ReadyToInstall
}

public enum VmPowerOperationKind {
    Start,
    ShutDown,
    TurnOff
}
