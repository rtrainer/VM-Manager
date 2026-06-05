using System.Windows;

namespace VmManager.App;

public partial class GroupNameDialog : Window {
    public GroupNameDialog() {
        InitializeComponent();
        Loaded += (_, _) => GroupNameTextBox.Focus();
    }

    public string GroupName => GroupNameTextBox.Text;

    private void Window_Loaded(object sender, RoutedEventArgs e) {
        DialogPlacement.CenterOverOwner(this);
    }

    private void CreateButton_Click(object sender, RoutedEventArgs e) {
        if (string.IsNullOrWhiteSpace(GroupName)) {
            MessageDialog.Show(Owner ?? this, "Enter a group name.", "VM Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
