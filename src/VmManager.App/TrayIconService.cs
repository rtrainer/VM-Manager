using System.Drawing;

using VmManager.Core.Models;

using Forms = System.Windows.Forms;

namespace VmManager.App;

public sealed class TrayIconService : IDisposable {
    private readonly MainWindow _mainWindow;
    private readonly Icon _applicationIcon;
    private readonly Font _runningVmMenuFont;
    private readonly Forms.NotifyIcon _notifyIcon;

    public TrayIconService(MainWindow mainWindow) {
        _mainWindow = mainWindow;
        _applicationIcon = Icon.ExtractAssociatedIcon(Environment.ProcessPath!)
            ?? (Icon)SystemIcons.Application.Clone();
        _runningVmMenuFont = CreateRunningVmMenuFont();
        _notifyIcon = new Forms.NotifyIcon {
            Icon = _applicationIcon,
            Text = "VM Manager",
            Visible = true
        };
        _notifyIcon.MouseDoubleClick += NotifyIcon_MouseDoubleClick;
        _mainWindow.DataChanged += (_, _) => RebuildMenu();
        RebuildMenu();
    }

    public void Dispose() {
        _notifyIcon.Visible = false;
        _notifyIcon.Dispose();
        _applicationIcon.Dispose();
        _runningVmMenuFont.Dispose();
    }

    private void NotifyIcon_MouseDoubleClick(object? sender, Forms.MouseEventArgs e) {
        if (e.Button == Forms.MouseButtons.Left) {
            _mainWindow.ShowFromTray();
        }
    }

    private void RebuildMenu() {
        var menu = new Forms.ContextMenuStrip();
        menu.Items.Add("Open dashboard", null, (_, _) => _mainWindow.ShowFromTray());
        menu.Items.Add("Refresh", null, async (_, _) => await _mainWindow.RefreshAsync());
        menu.Items.Add("Settings...", null, (_, _) => _mainWindow.ShowSettings());
        menu.Items.Add(new Forms.ToolStripSeparator());

        foreach (VirtualMachine vm in _mainWindow.VirtualMachines) {
            menu.Items.Add(CreateVmItem(vm));
        }

        menu.Items.Add(new Forms.ToolStripSeparator());
        menu.Items.Add("Exit", null, (_, _) => _mainWindow.ExitApplication());

        ContextMenuStrip? oldMenu = _notifyIcon.ContextMenuStrip;
        _notifyIcon.ContextMenuStrip = menu;
        oldMenu?.Dispose();
    }

    private Forms.ToolStripMenuItem CreateVmItem(VirtualMachine vm) {
        var vmItem = new Forms.ToolStripMenuItem($"{GetStateMarker(vm.State)} {vm.Name} ({vm.State})") {
            ForeColor = vm.State == VirtualMachineState.Running
                ? Color.ForestGreen
                : SystemColors.ControlText
        };
        if (vm.State == VirtualMachineState.Running) {
            vmItem.Font = _runningVmMenuFont;
        }

        vmItem.DropDownItems.Add("Start", null, async (_, _) => await _mainWindow.StartVmAsync(vm.Id))
            .Enabled = vm.CanStart;
        vmItem.DropDownItems.Add("Shut down", null, async (_, _) => await _mainWindow.ShutDownVmAsync(vm.Id))
            .Enabled = vm.CanStop;
        return vmItem;
    }

    private static string GetStateMarker(VirtualMachineState state) =>
        state == VirtualMachineState.Running ? "●" : "○";

    private static Font CreateRunningVmMenuFont() {
        Font? menuFont = SystemFonts.MenuFont;
        return menuFont is null
            ? new Font("Segoe UI", 9f, FontStyle.Bold)
            : new Font(menuFont, FontStyle.Bold);
    }
}
