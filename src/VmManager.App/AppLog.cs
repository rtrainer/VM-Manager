using System.IO;

namespace VmManager.App;

internal static class AppLog {
    private static readonly string LogPath = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "VmManager",
        "vm-manager.log");

    public static void Write(string message) {
        try {
            Directory.CreateDirectory(Path.GetDirectoryName(LogPath)!);
            File.AppendAllText(LogPath, $"{DateTimeOffset.Now:O} {message}{Environment.NewLine}");
        } catch {
            // Logging must never prevent the tray application from starting.
        }
    }

    public static void Write(Exception exception) => Write(exception.ToString().ReplaceLineEndings(" "));
}
