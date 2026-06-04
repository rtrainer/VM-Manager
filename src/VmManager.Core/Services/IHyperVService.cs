using VmManager.Core.Models;

namespace VmManager.Core.Services;

public interface IHyperVService {
    Task<IReadOnlyList<VirtualMachine>> GetVirtualMachinesAsync(CancellationToken cancellationToken = default);

    Task StartAsync(Guid vmId, CancellationToken cancellationToken = default);

    Task ShutDownAsync(Guid vmId, CancellationToken cancellationToken = default);

    Task TurnOffAsync(Guid vmId, CancellationToken cancellationToken = default);
}
