using System.Text.Json;

using VmManager.Core.Models;

namespace VmManager.Core.Services;

public sealed class JsonAppSettingsRepository(string filePath) : IAppSettingsRepository {
    private static readonly JsonSerializerOptions SerializerOptions = new() {
        WriteIndented = true
    };

    public async Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default) {
        if (!File.Exists(filePath)) {
            return new AppSettings();
        }

        await using FileStream stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<AppSettings>(stream, SerializerOptions, cancellationToken)
            ?? new AppSettings();
    }

    public async Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default) {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{filePath}.tmp";
        await using (FileStream stream = File.Create(temporaryPath)) {
            await JsonSerializer.SerializeAsync(stream, settings, SerializerOptions, cancellationToken);
        }

        File.Move(temporaryPath, filePath, true);
    }
}
