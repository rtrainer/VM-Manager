using System.Windows;

namespace VmManager.App;

public partial class TurnOffConfirmationDialog : Window {
    public TurnOffConfirmationDialog(string vmName) {
        InitializeComponent();
        MessageTextBlock.Text = $"Turning off '{vmName}' immediately is equivalent to disconnecting its power.";
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) {
        if (Owner is null) {
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        Left = Owner.Left + ((Owner.ActualWidth - ActualWidth) / 2);
        Top = Owner.Top + ((Owner.ActualHeight - ActualHeight) / 2);
    }

    private void TurnOffButton_Click(object sender, RoutedEventArgs e) {
        DialogResult = true;
    }
}
