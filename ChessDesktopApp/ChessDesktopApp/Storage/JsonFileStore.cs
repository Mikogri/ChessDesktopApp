using System.Text.Json;

namespace ChessDesktopApp.Storage;

public sealed class JsonFileStore
{
    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        PropertyNameCaseInsensitive = true
    };
    private readonly SemaphoreSlim _gate = new(1, 1);

    public async Task<T?> LoadAsync<T>(string path, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            if (!File.Exists(path)) return default;
            await using var stream = File.OpenRead(path);
            return await JsonSerializer.DeserializeAsync<T>(stream, Options, cancellationToken);
        }
        catch (JsonException)
        {
            var backup = path + ".invalid-" + DateTime.Now.ToString("yyyyMMddHHmmss");
            File.Copy(path, backup, overwrite: true);
            return default;
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task SaveAsync<T>(string path, T value, CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken);
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            var temporaryPath = path + ".tmp";
            await using (var stream = File.Create(temporaryPath))
            {
                await JsonSerializer.SerializeAsync(stream, value, Options, cancellationToken);
            }
            File.Move(temporaryPath, path, overwrite: true);
        }
        finally
        {
            _gate.Release();
        }
    }

    public async Task<List<T>> LoadListAsync<T>(string path, CancellationToken cancellationToken = default) =>
        await LoadAsync<List<T>>(path, cancellationToken) ?? [];

    public Task SaveListAsync<T>(string path, IEnumerable<T> values, CancellationToken cancellationToken = default) =>
        SaveAsync(path, values, cancellationToken);
}
