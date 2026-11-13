using System.Diagnostics;
using System.Text.RegularExpressions;
using System.Threading.Channels;
using Chess.Core;

namespace ChessDesktopApp.Engine;

public sealed partial class StockfishClient : IChessEngine
{
    private readonly string _executablePath;
    private readonly IReadOnlyDictionary<string, string> _customOptions;
    private readonly SemaphoreSlim _operationLock = new(1, 1);
    private readonly SemaphoreSlim _inputLock = new(1, 1);
    private Channel<string> _output = CreateOutputChannel();

    private Process? _process;
    private CancellationTokenSource? _processLifetime;
    private Task? _outputPump;
    private Task? _errorPump;
    private string? _lastEngineError;
    private bool _initialized;
    private bool _disposed;
    private int _minimumElo = 1320;
    private int _maximumElo = 3190;
    private double _randomMoveChance;

    public StockfishClient(string executablePath, IReadOnlyDictionary<string, string>? customOptions = null)
    {
        _executablePath = executablePath;
        _customOptions = customOptions ?? new Dictionary<string, string>();
    }

    public string Name { get; private set; } = "Silnik UCI";
    public bool IsRunning => _process is { HasExited: false };

    public async Task InitializeAsync(CancellationToken cancellationToken = default)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            if (_initialized && IsRunning)
            {
                return;
            }

