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
        Assert.True(settings.StartAtLogin);
    }

    [Fact]
    public async Task SaveAndLoadRoundTripsStartMinimized() {
        var repository = new JsonAppSettingsRepository(Path.Combine(_directory, "settings.json"));

        await repository.SaveAsync(new AppSettings(StartMinimized: true));
        AppSettings settings = await repository.LoadAsync();

        Assert.True(settings.StartMinimized);
    }

    [Fact]
    public async Task SaveAndLoadRoundTripsStartAtLogin() {
        var repository = new JsonAppSettingsRepository(Path.Combine(_directory, "settings.json"));

        await repository.SaveAsync(new AppSettings(StartAtLogin: false));
        AppSettings settings = await repository.LoadAsync();

        Assert.False(settings.StartAtLogin);
    }

    [Fact]
    public async Task LoadUsesStartAtLoginDefaultWhenExistingSettingsDoNotContainIt() {
        Directory.CreateDirectory(_directory);
        string settingsPath = Path.Combine(_directory, "settings.json");
        await File.WriteAllTextAsync(settingsPath, """
            {
              "StartMinimized": true,
              "AutoUpdateEnabled": false
            }
            """);
        var repository = new JsonAppSettingsRepository(settingsPath);

        AppSettings settings = await repository.LoadAsync();

        Assert.True(settings.StartMinimized);
        Assert.True(settings.StartAtLogin);
    }

    public void Dispose() {
        if (Directory.Exists(_directory)) {
            Directory.Delete(_directory, true);
        }
    }
}
