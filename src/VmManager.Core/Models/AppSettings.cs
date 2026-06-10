namespace VmManager.Core.Models;

public sealed record AppSettings(
    bool StartMinimized = false,
    bool AutoUpdateEnabled = false);