            StartProcess();
            await SendCommandAsync("uci", cancellationToken);
            using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(10));
            while (true)
            {
                var line = await ReadLineAsync(timeout.Token);
                ParseOptionCapabilities(line);
                if (line.StartsWith("id name ", StringComparison.Ordinal))
                {
                    Name = line[8..].Trim();
                }
                if (line.Equals("uciok", StringComparison.Ordinal))
                {
                    break;
                }
            }

            foreach (var option in _customOptions)
            {
                await SendCommandAsync($"setoption name {option.Key} value {option.Value}", timeout.Token);
            }
            await WaitUntilReadyAsync(timeout.Token);
            _initialized = true;
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            throw new ChessEngineException("Silnik UCI nie odpowiedział na inicjalizację w wyznaczonym czasie.");
        }
        catch (ChessEngineException)
        {
            throw;
        }
        catch (Exception exception)
        {
            throw new ChessEngineException("Nie udało się uruchomić silnika UCI.", exception);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task NewGameAsync(CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            await SendCommandAsync("ucinewgame", cancellationToken);
            await WaitUntilReadyAsync(cancellationToken);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task ConfigureStrengthAsync(EngineDifficulty difficulty, CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _operationLock.WaitAsync(cancellationToken);
        try
        {
            _randomMoveChance = Math.Clamp(difficulty.RandomMoveChance, 0, 1);
            await SendCommandAsync($"setoption name Skill Level value {Math.Clamp(difficulty.SkillLevel, 0, 20)}", cancellationToken);
            if (difficulty.Elo is { } requestedElo)
            {
                await SendCommandAsync("setoption name UCI_LimitStrength value true", cancellationToken);
                await SendCommandAsync($"setoption name UCI_Elo value {Math.Clamp(requestedElo, _minimumElo, _maximumElo)}", cancellationToken);
            }
            else
            {
                await SendCommandAsync("setoption name UCI_LimitStrength value false", cancellationToken);
            }

            await WaitUntilReadyAsync(cancellationToken);
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task<EngineMoveResult> CalculateMoveAsync(
        string fen,
        TimeSpan moveTime,
        CancellationToken cancellationToken = default)
    {
        await EnsureInitializedAsync(cancellationToken);
        await _operationLock.WaitAsync(cancellationToken);
        var positionIsWhiteToMove = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries).ElementAtOrDefault(1) == "w";
        try
        {
            if (_randomMoveChance > 0 && Random.Shared.NextDouble() < _randomMoveChance)
            {
                var legalMoves = ChessGame.FromFen(fen).GetLegalMoves();
                if (legalMoves.Count > 0)
                {
                    var randomMove = legalMoves[Random.Shared.Next(legalMoves.Count)];
                    return new EngineMoveResult(randomMove.Uci, null);
                }
            }

            await SendCommandAsync($"position fen {fen}", cancellationToken);
            await SendCommandAsync($"go movetime {Math.Max(50, (int)moveTime.TotalMilliseconds)}", cancellationToken);

            EngineEvaluation? latestEvaluation = null;
            using var stopRegistration = cancellationToken.Register(() => _ = TrySendStopAsync());
            using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(30));
            while (true)
            {
                var line = await ReadLineAsync(timeout.Token);
                if (line.StartsWith("info ", StringComparison.Ordinal))
                {
                    latestEvaluation = ParseEvaluation(line, positionIsWhiteToMove) ?? latestEvaluation;
                    continue;
                }

                var bestMove = BestMoveRegex().Match(line);
                if (bestMove.Success)
                {
                    var move = bestMove.Groups[1].Value;
                    if (move is "(none)" or "0000")
                    {
                throw new ChessEngineException("Silnik UCI nie zwrócił legalnego ruchu dla tej pozycji.");
                    }

                    return new EngineMoveResult(move, latestEvaluation);
                }
            }
        }
        catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
        {
            await TrySendStopAsync();
            throw new ChessEngineException("Silnik UCI przekroczył limit czasu obliczania ruchu.");
        }
        finally
        {
            _operationLock.Release();
        }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        if (IsRunning)
        {
            await SendCommandAsync("stop", cancellationToken);
        }
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _processLifetime?.Cancel();
        if (IsRunning)
        {
            try
            {
                await SendCommandAsync("quit", CancellationToken.None);
                using var quitTimeout = new CancellationTokenSource(TimeSpan.FromSeconds(1));
                await _process!.WaitForExitAsync(quitTimeout.Token);
            }
            catch
            {
                if (IsRunning)
                {
                    _process!.Kill(entireProcessTree: true);
                }
            }
        }

        if (_outputPump is not null)
        {
            try { await _outputPump; } catch (OperationCanceledException) { }
        }
        if (_errorPump is not null)
        {
            try { await _errorPump; } catch (OperationCanceledException) { }
        }

        _process?.Dispose();
        _processLifetime?.Dispose();
        _operationLock.Dispose();
        _inputLock.Dispose();
    }

    private void StartProcess()
    {
        if (!File.Exists(_executablePath))
        {
            throw new ChessEngineException(
                $"Nie znaleziono pliku silnika UCI: '{_executablePath}'. Dodaj go ponownie w sekcji Silniki.");
        }

        _processLifetime?.Cancel();
        if (_process is not null)
        {
            try
            {
                if (!_process.HasExited)
                {
                    _process.Kill(entireProcessTree: true);
                }
            }
            catch
            {
            }
            _process.Dispose();
        }

        _processLifetime?.Dispose();
        _processLifetime = new CancellationTokenSource();
        _output = CreateOutputChannel();
        _lastEngineError = null;
        _process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = _executablePath,
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Path.GetDirectoryName(_executablePath)!
            },
            EnableRaisingEvents = true
        };

        if (!_process.Start())
        {
            throw new ChessEngineException("System Windows nie uruchomił procesu silnika UCI.");
        }

        _outputPump = PumpOutputAsync(_process, _output.Writer, _processLifetime.Token);
        _errorPump = PumpErrorAsync(_process, _processLifetime.Token);
    }

    private async Task PumpOutputAsync(
        Process process,
        ChannelWriter<string> writer,
        CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardOutput.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                await writer.WriteAsync(line, cancellationToken);
            }
        }
        catch (OperationCanceledException)
        {
        }
        finally
        {
            if (process.HasExited && !_disposed)
            {
                var details = string.IsNullOrWhiteSpace(_lastEngineError) ? string.Empty : $" Szczegóły: {_lastEngineError}";
                writer.TryComplete(new ChessEngineException($"Proces silnika UCI został nieoczekiwanie zamknięty.{details}"));
            }
        }
    }

    private async Task PumpErrorAsync(Process process, CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var line = await process.StandardError.ReadLineAsync(cancellationToken);
                if (line is null)
                {
                    break;
                }

                _lastEngineError = line;
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task WaitUntilReadyAsync(CancellationToken cancellationToken)
    {
        await SendCommandAsync("isready", cancellationToken);
        using var timeout = CreateTimeout(cancellationToken, TimeSpan.FromSeconds(10));
        while (!string.Equals(await ReadLineAsync(timeout.Token), "readyok", StringComparison.Ordinal))
        {
        }
    }

    private async Task EnsureInitializedAsync(CancellationToken cancellationToken)
    {
        if (!_initialized || !IsRunning)
        {
            await InitializeAsync(cancellationToken);
        }
    }

    private async Task SendCommandAsync(string command, CancellationToken cancellationToken)
    {
        var process = _process;
        if (process is null || process.HasExited)
        {
            throw new ChessEngineException("Proces silnika UCI nie jest uruchomiony.");
        }

        await _inputLock.WaitAsync(cancellationToken);
        try
        {
            await process.StandardInput.WriteLineAsync(command.AsMemory(), cancellationToken);
            await process.StandardInput.FlushAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is IOException or InvalidOperationException)
        {
            throw new ChessEngineException("Utracono połączenie z procesem Stockfish.", exception);
        }
        finally
        {
            _inputLock.Release();
        }
    }

    private async Task TrySendStopAsync()
    {
        try { await SendCommandAsync("stop", CancellationToken.None); } catch { }
    }

    private async Task<string> ReadLineAsync(CancellationToken cancellationToken)
    {
        try
        {
            return await _output.Reader.ReadAsync(cancellationToken);
        }
        catch (ChannelClosedException exception)
        {
            throw new ChessEngineException("Silnik UCI zakończył strumień odpowiedzi.", exception);
        }
    }

    private void ParseOptionCapabilities(string line)
    {
        var elo = EloOptionRegex().Match(line);
        if (elo.Success && int.TryParse(elo.Groups[1].Value, out var minimum) &&
            int.TryParse(elo.Groups[2].Value, out var maximum))
        {
            _minimumElo = minimum;
            _maximumElo = maximum;
        }
    }

    private static EngineEvaluation? ParseEvaluation(string line, bool positionIsWhiteToMove)
    {
        var score = ScoreRegex().Match(line);
        if (!score.Success)
        {
            return null;
        }

        var raw = int.Parse(score.Groups[2].Value);
        var whiteValue = positionIsWhiteToMove ? raw : -raw;
        var depthMatch = DepthRegex().Match(line);
        var nodesMatch = NodesRegex().Match(line);
        var pvMatch = PvRegex().Match(line);
        return new EngineEvaluation(
            score.Groups[1].Value == "cp" ? whiteValue : null,
            score.Groups[1].Value == "mate" ? whiteValue : null,
            depthMatch.Success ? int.Parse(depthMatch.Groups[1].Value) : null,
            nodesMatch.Success ? long.Parse(nodesMatch.Groups[1].Value) : null,
            pvMatch.Success ? pvMatch.Groups[1].Value : null);
    }

    private static CancellationTokenSource CreateTimeout(CancellationToken outerToken, TimeSpan timeout)
    {
        var linked = CancellationTokenSource.CreateLinkedTokenSource(outerToken);
        linked.CancelAfter(timeout);
        return linked;
    }

    private static Channel<string> CreateOutputChannel() =>
        Channel.CreateUnbounded<string>(new UnboundedChannelOptions
        {
            SingleReader = true,
            SingleWriter = true
        });

    [GeneratedRegex(@"^bestmove\s+(\S+)", RegexOptions.CultureInvariant)]
    private static partial Regex BestMoveRegex();

    [GeneratedRegex(@"\bscore\s+(cp|mate)\s+(-?\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex ScoreRegex();

    [GeneratedRegex(@"\bdepth\s+(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex DepthRegex();

    [GeneratedRegex(@"\bnodes\s+(\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex NodesRegex();

    [GeneratedRegex(@"\bpv\s+(.+)$", RegexOptions.CultureInvariant)]
    private static partial Regex PvRegex();

    [GeneratedRegex(@"^option name UCI_Elo type spin default \d+ min (\d+) max (\d+)", RegexOptions.CultureInvariant)]
    private static partial Regex EloOptionRegex();
}
