using ChessDesktopApp.Engine;
using ChessDesktopApp.Models;
using ChessDesktopApp.Storage;

namespace ChessDesktopApp.Services;

public sealed class EngineManager(EngineRepository repository)
{
    public async Task<List<EngineDefinition>> LoadAsync(CancellationToken cancellationToken = default)
    {
        var engines = await repository.GetAllAsync(cancellationToken);
        var bundledPath = StockfishPathResolver.Resolve();
        var bundled = engines.FirstOrDefault(item => item.IsBundled ||
            (item.Name.Equals("Stockfish", StringComparison.OrdinalIgnoreCase) &&
             Path.GetFileName(item.ExecutablePath).StartsWith("stockfish-windows", StringComparison.OrdinalIgnoreCase)));
        if (File.Exists(bundledPath) && bundled is not null)
        {
            bundled.ExecutablePath = bundledPath;
            bundled.WorkingDirectory = Path.GetDirectoryName(bundledPath);
            bundled.Status = "Dostępny";
            bundled.IsBundled = true;
            await repository.SaveAllAsync(engines, cancellationToken);
        }
        else if (File.Exists(bundledPath) && engines.All(item => !PathEquals(item.ExecutablePath, bundledPath)))
        {
            engines.Insert(0, new EngineDefinition
            {
                Name = "Stockfish",
                ExecutablePath = bundledPath,
                WorkingDirectory = Path.GetDirectoryName(bundledPath),
                Status = "Dostępny",
                IsBundled = true
            });
            await repository.SaveAllAsync(engines, cancellationToken);
        }
        return engines;
    }

    public IChessEngine Create(EngineDefinition definition)
    {
        if (!File.Exists(definition.ExecutablePath))
        {
            throw new ChessEngineException($"Plik silnika '{definition.Name}' nie istnieje: {definition.ExecutablePath}");
        }
        return new StockfishClient(definition.ExecutablePath, definition.UciOptions);
    }

    public async Task<EngineDefinition> AddAsync(string executablePath, CancellationToken cancellationToken = default)
    {
        var definition = new EngineDefinition
        {
            Name = Path.GetFileNameWithoutExtension(executablePath),
            ExecutablePath = Path.GetFullPath(executablePath),
            WorkingDirectory = Path.GetDirectoryName(executablePath)
        };
        var validation = await ValidateAsync(definition, cancellationToken);
        definition.Name = validation.Name;
        definition.Status = validation.IsValid ? "Gotowy" : validation.Message;
        definition.LastValidatedAt = DateTimeOffset.Now;
        var engines = await LoadAsync(cancellationToken);
        engines.RemoveAll(item => PathEquals(item.ExecutablePath, definition.ExecutablePath));
        engines.Add(definition);
        await repository.SaveAllAsync(engines, cancellationToken);
        return definition;
    }

    public async Task RemoveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var engines = await LoadAsync(cancellationToken);
        engines.RemoveAll(item => item.Id == id);
        await repository.SaveAllAsync(engines, cancellationToken);
    }

    public async Task<EngineValidationResult> ValidateAsync(EngineDefinition definition, CancellationToken cancellationToken = default)
    {
        if (!File.Exists(definition.ExecutablePath))
            return new(false, definition.Name, "Plik został przeniesiony lub usunięty");
        try
        {
            await using var engine = Create(definition);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeout.CancelAfter(TimeSpan.FromSeconds(12));
            await engine.InitializeAsync(timeout.Token);
            return new(true, engine.Name, "Handshake UCI zakończony poprawnie");
        }
        catch (Exception exception) when (exception is ChessEngineException or OperationCanceledException or IOException)
        {
            var hint = Path.GetFileName(definition.ExecutablePath).Contains("lc0", StringComparison.OrdinalIgnoreCase)
                ? " Sprawdź również plik sieci weights Lc0 i jego katalog roboczy."
                : string.Empty;
            return new(false, definition.Name, exception.Message + hint);
        }
    }

    public Task SaveAsync(IEnumerable<EngineDefinition> engines, CancellationToken cancellationToken = default) =>
        repository.SaveAllAsync(engines, cancellationToken);

    private static bool PathEquals(string left, string right) =>
        string.Equals(Path.GetFullPath(left), Path.GetFullPath(right), StringComparison.OrdinalIgnoreCase);
}
