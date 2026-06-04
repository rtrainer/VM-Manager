using VmManager.Core.Models;
using VmManager.Core.Services;

namespace VmManager.Core.Tests;

public sealed class JsonAppSettingsRepositoryTests : IDisposable {
    private readonly string _directory = Path.Combine(Path.GetTempPath(), $"VmManagerTests-{Guid.NewGuid():N}");

    [Fact]
    public async Task LoadReturnsDefaultsWhenSettingsDoNotExist() {
        var repository = new JsonAppSettingsRepository(Path.Combine(_directory, "settings.json"));

        AppSettings settings = await repository.LoadAsync();

        Assert.False(settings.StartMinimized);
    }

    [Fact]
    public async Task SaveAndLoadRoundTripsStartMinimized() {
        var repository = new JsonAppSettingsRepository(Path.Combine(_directory, "settings.json"));

        await repository.SaveAsync(new AppSettings(StartMinimized: true));
        AppSettings settings = await repository.LoadAsync();

        Assert.True(settings.StartMinimized);
    }

    public void Dispose() {
        if (Directory.Exists(_directory)) {
            Directory.Delete(_directory, true);
        }
    }
}
