using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;
using System.Runtime.InteropServices;
using WindowsPoint = System.Windows.Point;

namespace VmManager.App;

internal static class DialogPlacement {
    public static void CenterOverOwner(Window dialog) {
        Window? owner = dialog.Owner;
        if (owner is null || !owner.IsVisible) {
            dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
            return;
        }

        dialog.UpdateLayout();
        Rect ownerBounds = GetOwnerBounds(owner);
        double dialogWidth = GetDialogDimension(dialog.ActualWidth, dialog.Width);
        double dialogHeight = GetDialogDimension(dialog.ActualHeight, dialog.Height);

        dialog.Left = ownerBounds.Left + ((ownerBounds.Width - dialogWidth) / 2);
        dialog.Top = ownerBounds.Top + ((ownerBounds.Height - dialogHeight) / 2);
    }

    private static double GetDialogDimension(double actualValue, double configuredValue) =>
        actualValue > 0
            ? actualValue
            : double.IsNaN(configuredValue) ? 0 : configuredValue;

    private static Rect GetOwnerBounds(Window owner) {
        var helper = new WindowInteropHelper(owner);
        if (helper.Handle != IntPtr.Zero && GetWindowRect(helper.Handle, out NativeRect rect)) {
            WindowsPoint topLeft = new(rect.Left, rect.Top);
            WindowsPoint bottomRight = new(rect.Right, rect.Bottom);
            PresentationSource? source = PresentationSource.FromVisual(owner);
            if (source?.CompositionTarget is not null) {
                Matrix transform = source.CompositionTarget.TransformFromDevice;
                topLeft = transform.Transform(topLeft);
                bottomRight = transform.Transform(bottomRight);
            }

            return new Rect(topLeft, bottomRight);
        }

        return new Rect(owner.Left, owner.Top, owner.ActualWidth, owner.ActualHeight);
    }

    [DllImport("user32.dll")]
    private static extern bool GetWindowRect(IntPtr hwnd, out NativeRect rect);

    [StructLayout(LayoutKind.Sequential)]
    private readonly struct NativeRect {
        public readonly int Left;
        public readonly int Top;
        public readonly int Right;
        public readonly int Bottom;
    }
}
