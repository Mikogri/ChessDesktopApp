namespace ChessDesktopApp.Engine;

public interface IChessEngine : IAsyncDisposable
{
    string Name => "Silnik UCI";
    bool IsRunning { get; }

    Task InitializeAsync(CancellationToken cancellationToken = default);
    Task NewGameAsync(CancellationToken cancellationToken = default);
    Task ConfigureStrengthAsync(EngineDifficulty difficulty, CancellationToken cancellationToken = default);
    Task<EngineMoveResult> CalculateMoveAsync(
        string fen,
        TimeSpan moveTime,
        CancellationToken cancellationToken = default);
    Task StopAsync(CancellationToken cancellationToken = default);
}
