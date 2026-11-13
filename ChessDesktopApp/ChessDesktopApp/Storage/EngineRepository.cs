using ChessDesktopApp.Models;

namespace ChessDesktopApp.Storage;

public sealed class EngineRepository(JsonFileStore store)
{
    public async Task<List<EngineDefinition>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        AppDataPaths.EnsureCreated();
        return await store.LoadListAsync<EngineDefinition>(AppDataPaths.EngineConfigurationFile, cancellationToken);
    }

    public Task SaveAllAsync(IEnumerable<EngineDefinition> engines, CancellationToken cancellationToken = default) =>
        store.SaveListAsync(AppDataPaths.EngineConfigurationFile, engines, cancellationToken);
}
