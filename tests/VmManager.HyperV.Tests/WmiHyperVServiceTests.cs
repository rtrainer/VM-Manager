using VmManager.Core.Models;
using VmManager.HyperV;

namespace VmManager.HyperV.Tests;

public sealed class WmiHyperVServiceTests {
    [Fact]
    public async Task GetVirtualMachinesAsyncMapsAndSortsSummaries() {
        Guid runningId = Guid.NewGuid();
        Guid savedId = Guid.NewGuid();
        var gateway = new FakeHyperVWmiGateway {
            Summaries = [
                new WmiVirtualMachineSummary(savedId, "Zulu", 32769, 0, 1024),
                new WmiVirtualMachineSummary(runningId, "alpha", 2, 42, 512)
            ]
        };
        var service = new WmiHyperVService(gateway, TimeSpan.Zero);

        IReadOnlyList<VirtualMachine> virtualMachines = await service.GetVirtualMachinesAsync();

        Assert.Collection(
            virtualMachines,
            vm => {
                Assert.Equal(runningId, vm.Id);
                Assert.Equal("alpha", vm.Name);
                Assert.Equal(VirtualMachineState.Running, vm.State);
                Assert.Equal(42, vm.CpuUsage);
                Assert.Equal(512L * 1024L * 1024L, vm.MemoryAssignedBytes);
            },
            vm => {
                Assert.Equal(savedId, vm.Id);
                Assert.Equal("Zulu", vm.Name);
                Assert.Equal(VirtualMachineState.Saved, vm.State);
                Assert.Equal(1024L * 1024L * 1024L, vm.MemoryAssignedBytes);
            });
    }

    [Theory]
    [InlineData(0, VirtualMachineState.Unknown)]
    [InlineData(2, VirtualMachineState.Running)]
    [InlineData(3, VirtualMachineState.Off)]
    [InlineData(32768, VirtualMachineState.Paused)]
    [InlineData(32769, VirtualMachineState.Saved)]
    [InlineData(32770, VirtualMachineState.Starting)]
    [InlineData(32773, VirtualMachineState.Saved)]
    [InlineData(32774, VirtualMachineState.Stopping)]
    [InlineData(9999, VirtualMachineState.Other)]
    public async Task GetVirtualMachinesAsyncMapsEnabledState(ushort enabledState, VirtualMachineState expectedState) {
        var gateway = new FakeHyperVWmiGateway {
            Summaries = [
                new WmiVirtualMachineSummary(Guid.NewGuid(), "Test", enabledState, 0, 0)
            ]
        };
        var service = new WmiHyperVService(gateway, TimeSpan.Zero);

        IReadOnlyList<VirtualMachine> virtualMachines = await service.GetVirtualMachinesAsync();

        Assert.Equal(expectedState, virtualMachines.Single().State);
    }

    [Fact]
    public async Task StartAsyncRequestsEnabledStateAndWaitsForJobCompletion() {
        Guid vmId = Guid.NewGuid();
        var gateway = new FakeHyperVWmiGateway {
            StateChangeResult = new WmiMethodResult(4096, "job-1"),
            JobStatuses = [
                new WmiJobStatus(4, null),
                new WmiJobStatus(7, null)
            ]
        };
        var service = new WmiHyperVService(gateway, TimeSpan.Zero);

        await service.StartAsync(vmId);

        Assert.Equal((vmId, (ushort)2), gateway.RequestedStateChanges.Single());
        Assert.Equal(["job-1", "job-1"], gateway.RequestedJobs);
    }

    [Fact]
    public async Task TurnOffAsyncRequestsDisabledState() {
        Guid vmId = Guid.NewGuid();
        var gateway = new FakeHyperVWmiGateway {
            StateChangeResult = new WmiMethodResult(0, null)
        };
        var service = new WmiHyperVService(gateway, TimeSpan.Zero);

        await service.TurnOffAsync(vmId);

        Assert.Equal((vmId, (ushort)3), gateway.RequestedStateChanges.Single());
        Assert.Empty(gateway.RequestedJobs);
    }

