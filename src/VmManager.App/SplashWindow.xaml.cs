using System.Windows;

namespace VmManager.App;

public partial class SplashWindow : Window {
    public SplashWindow() {
        InitializeComponent();
    }

    public void SetStatus(string status) {
        StatusTextBlock.Text = status;
    }
}
