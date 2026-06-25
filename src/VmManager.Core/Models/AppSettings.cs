namespace VmManager.Core.Models;

public sealed record AppSettings(
    bool StartMinimized = false,
    bool AutoUpdateEnabled = false,
    bool StartAtLogin = true);
