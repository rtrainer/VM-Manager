using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;

using VmManager.Core.Models;
using VmManager.Core.Services;

using MessageBox = System.Windows.MessageBox;

namespace VmManager.App;

public partial class MainWindow : Window {
    private readonly IHyperVService _hyperVService;
    private readonly VmGroupCatalog _groupCatalog;
    private readonly IAppSettingsRepository _settingsRepository;
    private readonly DispatcherTimer _refreshTimer;
    private AppSettings _settings;
    private IReadOnlyList<VirtualMachine> _virtualMachines = [];
    private SettingsWindow? _settingsWindow;
    private bool _allowClose;
    private bool _operationInProgress;

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
    }

    public event EventHandler? DataChanged;

    public IReadOnlyList<VirtualMachine> VirtualMachines => _virtualMachines;

    public IReadOnlyList<VmGroup> Groups => _groupCatalog.Groups;

    public bool StartMinimized => _settings.StartMinimized;

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
            _settingsWindow.Activate();
            return;
        }

        _settingsWindow = new SettingsWindow(this);
        _settingsWindow.Closed += (_, _) => _settingsWindow = null;
        if (IsVisible) {
            _settingsWindow.Owner = this;
        }

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

    private VmListRow? SelectedVm => VmGrid.SelectedItem as VmListRow;

    private GroupOption? SelectedFilter => GroupFilterComboBox.SelectedItem as GroupOption;

    private void RebuildGroupSelectors(Guid? selectedFilterId = null) {
        IReadOnlyList<VmGroup> groups = _groupCatalog.Groups;
        var filterOptions = new List<GroupOption> { new(null, "All virtual machines") };
        filterOptions.AddRange(groups.Select(group => new GroupOption(group.Id, group.Name)));

        GroupFilterComboBox.ItemsSource = filterOptions;
        GroupFilterComboBox.SelectedItem = filterOptions.FirstOrDefault(option => option.Id == selectedFilterId)
            ?? filterOptions[0];
        MembershipGroupComboBox.ItemsSource = groups;
        MembershipGroupComboBox.SelectedIndex = groups.Count > 0 ? 0 : -1;
    }

    private void ApplyGroupFilter() {
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
            MessageBox.Show(exception.Message, "VM Manager", MessageBoxButton.OK, MessageBoxImage.Error);
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
        StartVmButton.IsEnabled = SelectedVm?.VirtualMachine.CanStart == true && !_operationInProgress;
        ShutDownVmButton.IsEnabled = SelectedVm?.VirtualMachine.CanStop == true && !_operationInProgress;
        TurnOffVmButton.IsEnabled = SelectedVm?.VirtualMachine.CanStop == true && !_operationInProgress;
    }

    private async void RefreshButton_Click(object sender, RoutedEventArgs e) => await RefreshAsync();

    private void SettingsButton_Click(object sender, RoutedEventArgs e) => ShowSettings();

    private void GroupFilterComboBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        if (VmGrid is not null) {
            ApplyGroupFilter();
        }
    }

    private void VmGrid_SelectionChanged(object sender, SelectionChangedEventArgs e) => UpdateActionButtons();

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

    private async void NewGroupButton_Click(object sender, RoutedEventArgs e) {
        var dialog = new GroupNameDialog { Owner = this };
        if (dialog.ShowDialog() != true) {
            return;
        }

        try {
            VmGroup group = await _groupCatalog.CreateAsync(dialog.GroupName);
            RebuildGroupSelectors(group.Id);
            ApplyGroupFilter();
            DataChanged?.Invoke(this, EventArgs.Empty);
        } catch (Exception exception) {
            MessageBox.Show(exception.Message, "VM Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private async void DeleteGroupButton_Click(object sender, RoutedEventArgs e) {
        if (SelectedFilter?.Id is not Guid groupId) {
            return;
        }

        if (MessageBox.Show("Delete the selected group? Virtual machines will not be deleted.",
                "VM Manager", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) {
            return;
        }

        await _groupCatalog.DeleteAsync(groupId);
        RebuildGroupSelectors();
        ApplyGroupFilter();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void AddToGroupButton_Click(object sender, RoutedEventArgs e) {
        if (SelectedVm is null || MembershipGroupComboBox.SelectedItem is not VmGroup group) {
            return;
        }

        await _groupCatalog.AddVmAsync(group.Id, SelectedVm.VirtualMachine.Id);
        ApplyGroupFilter();
        DataChanged?.Invoke(this, EventArgs.Empty);
    }

    private async void RemoveFromGroupButton_Click(object sender, RoutedEventArgs e) {
        if (SelectedVm is null || MembershipGroupComboBox.SelectedItem is not VmGroup group) {
            return;
        }

        await _groupCatalog.RemoveVmAsync(group.Id, SelectedVm.VirtualMachine.Id);
        ApplyGroupFilter();
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

    private sealed record GroupOption(Guid? Id, string Name);

    private sealed record VmListRow(VirtualMachine VirtualMachine, string GroupsDisplay) {
        public string Name => VirtualMachine.Name;
        public VirtualMachineState State => VirtualMachine.State;
        public string CpuDisplay => $"{VirtualMachine.CpuUsage}%";
        public string MemoryDisplay => $"{VirtualMachine.MemoryAssignedBytes / 1024d / 1024d:N0} MB";
    }
}
