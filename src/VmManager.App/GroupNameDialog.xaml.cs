using System.Windows;

using MessageBox = System.Windows.MessageBox;

namespace VmManager.App;

public partial class GroupNameDialog : Window {
    public GroupNameDialog() {
        InitializeComponent();
        Loaded += (_, _) => GroupNameTextBox.Focus();
    }

    public string GroupName => GroupNameTextBox.Text;

    private void CreateButton_Click(object sender, RoutedEventArgs e) {
        if (string.IsNullOrWhiteSpace(GroupName)) {
            MessageBox.Show("Enter a group name.", "VM Manager", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        DialogResult = true;
    }
}
