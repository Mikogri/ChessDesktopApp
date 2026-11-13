using Chess.Core;
using ChessDesktopApp.Engine;
using ChessDesktopApp.Models;
using ChessDesktopApp.Storage;
using System.Text;

namespace ChessDesktopApp.Services;

public enum PlayerColorChoice
{
    White,
    Black,
    Random
}

public sealed class GameSessionService : IAsyncDisposable
{
    private IChessEngine? _engine;
    private readonly EngineManager? _engineManager;
    private readonly EngineUsageCoordinator? _engineUsage;
    private IAsyncDisposable? _engineLease;
    private readonly EngineGameRepository? _games;
    private EngineDefinition? _currentEngineDefinition;
    private DateTimeOffset _gameStartedAt;
    private bool _currentGameSaved;
    private readonly SemaphoreSlim _turnLock = new(1, 1);
    private CancellationTokenSource _gameCancellation = new();
    private bool _disposed;
    private PieceColor? _resignationWinner;

    public GameSessionService(IChessEngine engine)
    {
        _engine = engine;
        Game = ChessGame.CreateInitial();
    }

    public GameSessionService(EngineManager engineManager)
    {
        _engineManager = engineManager;
        Game = ChessGame.CreateInitial();
    }

    public GameSessionService(
        EngineManager engineManager,
        EngineUsageCoordinator engineUsage,
        EngineGameRepository games)
    {
        _engineManager = engineManager;
        _engineUsage = engineUsage;
        _games = games;
        Game = ChessGame.CreateInitial();
    }

    public event EventHandler? StateChanged;

    public ChessGame Game { get; private set; }
    public PieceColor HumanColor { get; private set; } = PieceColor.White;
    public EngineDifficulty Difficulty { get; private set; } = EngineDifficulty.Presets[2];
    public string EngineName { get; private set; } = "Silnik UCI";
    public EngineEvaluation? Evaluation { get; private set; }
    public bool HasStarted { get; private set; }
    public bool IsEngineThinking { get; private set; }
    public string? EngineError { get; private set; }
    public GameStatus Status => _resignationWinner is { } winner
        ? new GameStatus(GameStatusKind.Resignation, winner)
        : Game.GetStatus();
    public bool IsHumanTurn => HasStarted && !IsEngineThinking && !Status.IsGameOver && Game.SideToMove == HumanColor;
    public bool CanResign => HasStarted && !Status.IsGameOver;

    public async Task StartNewGameAsync(
        PlayerColorChoice colorChoice,
        EngineDifficulty difficulty,
        EngineDefinition? engineDefinition = null,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        _gameCancellation.Cancel();
        _gameCancellation.Dispose();
        _gameCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var gameToken = _gameCancellation.Token;

        await _turnLock.WaitAsync(cancellationToken);
        try
        {
            if (_engineManager is not null)
            {
                if (engineDefinition is null)
                {
                    throw new ChessEngineException("Wybierz silnik do gry.");
                }

                if (_engine is not null)
                {
                    try { await _engine.StopAsync(cancellationToken); } catch (ChessEngineException) { }
                    await _engine.DisposeAsync();
                }

                if (_engineLease is not null)
                {
                    await _engineLease.DisposeAsync();
                    _engineLease = null;
                }
                if (_engineUsage is not null)
                    _engineLease = await _engineUsage.AcquireAsync([engineDefinition.Id], false, cancellationToken);

                _engine = _engineManager.Create(engineDefinition);
                _currentEngineDefinition = engineDefinition;
            }

            var engine = _engine ?? throw new ChessEngineException("Silnik UCI nie jest skonfigurowany.");
            HasStarted = false;
            IsEngineThinking = true;
            EngineError = null;
            Evaluation = null;
            _resignationWinner = null;
            Game = ChessGame.CreateInitial();
            _gameStartedAt = DateTimeOffset.Now;
            _currentGameSaved = false;
            HumanColor = colorChoice switch
            {
                PlayerColorChoice.White => PieceColor.White,
                PlayerColorChoice.Black => PieceColor.Black,
                PlayerColorChoice.Random => Random.Shared.Next(2) == 0 ? PieceColor.White : PieceColor.Black,
                _ => PieceColor.White
            };
            Difficulty = difficulty;
            NotifyStateChanged();

            await engine.InitializeAsync(gameToken);
            EngineName = engine.Name;
            await engine.ConfigureStrengthAsync(difficulty, gameToken);
            await engine.NewGameAsync(gameToken);
            HasStarted = true;
            IsEngineThinking = false;
            NotifyStateChanged();

            if (HumanColor == PieceColor.Black)
            {
                await MakeEngineMoveCoreAsync(gameToken);
            }
        }
        catch (Exception exception) when (exception is ChessEngineException or IOException)
        {
            HasStarted = false;
            IsEngineThinking = false;
            EngineError = exception.Message;
            NotifyStateChanged();
            if (_engineLease is not null)
            {
                await _engineLease.DisposeAsync();
                _engineLease = null;
            }
            throw;
        }
        finally
        {
            IsEngineThinking = false;
            NotifyStateChanged();
            _turnLock.Release();
        }
    }

    public async Task<bool> TryMakeHumanMoveAsync(
        Square from,
        Square to,
        PieceType? promotion,
        CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!IsHumanTurn || !_turnLock.Wait(0))
        {
            return false;
        }

        try
        {
            using var linked = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken, _gameCancellation.Token);
            if (!Game.TryMakeMove(new Move(from, to, promotion), out _))
            {
                return false;
            }

            Evaluation = null;
            NotifyStateChanged();
            if (!Status.IsGameOver)
            {
                await MakeEngineMoveCoreAsync(linked.Token);
            }
            await ReleaseEngineIfGameEndedAsync();

