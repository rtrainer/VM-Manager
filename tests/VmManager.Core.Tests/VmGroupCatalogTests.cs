using VmManager.Core.Models;
using VmManager.Core.Services;

namespace VmManager.Core.Tests;

public sealed class VmGroupCatalogTests {
    [Fact]
    public async Task CreateAndAddVmPersistsMembership() {
        var repository = new InMemoryRepository();
        var catalog = new VmGroupCatalog(repository);
        await catalog.InitializeAsync();

        VmGroup group = await catalog.CreateAsync("Development");
        var vmId = Guid.NewGuid();
        await catalog.AddVmAsync(group.Id, vmId);

        VmGroup savedGroup = Assert.Single(repository.SavedGroups);
        Assert.Equal("Development", savedGroup.Name);
        Assert.Equal(vmId, Assert.Single(savedGroup.VmIds));
    }

    [Fact]
    public async Task CreateRejectsDuplicateNameIgnoringCase() {
        var catalog = new VmGroupCatalog(new InMemoryRepository());
        await catalog.InitializeAsync();
        await catalog.CreateAsync("Development");

        await Assert.ThrowsAsync<InvalidOperationException>(() => catalog.CreateAsync("development"));
    }

    [Fact]
    public async Task RemoveVmPreservesOtherMemberships() {
        var firstVmId = Guid.NewGuid();
        var secondVmId = Guid.NewGuid();
        var group = new VmGroup(Guid.NewGuid(), "Development", 0, [firstVmId, secondVmId]);
        var repository = new InMemoryRepository([group]);
        var catalog = new VmGroupCatalog(repository);
        await catalog.InitializeAsync();

        await catalog.RemoveVmAsync(group.Id, firstVmId);

        Assert.Equal(secondVmId, Assert.Single(Assert.Single(repository.SavedGroups).VmIds));
    }

    private sealed class InMemoryRepository(IReadOnlyList<VmGroup>? initialGroups = null) : IVmGroupRepository {
        public IReadOnlyList<VmGroup> SavedGroups { get; private set; } = initialGroups ?? [];

        public Task<IReadOnlyList<VmGroup>> LoadAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(SavedGroups);

        public Task SaveAsync(IReadOnlyList<VmGroup> groups, CancellationToken cancellationToken = default) {
            SavedGroups = groups
                .Select(group => group with { VmIds = group.VmIds.ToList() })
                .ToList();
            return Task.CompletedTask;
        }
    }
}
