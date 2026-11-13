using Chess.Core;
using ChessDesktopApp.Engine;
using ChessDesktopApp.Services;
using Xunit;

namespace Chess.App.Tests;

public sealed class GameSessionServiceTests
{
    [Fact]
    public void DifficultyPresets_IncludeLevelsBelowStockfishNativeMinimum()
    {
        Assert.Contains(EngineDifficulty.Presets, level => level.DisplayElo <= 400);
        Assert.Contains(EngineDifficulty.Presets, level => level.DisplayElo == 700);
        Assert.All(
            EngineDifficulty.Presets.Where(level => level.DisplayElo < 1320),
            level => Assert.True(level.RandomMoveChance > 0));
    }

    [Fact]
    public async Task HumanMove_IsFollowedByMockedEngineMove()
    {
        var engine = new FakeChessEngine("e7e5");
        await using var session = new GameSessionService(engine);
        await session.StartNewGameAsync(PlayerColorChoice.White, EngineDifficulty.Presets[2]);

        var moved = await session.TryMakeHumanMoveAsync(
            Square.Parse("e2"), Square.Parse("e4"), promotion: null);

        Assert.True(moved);
        Assert.Equal(new Piece(PieceColor.White, PieceType.Pawn), session.Game[Square.Parse("e4")]);
        Assert.Equal(new Piece(PieceColor.Black, PieceType.Pawn), session.Game[Square.Parse("e5")]);
        Assert.Equal(PieceColor.White, session.Game.SideToMove);
        Assert.Equal(1, engine.CalculationCount);
    }

    [Fact]
    public async Task ChoosingBlack_TriggersAutomaticOpeningMove()
    {
        var engine = new FakeChessEngine("e2e4");
        await using var session = new GameSessionService(engine);

        await session.StartNewGameAsync(PlayerColorChoice.Black, EngineDifficulty.Presets[0]);

        Assert.Equal(PieceColor.Black, session.HumanColor);
        Assert.Equal(PieceColor.Black, session.Game.SideToMove);
        Assert.Equal(new Piece(PieceColor.White, PieceType.Pawn), session.Game[Square.Parse("e4")]);
        Assert.True(session.IsHumanTurn);
    }

    [Fact]
    public async Task Resignation_EndsGameAndAwardsWinToOpponent()
    {
        var engine = new FakeChessEngine();
        await using var session = new GameSessionService(engine);
        await session.StartNewGameAsync(PlayerColorChoice.White, EngineDifficulty.Presets[1]);

        await session.ResignAsync();

        Assert.Equal(GameStatusKind.Resignation, session.Status.Kind);
        Assert.Equal(PieceColor.Black, session.Status.Winner);
        Assert.True(session.Status.IsGameOver);
        Assert.False(session.CanResign);
    }

    private sealed class FakeChessEngine(params string[] moves) : IChessEngine
    {
        private readonly Queue<string> _moves = new(moves);

        public bool IsRunning => true;
        public int CalculationCount { get; private set; }

        public Task InitializeAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task NewGameAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public Task ConfigureStrengthAsync(EngineDifficulty difficulty, CancellationToken cancellationToken = default) => Task.CompletedTask;

        public Task<EngineMoveResult> CalculateMoveAsync(
            string fen,
            TimeSpan moveTime,
            CancellationToken cancellationToken = default)
        {
            CalculationCount++;
            return Task.FromResult(new EngineMoveResult(
                _moves.Dequeue(),
                new EngineEvaluation(WhiteCentipawns: 12, WhiteMateIn: null, Depth: 10, Nodes: 1000, PrincipalVariation: null)));
        }

        public Task StopAsync(CancellationToken cancellationToken = default) => Task.CompletedTask;
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
    }
}
