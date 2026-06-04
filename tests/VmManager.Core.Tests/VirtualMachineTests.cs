using VmManager.Core.Models;

namespace VmManager.Core.Tests;

public sealed class VirtualMachineTests {
    [Theory]
    [InlineData(VirtualMachineState.Off, true, false)]
    [InlineData(VirtualMachineState.Saved, true, true)]
    [InlineData(VirtualMachineState.Running, false, true)]
    [InlineData(VirtualMachineState.Unknown, false, false)]
    public void StateDeterminesAvailableActions(
        VirtualMachineState state,
        bool canStart,
        bool canStop) {
        var vm = new VirtualMachine(Guid.NewGuid(), "Test", state, 0, 0);

        Assert.Equal(canStart, vm.CanStart);
        Assert.Equal(canStop, vm.CanStop);
    }
}
