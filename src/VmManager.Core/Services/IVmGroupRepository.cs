using VmManager.Core.Models;

namespace VmManager.Core.Services;

public interface IVmGroupRepository {
    Task<IReadOnlyList<VmGroup>> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(IReadOnlyList<VmGroup> groups, CancellationToken cancellationToken = default);
}
