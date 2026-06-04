using System.Text.Json;

using VmManager.Core.Models;

namespace VmManager.Core.Services;

public sealed class JsonVmGroupRepository(string filePath) : IVmGroupRepository {
    private static readonly JsonSerializerOptions SerializerOptions = new() {
        WriteIndented = true
    };

    public async Task<IReadOnlyList<VmGroup>> LoadAsync(CancellationToken cancellationToken = default) {
        if (!File.Exists(filePath)) {
            return [];
        }

        await using FileStream stream = File.OpenRead(filePath);
        return await JsonSerializer.DeserializeAsync<List<VmGroup>>(stream, SerializerOptions, cancellationToken)
            ?? [];
    }

    public async Task SaveAsync(IReadOnlyList<VmGroup> groups, CancellationToken cancellationToken = default) {
        var directory = Path.GetDirectoryName(filePath);
        if (!string.IsNullOrWhiteSpace(directory)) {
            Directory.CreateDirectory(directory);
        }

        var temporaryPath = $"{filePath}.tmp";
        await using (FileStream stream = File.Create(temporaryPath)) {
            await JsonSerializer.SerializeAsync(stream, groups, SerializerOptions, cancellationToken);
        }

        File.Move(temporaryPath, filePath, true);
    }
}