    [Fact]
    public async Task ShutDownAsyncInitiatesGracefulGuestShutdown() {
        Guid vmId = Guid.NewGuid();
        var gateway = new FakeHyperVWmiGateway {
            ShutdownResult = new WmiMethodResult(0, null)
        };
        var service = new WmiHyperVService(gateway, TimeSpan.Zero);

        await service.ShutDownAsync(vmId);

        Assert.Equal(vmId, gateway.ShutdownVmId);
        Assert.False(gateway.ShutdownForce);
        Assert.Equal("VM Manager requested a guest shutdown.", gateway.ShutdownReason);
    }

    [Fact]
    public async Task ShutDownAsyncTreatsCompletedResultWithoutJobAsSuccess() {
        var gateway = new FakeHyperVWmiGateway {
            ShutdownResult = new WmiMethodResult(0, null)
        };
        var service = new WmiHyperVService(gateway, TimeSpan.Zero);

        await service.ShutDownAsync(Guid.NewGuid());

        Assert.Empty(gateway.RequestedJobs);
    }

    [Fact]
    public async Task StartAsyncThrowsWhenStateChangeFailsImmediately() {
        var gateway = new FakeHyperVWmiGateway {
            StateChangeResult = new WmiMethodResult(32775, null)
        };
        var service = new WmiHyperVService(gateway, TimeSpan.Zero);

        HyperVException exception = await Assert.ThrowsAsync<HyperVException>(() =>
            service.StartAsync(Guid.NewGuid()));

        Assert.Contains("Return value: 32775", exception.Message, StringComparison.Ordinal);
    }

    [Fact]
    public async Task StartAsyncThrowsWhenTrackedJobFails() {
        var gateway = new FakeHyperVWmiGateway {
            StateChangeResult = new WmiMethodResult(4096, "job-1"),
            JobStatuses = [
                new WmiJobStatus(10, "The VM could not be started.")
            ]
        };
        var service = new WmiHyperVService(gateway, TimeSpan.Zero);

        HyperVException exception = await Assert.ThrowsAsync<HyperVException>(() =>
            service.StartAsync(Guid.NewGuid()));

        Assert.Contains("The VM could not be started.", exception.Message, StringComparison.Ordinal);
    }

    private sealed class FakeHyperVWmiGateway : IHyperVWmiGateway {
        private int _jobStatusIndex;

        public IReadOnlyList<WmiVirtualMachineSummary> Summaries { get; init; } = [];

        public WmiMethodResult StateChangeResult { get; init; } = new(0, null);

        public WmiMethodResult ShutdownResult { get; init; } = new(0, null);

        public IReadOnlyList<WmiJobStatus> JobStatuses { get; init; } = [];

        public List<(Guid VmId, ushort RequestedState)> RequestedStateChanges { get; } = [];

        public List<string> RequestedJobs { get; } = [];

        public Guid? ShutdownVmId { get; private set; }

        public bool? ShutdownForce { get; private set; }

        public string? ShutdownReason { get; private set; }

        public IReadOnlyList<WmiVirtualMachineSummary> GetVirtualMachineSummaries(CancellationToken cancellationToken) =>
            Summaries;

        public WmiMethodResult RequestStateChange(Guid vmId, ushort requestedState, CancellationToken cancellationToken) {
            RequestedStateChanges.Add((vmId, requestedState));
            return StateChangeResult;
        }

        public WmiMethodResult InitiateGuestShutdown(
            Guid vmId,
            bool force,
            string reason,
            CancellationToken cancellationToken) {
            ShutdownVmId = vmId;
            ShutdownForce = force;
            ShutdownReason = reason;
            return ShutdownResult;
        }

        public WmiJobStatus GetJobStatus(string jobPath, CancellationToken cancellationToken) {
            RequestedJobs.Add(jobPath);
            if (_jobStatusIndex >= JobStatuses.Count) {
                return new WmiJobStatus(7, null);
            }

            return JobStatuses[_jobStatusIndex++];
        }
    }
}
