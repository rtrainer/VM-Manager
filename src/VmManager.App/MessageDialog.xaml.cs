using System.Windows;
using MediaBrushes = System.Windows.Media.Brushes;

namespace VmManager.App;

public partial class MessageDialog : Window {
    private MessageBoxResult _primaryResult;
    private MessageBoxResult _secondaryResult;

    public MessageDialog(
        string message,
        string caption,
        MessageBoxButton buttons,
        MessageBoxImage image) {
        InitializeComponent();
        Title = caption;
        MessageTextBlock.Text = message;
        ConfigureButtons(buttons);
        ConfigureIcon(image);
    }

    public MessageBoxResult Result { get; private set; } = MessageBoxResult.None;

    public static MessageBoxResult Show(
        Window? owner,
        string message,
        string caption,
        MessageBoxButton buttons = MessageBoxButton.OK,
        MessageBoxImage image = MessageBoxImage.None) {
        var dialog = new MessageDialog(message, caption, buttons, image);
        if (owner is not null) {
            dialog.Owner = owner;
        }

        return dialog.ShowDialog() == true ? dialog.Result : dialog._secondaryResult;
    }

    private void ConfigureButtons(MessageBoxButton buttons) {
        switch (buttons) {
            case MessageBoxButton.OK:
                _primaryResult = MessageBoxResult.OK;
                _secondaryResult = MessageBoxResult.OK;
                PrimaryButton.Content = "OK";
                SecondaryButton.Visibility = Visibility.Collapsed;
                break;
            case MessageBoxButton.YesNo:
                _primaryResult = MessageBoxResult.Yes;
                _secondaryResult = MessageBoxResult.No;
                PrimaryButton.Content = "Yes";
                SecondaryButton.Content = "No";
                SecondaryButton.IsCancel = true;
                break;
            default:
                throw new NotSupportedException($"{buttons} is not supported by {nameof(MessageDialog)}.");
        }
    }

    private void ConfigureIcon(MessageBoxImage image) {
        switch (image) {
            case MessageBoxImage.Error:
                IconTextBlock.Text = "\uE783";
                IconTextBlock.Foreground = MediaBrushes.Firebrick;
                break;
            case MessageBoxImage.Warning:
                IconTextBlock.Text = "\uE7BA";
                IconTextBlock.Foreground = MediaBrushes.DarkOrange;
                break;
            case MessageBoxImage.Question:
                IconTextBlock.Text = "\uE897";
                IconTextBlock.Foreground = MediaBrushes.SteelBlue;
                break;
            default:
                IconTextBlock.Text = "\uE946";
                IconTextBlock.Foreground = MediaBrushes.SteelBlue;
                break;
        }
    }

    private void Window_Loaded(object sender, RoutedEventArgs e) {
        DialogPlacement.CenterOverOwner(this);
    }

    private void PrimaryButton_Click(object sender, RoutedEventArgs e) {
        Result = _primaryResult;
        DialogResult = true;
    }

    private void SecondaryButton_Click(object sender, RoutedEventArgs e) {
        Result = _secondaryResult;
        DialogResult = false;
    }
}