            return true;
        }
        catch (ChessEngineException exception)
        {
            EngineError = exception.Message;
            NotifyStateChanged();
            return false;
        }
        catch (OperationCanceledException) when (_gameCancellation.IsCancellationRequested)
        {
            return false;
        }
        finally
        {
            IsEngineThinking = false;
            NotifyStateChanged();
            _turnLock.Release();
        }
    }

    public IReadOnlyList<Move> GetLegalMovesFrom(Square square) =>
        IsHumanTurn ? Game.GetLegalMovesFrom(square) : [];

    public async Task CancelCurrentCalculationAsync()
    {
        _gameCancellation.Cancel();
        if (_engine is not null) await _engine.StopAsync();
    }

    public async Task ResignAsync(CancellationToken cancellationToken = default)
    {
        if (!CanResign)
        {
            return;
        }

        _gameCancellation.Cancel();
        try { if (_engine is not null) await _engine.StopAsync(cancellationToken); } catch (ChessEngineException) { }

        await _turnLock.WaitAsync(cancellationToken);
        try
        {
            if (!Status.IsGameOver)
            {
                _resignationWinner = HumanColor == PieceColor.White ? PieceColor.Black : PieceColor.White;
                IsEngineThinking = false;
                NotifyStateChanged();
            }
            await ReleaseEngineIfGameEndedAsync();
        }
        finally
        {
            _turnLock.Release();
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _gameCancellation.Cancel();
        try { if (_engine is not null) await _engine.StopAsync(); } catch (ChessEngineException) { }
        if (_engine is not null) await _engine.DisposeAsync();
        if (_engineLease is not null) await _engineLease.DisposeAsync();
        _gameCancellation.Dispose();
        _turnLock.Dispose();
    }

    private async Task MakeEngineMoveCoreAsync(CancellationToken cancellationToken)
    {
        if (Status.IsGameOver)
        {
            return;
        }

        IsEngineThinking = true;
        EngineError = null;
        NotifyStateChanged();
        await Task.Delay(TimeSpan.FromMilliseconds(500), cancellationToken);
        var engine = _engine ?? throw new ChessEngineException("Silnik UCI nie jest uruchomiony.");
        var result = await engine.CalculateMoveAsync(
            Game.ToFen(),
            TimeSpan.FromMilliseconds(Difficulty.MoveTimeMilliseconds),
            cancellationToken);
        cancellationToken.ThrowIfCancellationRequested();

        if (!Game.TryMakeUciMove(result.BestMove, out _))
        {
            throw new ChessEngineException($"{EngineName} zwrócił nielegalny ruch: {result.BestMove}.");
        }

        Evaluation = result.Evaluation;
        IsEngineThinking = false;
        NotifyStateChanged();
        await ReleaseEngineIfGameEndedAsync();
    }

    private async Task ReleaseEngineIfGameEndedAsync()
    {
        if (!Status.IsGameOver) return;
        await SaveFinishedPlayerGameAsync();
        if (_engineLease is not null)
        {
            await _engineLease.DisposeAsync();
            _engineLease = null;
        }
    }

    private async Task SaveFinishedPlayerGameAsync()
    {
        if (_currentGameSaved || _games is null || _currentEngineDefinition is null) return;
        _currentGameSaved = true;
        var humanIsWhite = HumanColor == PieceColor.White;
        var result = Status.Winner switch
        {
            PieceColor.White => "1-0",
            PieceColor.Black => "0-1",
            _ => "1/2-1/2"
        };
        var saved = new SavedEngineGame
        {
            GameKind = "PlayerVsEngine",
            WhiteEngine = humanIsWhite ? "Gracz" : EngineName,
            BlackEngine = humanIsWhite ? EngineName : "Gracz",
            WhiteEngineId = humanIsWhite ? Guid.Empty : _currentEngineDefinition.Id,
            BlackEngineId = humanIsWhite ? _currentEngineDefinition.Id : Guid.Empty,
            StartedAt = _gameStartedAt,
            FinishedAt = DateTimeOffset.Now,
            Result = result,
            FormatDescription = $"Gracz kontra silnik · ELO {Difficulty.DisplayElo}",
            ResultReason = Status.Kind switch
            {
                GameStatusKind.Checkmate => "Mat",
                GameStatusKind.Stalemate => "Pat",
                GameStatusKind.DrawByInsufficientMaterial => "Remis — niewystarczający materiał",
                GameStatusKind.DrawByFiftyMoveRule => "Remis — zasada 50 posunięć",
                GameStatusKind.DrawByThreefoldRepetition => "Remis — powtórzenie pozycji",
                GameStatusKind.Resignation => "Poddanie",
                _ => "Partia zakończona"
            },
            UciMoves = Game.MoveHistory.Select(item => item.Move.Uci).ToList()
        };
        var pgn = new StringBuilder()
            .AppendLine("[Event \"Player vs engine\"]")
            .AppendLine($"[Date \"{saved.StartedAt:yyyy.MM.dd}\"]")
            .AppendLine($"[White \"{saved.WhiteEngine}\"]")
            .AppendLine($"[Black \"{saved.BlackEngine}\"]")
            .AppendLine($"[Result \"{result}\"]")
            .AppendLine();
        foreach (var group in Game.MoveHistory.GroupBy(item => item.MoveNumber))
        {
            pgn.Append(group.Key).Append(". ")
                .Append(group.FirstOrDefault(item => item.Color == PieceColor.White)?.Notation).Append(' ')
                .Append(group.FirstOrDefault(item => item.Color == PieceColor.Black)?.Notation).Append(' ');
        }
        saved.Pgn = pgn.Append(result).ToString();
        await _games.AddAsync(saved);
    }

    private void NotifyStateChanged() => StateChanged?.Invoke(this, EventArgs.Empty);
}
