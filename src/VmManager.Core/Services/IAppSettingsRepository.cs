using VmManager.Core.Models;

namespace VmManager.Core.Services;

public interface IAppSettingsRepository {
    Task<AppSettings> LoadAsync(CancellationToken cancellationToken = default);

    Task SaveAsync(AppSettings settings, CancellationToken cancellationToken = default);
}
