using Chess.Core;
using ChessDesktopApp.Engine;
using ChessDesktopApp.Models;
using ChessDesktopApp.Storage;
using Xunit;

namespace Chess.App.Tests;

public sealed class EngineInfrastructureTests
{
    [Fact]
    public async Task JsonStore_ListAndGenericMethodsUseTheSameFormat()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FluentChessTests", Guid.NewGuid().ToString("N"));
        var path = Path.Combine(directory, "values.json");
        try
        {
            var store = new JsonFileStore();
            Assert.Empty(await store.LoadListAsync<int>(path));

            await store.SaveListAsync(path, new[] { 1, 2, 3 });
            Assert.Equal(new[] { 1, 2, 3 }, await store.LoadAsync<List<int>>(path));

            await store.SaveAsync(path, new[] { 4, 5 });
            Assert.Equal(new[] { 4, 5 }, await store.LoadListAsync<int>(path));
        }
        finally
        {
            if (Directory.Exists(directory)) Directory.Delete(directory, recursive: true);
        }
    }

    [Fact]
    public async Task BundledStockfish_CompletesUciHandshakeAndReturnsLegalMove()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "Stockfish", "stockfish-windows-x86-64-avx2.exe");
        Assert.True(File.Exists(path));
        await using var engine = new StockfishClient(path);

        await engine.InitializeAsync();
        await engine.NewGameAsync();
        await engine.ConfigureStrengthAsync(EngineDifficulty.Presets[0]);
        var result = await engine.CalculateMoveAsync(ChessGame.InitialFen, TimeSpan.FromMilliseconds(100));

        Assert.False(string.IsNullOrWhiteSpace(engine.Name));
        Assert.True(Move.TryParseUci(result.BestMove, out var from, out var to, out var promotion));
        Assert.True(ChessGame.CreateInitial().TryMakeMove(new Move(from, to, promotion), out _));
    }

    [Fact]
    public async Task JsonStore_PersistsEngineConfigurationAcrossReload()
    {
        var directory = Path.Combine(Path.GetTempPath(), "FluentChessTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, "engines.json");
        try
        {
            var store = new JsonFileStore();
            var expected = new EngineDefinition { Name = "Test UCI", ExecutablePath = "C:\\Engines\\test.exe" };
            await store.SaveListAsync(path, new[] { expected });

            var actual = await store.LoadListAsync<EngineDefinition>(path);

            var loaded = Assert.Single(actual);
            Assert.Equal(expected.Id, loaded.Id);
            Assert.Equal("Test UCI", loaded.Name);
        }
        finally
        {
            Directory.Delete(directory, recursive: true);
        }
    }
}
