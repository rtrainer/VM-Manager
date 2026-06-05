using System.Windows;

namespace VmManager.App;

public partial class TurnOffConfirmationDialog : Window {
    public TurnOffConfirmationDialog(string vmName) {
        InitializeComponent();
        MessageTextBlock.Text = $"Turning off '{vmName}' immediately is equivalent to disconnecting its power.";
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) {
        DialogPlacement.CenterOverOwner(this);
    }

    private void TurnOffButton_Click(object sender, RoutedEventArgs e) {
        DialogResult = true;
    }
}
