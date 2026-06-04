namespace VmManager.Core.Models;

public sealed record VirtualMachine(
    Guid Id,
    string Name,
    VirtualMachineState State,
    int CpuUsage,
    long MemoryAssignedBytes) {
    public bool CanStart => State is VirtualMachineState.Off or VirtualMachineState.Saved;

    public bool CanStop => State is not VirtualMachineState.Off and not VirtualMachineState.Unknown;
}
