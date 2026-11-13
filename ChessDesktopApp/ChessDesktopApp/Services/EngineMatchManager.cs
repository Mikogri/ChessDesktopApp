using System.Diagnostics;
using System.Text;
using Chess.Core;
using ChessDesktopApp.Engine;
using ChessDesktopApp.Models;
using ChessDesktopApp.Storage;

namespace ChessDesktopApp.Services;

public enum EngineColorAssignment { EngineAWhite, EngineBWhite, Random }

public sealed record EngineMatchConfiguration(
    EngineDefinition EngineA,
    EngineDefinition EngineB,
    EngineColorAssignment Colors,
    int InitialTimeSeconds,
    int IncrementSeconds,
    Guid? TournamentId = null,
    Guid? TournamentMatchId = null,
    bool SaveGame = true,
    bool WaitForAvailableEngines = false);

public sealed class EngineMatchManager(
    EngineManager engineManager,
    EngineGameRepository games,
    EngineUsageCoordinator engineUsage) : IAsyncDisposable
{
    private CancellationTokenSource? _matchCancellation;
    private TaskCompletionSource _resumeSignal = CompletedSignal();
    private IChessEngine? _white;
    private IChessEngine? _black;
    private DateTimeOffset _startedAt;
    private EngineMatchConfiguration? _configuration;
    private readonly List<EngineAnalysisSnapshot> _analysis = [];
    private DateTimeOffset? _thinkingStartedAt;
    private PieceColor? _thinkingColor;
    private bool _disposed;

    public event EventHandler? StateChanged;
    public ChessGame Game { get; private set; } = ChessGame.CreateInitial();
    public EngineDefinition? WhiteDefinition { get; private set; }
    public EngineDefinition? BlackDefinition { get; private set; }
    public EngineEvaluation? Evaluation { get; private set; }
    public TimeSpan WhiteRemaining { get; private set; }
    public TimeSpan BlackRemaining { get; private set; }
    public TimeSpan DisplayWhiteRemaining => GetDisplayRemaining(PieceColor.White);
    public TimeSpan DisplayBlackRemaining => GetDisplayRemaining(PieceColor.Black);
    public DateTimeOffset StartedAt => _startedAt;
    public int InitialTimeSeconds => _configuration?.InitialTimeSeconds ?? 0;
    public int IncrementSeconds => _configuration?.IncrementSeconds ?? 0;
    public string StatusText { get; private set; } = "Gotowy";
    public string Result { get; private set; } = "*";
    public bool IsRunning { get; private set; }
    public bool IsPaused { get; private set; }
    public bool WasStoppedManually { get; private set; }
    public SavedEngineGame? LastSavedGame { get; private set; }
    public IReadOnlyList<EngineAnalysisSnapshot> Analysis => _analysis;

    public async Task<SavedEngineGame?> StartAsync(EngineMatchConfiguration configuration, CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await StopAsync();
        StatusText = "Oczekiwanie na wolne silniki";
        Notify();
        await using var engineLease = await engineUsage.AcquireAsync(
            [configuration.EngineA.Id, configuration.EngineB.Id],
            configuration.WaitForAvailableEngines,
            cancellationToken);
        _configuration = configuration;
        _matchCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _matchCancellation.Token;
        var engineAWhite = configuration.Colors switch
        {
            EngineColorAssignment.EngineAWhite => true,
            EngineColorAssignment.EngineBWhite => false,
            _ => Random.Shared.Next(2) == 0
        };
        WhiteDefinition = engineAWhite ? configuration.EngineA : configuration.EngineB;
        BlackDefinition = engineAWhite ? configuration.EngineB : configuration.EngineA;
        Game = ChessGame.CreateInitial();
        WhiteRemaining = TimeSpan.FromSeconds(Math.Max(1, configuration.InitialTimeSeconds));
        BlackRemaining = WhiteRemaining;
        Evaluation = null;
        Result = "*";
        WasStoppedManually = false;
        LastSavedGame = null;
        _analysis.Clear();
        _startedAt = DateTimeOffset.Now;
        IsRunning = true;
        IsPaused = false;
        _resumeSignal = CompletedSignal();
        StatusText = "Uruchamianie silników";
        Notify();

        try
        {
            _white = engineManager.Create(WhiteDefinition);
            _black = engineManager.Create(BlackDefinition);
            await Task.WhenAll(_white.InitializeAsync(token), _black.InitializeAsync(token));
            var fullStrength = EngineDifficulty.Presets[^1];
            await Task.WhenAll(_white.ConfigureStrengthAsync(fullStrength, token), _black.ConfigureStrengthAsync(fullStrength, token));
            await Task.WhenAll(_white.NewGameAsync(token), _black.NewGameAsync(token));
            StatusText = "Partia w toku";
            Notify();

            while (!token.IsCancellationRequested && !Game.GetStatus().IsGameOver && Game.MoveHistory.Count < 400)
            {
                await _resumeSignal.Task.WaitAsync(token);
                var whiteToMove = Game.SideToMove == PieceColor.White;
                var engine = whiteToMove ? _white : _black;
                var definition = whiteToMove ? WhiteDefinition : BlackDefinition;
                var remaining = whiteToMove ? WhiteRemaining : BlackRemaining;
                var allocation = TimeSpan.FromMilliseconds(Math.Clamp(
                    remaining.TotalMilliseconds / 24 + configuration.IncrementSeconds * 350, 80, 2500));
                StatusText = $"Myśli: {definition.Name}";
                Notify();
                _thinkingStartedAt = DateTimeOffset.Now;
                _thinkingColor = Game.SideToMove;
                var stopwatch = Stopwatch.StartNew();
                var response = await engine.CalculateMoveAsync(Game.ToFen(), allocation, token);
                stopwatch.Stop();
                _thinkingStartedAt = null;
                _thinkingColor = null;
                remaining -= stopwatch.Elapsed;
                if (remaining <= TimeSpan.Zero)
                {
                    Result = whiteToMove ? "0-1" : "1-0";
                    StatusText = $"Koniec czasu: {definition.Name}";
                    break;
                }
                remaining += TimeSpan.FromSeconds(configuration.IncrementSeconds);
                if (whiteToMove) WhiteRemaining = remaining; else BlackRemaining = remaining;
                if (!Game.TryMakeUciMove(response.BestMove, out _))
                    throw new ChessEngineException($"{definition.Name} zwrócił nielegalny ruch {response.BestMove}.");
                Evaluation = response.Evaluation;
                _analysis.Add(new EngineAnalysisSnapshot
                {
                    Ply = Game.MoveHistory.Count,
                    EngineName = definition.Name,
                    WhiteCentipawns = response.Evaluation?.WhiteCentipawns,
                    WhiteMateIn = response.Evaluation?.WhiteMateIn,
                    Depth = response.Evaluation?.Depth,
                    Nodes = response.Evaluation?.Nodes,
                    ElapsedMilliseconds = stopwatch.ElapsedMilliseconds
                });
                StatusText = "Partia w toku";
                Notify();
                await Task.Delay(120, token);
            }

            if (Result == "*") Result = ResultFromStatus(Game.GetStatus());
            if (Game.MoveHistory.Count >= 400 && Result == "*")
            {
                Result = "1/2-1/2";
                StatusText = "Remis — limit 200 posunięć";
            }
            else if (!token.IsCancellationRequested)
            {
                StatusText = Result == "*" ? "Zakończono" : $"Wynik {Result}";
            }
        }
        catch (OperationCanceledException)
        {
            StatusText = "Pojedynek zatrzymany";
        }
        catch (Exception exception) when (exception is ChessEngineException or IOException)
        {
            StatusText = exception.Message;
            Result = Game.SideToMove == PieceColor.White ? "0-1" : "1-0";
        }
        finally
        {
            _thinkingStartedAt = null;
            _thinkingColor = null;
            IsRunning = false;
            IsPaused = false;
            await DisposeEnginesAsync();
            if (WhiteDefinition is not null && BlackDefinition is not null && Game.MoveHistory.Count > 0)
            {
                LastSavedGame = BuildSavedGame(configuration);
                if (configuration.SaveGame)
                    await games.AddAsync(LastSavedGame, CancellationToken.None);
            }
            Notify();
        }
        return LastSavedGame;
    }

    public void Pause()
    {
        if (!IsRunning || IsPaused) return;
        IsPaused = true;
        _resumeSignal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        StatusText = "Pauza — po bieżącym ruchu";
        Notify();
    }

    public void Resume()
    {
        if (!IsPaused) return;
        IsPaused = false;
        _resumeSignal.TrySetResult();
        StatusText = "Partia w toku";
        Notify();
    }

    public async Task StopAsync()
    {
        if (_matchCancellation is null) return;
        if (IsRunning) WasStoppedManually = true;
        _matchCancellation.Cancel();
        _resumeSignal.TrySetResult();
        try { if (_white is not null) await _white.StopAsync(); } catch { }
        try { if (_black is not null) await _black.StopAsync(); } catch { }
    }

    private SavedEngineGame BuildSavedGame(EngineMatchConfiguration configuration)
    {
        var game = new SavedEngineGame
        {
            TournamentId = configuration.TournamentId,
            TournamentMatchId = configuration.TournamentMatchId,
            WhiteEngine = WhiteDefinition!.Name,
            BlackEngine = BlackDefinition!.Name,
            WhiteEngineId = WhiteDefinition.Id,
            BlackEngineId = BlackDefinition.Id,
            StartedAt = _startedAt,
            FinishedAt = DateTimeOffset.Now,
            Result = Result,
            ResultReason = StatusText,
            InitialTimeSeconds = configuration.InitialTimeSeconds,
            IncrementSeconds = configuration.IncrementSeconds,
            FormatDescription = FormatTimeControl(configuration.InitialTimeSeconds, configuration.IncrementSeconds),
            UciMoves = Game.MoveHistory.Select(item => item.Move.Uci).ToList(),
            Analysis = [.. _analysis]
        };
        game.Pgn = BuildPgn(game, Game.MoveHistory);
        return game;
    }

    private static string FormatTimeControl(int initialTimeSeconds, int incrementSeconds)
    {
        var minutes = initialTimeSeconds / 60d;
        return $"{minutes:0.##} min + {incrementSeconds} s";
    }

    private static string BuildPgn(SavedEngineGame game, IReadOnlyList<MoveRecord> moves)
    {
        var text = new StringBuilder()
            .AppendLine($"[Event \"Engine match\"]")
            .AppendLine($"[Date \"{game.StartedAt:yyyy.MM.dd}\"]")
            .AppendLine($"[White \"{game.WhiteEngine.Replace("\"", "'")}\"]")
            .AppendLine($"[Black \"{game.BlackEngine.Replace("\"", "'")}\"]")
            .AppendLine($"[Result \"{game.Result}\"]")
            .AppendLine($"[TimeControl \"{game.InitialTimeSeconds}+{game.IncrementSeconds}\"]")
            .AppendLine();
        foreach (var group in moves.GroupBy(item => item.MoveNumber))
        {
            text.Append(group.Key).Append(". ");
            text.Append(group.FirstOrDefault(item => item.Color == PieceColor.White)?.Notation).Append(' ');
            text.Append(group.FirstOrDefault(item => item.Color == PieceColor.Black)?.Notation).Append(' ');
        }
        return text.Append(game.Result).ToString();
    }

    private static string ResultFromStatus(GameStatus status) => status.Kind switch
    {
        GameStatusKind.Checkmate or GameStatusKind.Resignation => status.Winner == PieceColor.White ? "1-0" : "0-1",
        GameStatusKind.Stalemate or GameStatusKind.DrawByFiftyMoveRule or GameStatusKind.DrawByInsufficientMaterial or GameStatusKind.DrawByThreefoldRepetition => "1/2-1/2",
        _ => "*"
    };

    private TimeSpan GetDisplayRemaining(PieceColor color)
    {
        var remaining = color == PieceColor.White ? WhiteRemaining : BlackRemaining;
        if (_thinkingStartedAt is { } startedAt && _thinkingColor == color)
            remaining -= DateTimeOffset.Now - startedAt;
        return remaining < TimeSpan.Zero ? TimeSpan.Zero : remaining;
    }

    private async Task DisposeEnginesAsync()
    {
        if (_white is not null) { await _white.DisposeAsync(); _white = null; }
        if (_black is not null) { await _black.DisposeAsync(); _black = null; }
    }

    private static TaskCompletionSource CompletedSignal()
    {
        var signal = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        signal.SetResult();
        return signal;
    }

    private void Notify() => StateChanged?.Invoke(this, EventArgs.Empty);

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        await StopAsync();
        await DisposeEnginesAsync();
        _matchCancellation?.Dispose();
    }
}
