namespace VmManager.Core.Models;

public enum VirtualMachineState {
    Unknown,
    Off,
    Running,
    Paused,
    Saved,
    Starting,
    Stopping,
    Other
}
