using System.Windows;
using System.Windows.Controls;

using VmManager.Core.Models;

namespace VmManager.App;

public partial class VmSelectionDialog : Window {
    public VmSelectionDialog(string groupName, IReadOnlyList<VirtualMachine> availableVirtualMachines) {
        InitializeComponent();
        TitleTextBlock.Text = $"Add virtual machines to '{groupName}'";
        VmListBox.ItemsSource = availableVirtualMachines;
    }

    public IReadOnlyList<VirtualMachine> SelectedVirtualMachines =>
        VmListBox.SelectedItems.Cast<VirtualMachine>().ToList();

    private void Window_Loaded(object sender, RoutedEventArgs e) {
        DialogPlacement.CenterOverOwner(this);
    }

    private void VmListBox_SelectionChanged(object sender, SelectionChangedEventArgs e) {
        AddSelectedButton.IsEnabled = VmListBox.SelectedItems.Count > 0;
    }

    private void AddSelectedButton_Click(object sender, RoutedEventArgs e) {
        DialogResult = true;
    }
}
