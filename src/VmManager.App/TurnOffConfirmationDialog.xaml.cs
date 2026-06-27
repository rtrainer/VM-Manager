using System.Windows;

namespace VmManager.App;

public partial class TurnOffConfirmationDialog : Window {
    public TurnOffConfirmationDialog(string targetName, bool multiple = false) {
        InitializeComponent();
        PromptTextBlock.Text = multiple
            ? "Turn off these virtual machines?"
            : "Turn off this virtual machine?";
        MessageTextBlock.Text = multiple
            ? $"Turning off {targetName} immediately is equivalent to disconnecting their power."
            : $"Turning off '{targetName}' immediately is equivalent to disconnecting its power.";
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) {
        DialogPlacement.CenterOverOwner(this);
    }

    private void TurnOffButton_Click(object sender, RoutedEventArgs e) {
        DialogResult = true;
    }
}
