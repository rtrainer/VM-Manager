using System.IO;

using Microsoft.Win32;

namespace VmManager.App;

public sealed class LoginStartupService {
    private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
    private const string ValueName = "LittleBitsSoftware.VmManager";

    public void SetEnabled(bool enabled) {
        if (enabled) {
            using RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath, true)
                ?? throw new InvalidOperationException("Unable to open the Windows startup registry key.");
            key.SetValue(ValueName, GetStartupCommand(), RegistryValueKind.String);
            return;
        }

        using RegistryKey? runKey = Registry.CurrentUser.OpenSubKey(RunKeyPath, true);
        runKey?.DeleteValue(ValueName, false);
    }

    private static string GetStartupCommand() {
        string? executablePath = Environment.ProcessPath;
        if (string.IsNullOrWhiteSpace(executablePath)) {
            throw new InvalidOperationException("Unable to determine the VM Manager executable path.");
        }

        return $"\"{GetVelopackStubPath(executablePath) ?? executablePath}\"";
    }

    private static string? GetVelopackStubPath(string executablePath) {
        string? executableDirectory = Path.GetDirectoryName(executablePath);
        if (string.IsNullOrWhiteSpace(executableDirectory)
            || !string.Equals(Path.GetFileName(executableDirectory), "current", StringComparison.OrdinalIgnoreCase)) {
            return null;
        }

        string? installDirectory = Directory.GetParent(executableDirectory)?.FullName;
        if (string.IsNullOrWhiteSpace(installDirectory)) {
            return null;
        }

        string stubPath = Path.Combine(installDirectory, Path.GetFileName(executablePath));
        return File.Exists(stubPath) ? stubPath : null;
    }
}
