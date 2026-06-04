using VmManager.Core.Models;

namespace VmManager.Core.Services;

public sealed class VmGroupCatalog(IVmGroupRepository repository) {
    private readonly SemaphoreSlim _gate = new(1, 1);
    private List<VmGroup> _groups = [];

    public IReadOnlyList<VmGroup> Groups => _groups
        .OrderBy(group => group.SortOrder)
        .ThenBy(group => group.Name, StringComparer.OrdinalIgnoreCase)
        .ToList();

    public async Task InitializeAsync(CancellationToken cancellationToken = default) {
        await _gate.WaitAsync(cancellationToken);
        try {
            _groups = (await repository.LoadAsync(cancellationToken)).ToList();
        } finally {
            _gate.Release();
        }
    }

    public async Task<VmGroup> CreateAsync(string name, CancellationToken cancellationToken = default) {
        var normalizedName = name.Trim();
        if (normalizedName.Length == 0) {
            throw new ArgumentException("A group name is required.", nameof(name));
        }

        return await MutateAsync(
            groups => {
                if (groups.Any(group => string.Equals(group.Name, normalizedName, StringComparison.OrdinalIgnoreCase))) {
                    throw new InvalidOperationException($"A group named '{normalizedName}' already exists.");
                }

                var group = new VmGroup(Guid.NewGuid(), normalizedName, groups.Count, []);
                groups.Add(group);
                return group;
            },
            cancellationToken);
    }

    public Task DeleteAsync(Guid groupId, CancellationToken cancellationToken = default) =>
        MutateAsync<object?>(
            groups => {
                groups.RemoveAll(group => group.Id == groupId);
                return null;
            },
            cancellationToken);

    public Task AddVmAsync(Guid groupId, Guid vmId, CancellationToken cancellationToken = default) =>
        MutateAsync<object?>(
            groups => {
                var index = FindGroupIndex(groups, groupId);
                VmGroup group = groups[index];
                if (!group.VmIds.Contains(vmId)) {
                    groups[index] = group with { VmIds = group.VmIds.Append(vmId).ToList() };
                }

                return null;
            },
            cancellationToken);

    public Task RemoveVmAsync(Guid groupId, Guid vmId, CancellationToken cancellationToken = default) =>
        MutateAsync<object?>(
            groups => {
                var index = FindGroupIndex(groups, groupId);
                VmGroup group = groups[index];
                groups[index] = group with { VmIds = group.VmIds.Where(id => id != vmId).ToList() };
                return null;
            },
            cancellationToken);

    private async Task<T> MutateAsync<T>(Func<List<VmGroup>, T> mutation, CancellationToken cancellationToken) {
        await _gate.WaitAsync(cancellationToken);
        try {
            T? result = mutation(_groups);
            await repository.SaveAsync(_groups, cancellationToken);
            return result;
        } finally {
            _gate.Release();
        }
    }

    private static int FindGroupIndex(List<VmGroup> groups, Guid groupId) {
        var index = groups.FindIndex(group => group.Id == groupId);
        return index >= 0 ? index : throw new KeyNotFoundException("The selected VM group no longer exists.");
    }
}
