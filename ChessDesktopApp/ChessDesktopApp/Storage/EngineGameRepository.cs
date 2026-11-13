using ChessDesktopApp.Models;

namespace ChessDesktopApp.Storage;

public sealed class EngineGameRepository(JsonFileStore store)
{
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<List<SavedEngineGame>> GetAllAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            return await LoadAllCoreAsync(cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task AddAsync(SavedEngineGame game, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Normalize(game);
            await store.SaveAsync(GamePath(game.Id), game, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task DeleteAsync(Guid gameId, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            var path = GamePath(gameId);
            if (File.Exists(path)) File.Delete(path);

            // Usuwamy wpis również ze starego zbiorczego pliku, aby migracja nie
            // przywróciła skasowanej partii przy kolejnym uruchomieniu.
            var legacy = await store.LoadListAsync<SavedEngineGame>(AppDataPaths.GamesFile, cancellationToken);
            if (legacy.RemoveAll(item => item.Id == gameId) > 0)
                await store.SaveListAsync(AppDataPaths.GamesFile, legacy, cancellationToken);
        }
        finally
        {
            _gate.Release();
        }
    }

    private async Task<List<SavedEngineGame>> LoadAllCoreAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(AppDataPaths.GamesDirectory);
        var byId = new Dictionary<Guid, SavedEngineGame>();
        foreach (var path in Directory.EnumerateFiles(AppDataPaths.GamesDirectory, "*.json"))
        {
            cancellationToken.ThrowIfCancellationRequested();
            var game = await store.LoadAsync<SavedEngineGame>(path, cancellationToken);
            if (game is null || game.Id == Guid.Empty) continue;
            if (Normalize(game)) await store.SaveAsync(path, game, cancellationToken);
            byId[game.Id] = game;
        }

        // Jednorazowa, bezstratna migracja dotychczasowego engine-games.json.
        // Po migracji widok i powtórki korzystają już z osobnych plików partii.
        var legacyGames = await store.LoadListAsync<SavedEngineGame>(AppDataPaths.GamesFile, cancellationToken);
        foreach (var game in legacyGames)
        {
            if (game.Id == Guid.Empty || byId.ContainsKey(game.Id)) continue;
            Normalize(game);
            await store.SaveAsync(GamePath(game.Id), game, cancellationToken);
            byId[game.Id] = game;
        }

        return byId.Values
            .OrderByDescending(item => item.FinishedAt)
            .ThenByDescending(item => item.StartedAt)
            .ToList();
    }

    private static string GamePath(Guid gameId) =>
        Path.Combine(AppDataPaths.GamesDirectory, $"{gameId:N}.json");

    private static bool Normalize(SavedEngineGame game)
    {
        var changed = false;
        if (game.SchemaVersion != 1) { game.SchemaVersion = 1; changed = true; }
        if (string.IsNullOrWhiteSpace(game.WhiteEngine)) { game.WhiteEngine = "Białe"; changed = true; }
        if (string.IsNullOrWhiteSpace(game.BlackEngine)) { game.BlackEngine = "Czarne"; changed = true; }
        if (string.IsNullOrWhiteSpace(game.FormatDescription))
        {
            game.FormatDescription = game.InitialTimeSeconds > 0
                ? $"{game.InitialTimeSeconds / 60d:0.##} min + {game.IncrementSeconds} s"
                : game.IsPlayerGame ? "Gracz kontra silnik" : "Bez kontroli czasu";
            changed = true;
        }
        if (game.UciMoves is null) { game.UciMoves = []; changed = true; }
        if (game.Analysis is null) { game.Analysis = []; changed = true; }
        return changed;
    }
}
