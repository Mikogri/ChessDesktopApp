using ChessDesktopApp.Models;
using ChessDesktopApp.Storage;

namespace ChessDesktopApp.Services;

public sealed class TournamentMatchStateChangedEventArgs(Guid matchId, int moveCount) : EventArgs
{
    public Guid MatchId { get; } = matchId;
    public int MoveCount { get; } = moveCount;
}

public sealed class TournamentMatchSession
{
    public TournamentMatchSession(TournamentMatch match, EngineMatchManager runner)
    {
        Match = match;
        Runner = runner;
    }

    public TournamentMatch Match { get; }
    public EngineMatchManager Runner { get; }
    public bool IsActive { get; internal set; } = true;
    public string Title => Match.PairingText;
    public string StatusText => IsActive ? Runner.StatusText : Match.ResultText;
}

public sealed class TournamentManager(
    TournamentRepository tournaments,
    EngineGameRepository games,
    EngineManager engineManager,
    EngineUsageCoordinator engineUsage)
{
    private CancellationTokenSource? _tournamentCancellation;
    private readonly List<EngineMatchManager> _activeMatches = [];
    private readonly List<TournamentMatchSession> _sessions = [];
    public event EventHandler? StateChanged;
    public event EventHandler<TournamentMatchStateChangedEventArgs>? MatchStateChanged;
    public EngineTournament? Current { get; private set; }
    public TournamentMatch? CurrentMatch { get; private set; }
    public bool IsRunning => Current?.Status == TournamentStatus.Running;
    public IReadOnlyList<TournamentMatchSession> Sessions
    {
        get { lock (_sessions) return [.. _sessions]; }
    }

    public void Load(EngineTournament tournament)
    {
        if (IsRunning) return;
        Current = tournament;
        CurrentMatch = null;
        Notify();
    }

    public EngineTournament CreateDraft(
        IReadOnlyList<EngineDefinition> selectedEngines,
        int initialTimeSeconds,
        int incrementSeconds,
        string? name = null,
        bool autoStartNextRound = true,
        bool saveLocally = true)
    {
        if (selectedEngines.Count < 2) throw new InvalidOperationException("Wybierz co najmniej dwa silniki.");
        Current = new EngineTournament
        {
            Name = string.IsNullOrWhiteSpace(name) ? $"Turniej {DateTime.Now:dd.MM.yyyy HH:mm}" : name.Trim(),
            InitialTimeSeconds = Math.Max(1, initialTimeSeconds),
            IncrementSeconds = Math.Max(0, incrementSeconds),
            AutoStartNextRound = autoStartNextRound,
            SaveLocally = saveLocally,
            ParticipantIds = selectedEngines.Select(item => item.Id).ToList(),
            ParticipantNames = selectedEngines.Select(item => item.Name).ToList()
        };
        lock (_sessions) _sessions.Clear();
        BuildSchedule(Current, selectedEngines);
        Notify();
        return Current;
    }

    public void Reroll(IReadOnlyList<EngineDefinition> engines)
    {
        if (Current is null || Current.Status != TournamentStatus.Draft) return;
        var selected = Current.ParticipantIds
            .Select(id => engines.FirstOrDefault(engine => engine.Id == id))
            .Where(engine => engine is not null).Cast<EngineDefinition>()
            .OrderBy(_ => Random.Shared.Next()).ToList();
        BuildSchedule(Current, selected);
        Notify();
    }

    public async Task StartAsync(IReadOnlyList<EngineDefinition> availableEngines, CancellationToken cancellationToken = default)
    {
        if (Current is null || Current.Status is not (TournamentStatus.Draft or TournamentStatus.Paused)) return;
        _tournamentCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        var token = _tournamentCancellation.Token;
        if (Current.Status == TournamentStatus.Draft) Current.StartedAt = DateTimeOffset.Now;
        Current.Status = TournamentStatus.Running;
        await SaveIfEnabledAsync(Current, token);
        Notify();
        try
        {
            while (Current.Matches.Any(item => item.Status == TournamentMatchStatus.Pending))
            {
                var round = Current.Matches.Where(item => item.Status == TournamentMatchStatus.Pending).Min(item => item.Round);
                Current.CurrentRound = round;
                var roundPairings = Current.Matches
                    .Where(item => item.Round == round && item.Status == TournamentMatchStatus.Pending)
                    .OrderBy(item => item.Index).ToList();
                CurrentMatch = roundPairings.FirstOrDefault();
                foreach (var pairing in roundPairings) pairing.Status = TournamentMatchStatus.Running;
                await SaveIfEnabledAsync(Current, token);
                Notify();

                var tasks = roundPairings.Select(pairing =>
                    RunPairingAsync(pairing, availableEngines, token));
                await Task.WhenAll(tasks);
                await SaveIfEnabledAsync(Current, token);

                CurrentMatch = null;
                if (!Current.AutoStartNextRound && Current.Matches.Any(item => item.Status == TournamentMatchStatus.Pending))
                {
                    Current.Status = TournamentStatus.Paused;
                    await SaveIfEnabledAsync(Current, token);
                    Notify();
                    return;
                }
            }

            var standings = CalculateStatistics(Current, []);
            var topPoints = standings.Max(item => item.Points);
            var leaders = standings.Where(item => Math.Abs(item.Points - topPoints) < 0.001).ToList();
            Current.WinnerEngineId = leaders.Count == 1 ? leaders[0].EngineId : null;
            Current.WinnerName = leaders.Count == 1 ? leaders[0].EngineName : "--";
            Current.Status = TournamentStatus.Completed;
            Current.FinishedAt = DateTimeOffset.Now;
            CurrentMatch = null;
            await SaveIfEnabledAsync(Current, CancellationToken.None);
            Notify();
        }
        catch (OperationCanceledException)
        {
            Current.Status = TournamentStatus.Interrupted;
            Current.FinishedAt = DateTimeOffset.Now;
            foreach (var match in Current.Matches.Where(item => item.Status == TournamentMatchStatus.Running))
                match.Status = TournamentMatchStatus.Interrupted;
            await SaveIfEnabledAsync(Current, CancellationToken.None);
            Notify();
        }
        catch
        {
            Current.Status = TournamentStatus.Interrupted;
            Current.FinishedAt = DateTimeOffset.Now;
            await SaveIfEnabledAsync(Current, CancellationToken.None);
            Notify();
            throw;
        }
    }

    public async Task StopAsync()
    {
        _tournamentCancellation?.Cancel();
        EngineMatchManager[] active;
        lock (_activeMatches) active = [.. _activeMatches];
        await Task.WhenAll(active.Select(item => item.StopAsync()));
        if (Current?.Status is TournamentStatus.Draft or TournamentStatus.Paused)
        {
            Current.Status = TournamentStatus.Interrupted;
            Current.FinishedAt = DateTimeOffset.Now;
            await SaveIfEnabledAsync(Current, CancellationToken.None);
            Notify();
        }
    }

    private async Task RunPairingAsync(
        TournamentMatch pairing,
        IReadOnlyList<EngineDefinition> availableEngines,
        CancellationToken token)
    {
        var engineA = availableEngines.FirstOrDefault(item => item.Id == pairing.EngineAId)
            ?? throw new InvalidOperationException($"Brak silnika {pairing.EngineAName}.");
        var engineB = availableEngines.FirstOrDefault(item => item.Id == pairing.EngineBId)
            ?? throw new InvalidOperationException($"Brak silnika {pairing.EngineBName}.");
        var runner = new EngineMatchManager(engineManager, games, engineUsage);
        var session = new TournamentMatchSession(pairing, runner);
        lock (_sessions) _sessions.Add(session);
        EventHandler stateHandler = (_, _) => NotifyMatch(session);
        runner.StateChanged += stateHandler;
        lock (_activeMatches) _activeMatches.Add(runner);
        try
        {
            var game = await runner.StartAsync(new EngineMatchConfiguration(
                engineA, engineB, EngineColorAssignment.EngineAWhite,
                Current!.InitialTimeSeconds, Current.IncrementSeconds,
                Current.Id, pairing.Id, Current.SaveLocally, true), token);
            token.ThrowIfCancellationRequested();
            pairing.SavedGameId = Current.SaveLocally ? game?.Id : null;
            if (runner.WasStoppedManually && (game?.Result ?? runner.Result) == "*")
            {
                pairing.Result = "Zakończono ręcznie";
                pairing.Status = TournamentMatchStatus.Interrupted;
                pairing.WinnerEngineId = null;
                pairing.WinnerName = string.Empty;
                return;
            }
            pairing.Result = game?.Result ?? "*";
            pairing.Status = TournamentMatchStatus.Completed;
            if (game?.Result == "1-0")
            {
                pairing.WinnerEngineId = engineA.Id;
                pairing.WinnerName = engineA.Name;
            }
            else if (game?.Result == "0-1")
            {
                pairing.WinnerEngineId = engineB.Id;
                pairing.WinnerName = engineB.Name;
            }
            else
            {
                pairing.WinnerEngineId = null;
                pairing.WinnerName = "Remis";
            }
        }
        finally
        {
            lock (_activeMatches) _activeMatches.Remove(runner);
            session.IsActive = false;
            runner.StateChanged -= stateHandler;
            await runner.DisposeAsync();
            NotifyMatch(session);
            Notify();
        }
    }

    public async Task<List<TournamentEngineStatistic>> GetStatisticsAsync(Guid tournamentId)
    {
        var tournament = Current?.Id == tournamentId
            ? Current
            : (await tournaments.GetAllAsync()).FirstOrDefault(item => item.Id == tournamentId);
        if (tournament is null) return [];
        var tournamentGames = (await games.GetAllAsync()).Where(item => item.TournamentId == tournamentId).ToList();
        return CalculateStatistics(tournament, tournamentGames);
    }

    private static List<TournamentEngineStatistic> CalculateStatistics(
        EngineTournament tournament, IReadOnlyList<SavedEngineGame> tournamentGames)
    {
        var maximumPoints = Math.Max(1, (tournament.ParticipantIds.Count - 1) * 2);
        var stats = tournament.ParticipantIds.Select((id, index) => new TournamentEngineStatistic
        {
            EngineId = id,
            EngineName = tournament.ParticipantNames.ElementAtOrDefault(index) ?? "Silnik",
            MaximumPoints = maximumPoints
        }).ToDictionary(item => item.EngineId);

        foreach (var match in tournament.Matches.Where(item => item.Status == TournamentMatchStatus.Completed))
        {
            if (match.EngineAId is not { } whiteId || match.EngineBId is not { } blackId ||
                !stats.TryGetValue(whiteId, out var white) || !stats.TryGetValue(blackId, out var black)) continue;
            white.Played++; black.Played++;
            if (match.Result == "1-0") { white.Wins++; black.Losses++; white.Points++; white.WhiteWins++; }
            else if (match.Result == "0-1") { black.Wins++; white.Losses++; black.Points++; black.BlackWins++; }
            else { white.Draws++; black.Draws++; white.Points += .5; black.Points += .5; }
        }

        foreach (var item in stats.Values)
        {
            var ownGames = tournamentGames.Where(game => game.WhiteEngineId == item.EngineId || game.BlackEngineId == item.EngineId).ToList();
            item.AverageMoves = ownGames.Count == 0 ? 0 : ownGames.Average(game => game.MoveCount / 2d);
            var analysisCount = 0;
            var depthCount = 0;
            double totalDepth = 0;
            double totalMoveTime = 0;
            foreach (var analysis in ownGames.SelectMany(game => game.Analysis))
            {
                if (analysis.EngineName != item.EngineName) continue;
                analysisCount++;
                totalMoveTime += analysis.ElapsedMilliseconds;
                if (analysis.Depth is { } depth)
                {
                    depthCount++;
                    totalDepth += depth;
                }
            }
            item.AverageDepth = depthCount == 0 ? 0 : totalDepth / depthCount;
            item.AverageMoveTimeMilliseconds = analysisCount == 0 ? null : totalMoveTime / analysisCount;
        }
        var ordered = stats.Values.OrderByDescending(item => item.Points).ThenByDescending(item => item.Wins).ThenBy(item => item.EngineName).ToList();
        for (var index = 0; index < ordered.Count; index++) ordered[index].Place = index + 1;
        return ordered;
    }

    private async Task SaveIfEnabledAsync(EngineTournament tournament, CancellationToken token)
    {
        // Szkic nie trafia do repozytorium. Od chwili startu stan turnieju jest jednak
        // zawsze zapisywany, aby zakładka mogła go przywrócić po ponownym uruchomieniu.
        // SaveLocally steruje zapisem powtórek poszczególnych partii.
        await tournaments.SaveAsync(tournament, token);
    }

    private static void BuildSchedule(EngineTournament tournament, IReadOnlyList<EngineDefinition> engines)
    {
        var participants = engines.Cast<EngineDefinition?>().ToList();
        if (participants.Count % 2 != 0) participants.Add(null);
        var count = participants.Count;
        var firstLeg = new List<TournamentMatch>();
        for (var round = 0; round < count - 1; round++)
        {
            var index = 0;
            for (var pair = 0; pair < count / 2; pair++)
            {
                var left = participants[pair];
                var right = participants[count - 1 - pair];
                if (left is not null && right is not null)
                {
                    var white = (round + pair) % 2 == 0 ? left : right;
                    var black = ReferenceEquals(white, left) ? right : left;
                    firstLeg.Add(new TournamentMatch
                    {
                        Round = round + 1,
                        Index = index++,
                        EngineAId = white.Id,
                        EngineAName = white.Name,
                        EngineBId = black.Id,
                        EngineBName = black.Name,
                        Status = TournamentMatchStatus.Pending
                    });
                }
            }
            var fixedEngine = participants[0];
            var rotating = participants.Skip(1).ToList();
            rotating.Insert(0, rotating[^1]);
            rotating.RemoveAt(rotating.Count - 1);
            participants = [fixedEngine, .. rotating];
        }

        tournament.Matches = firstLeg.Concat(firstLeg.Select(match => new TournamentMatch
        {
            Round = match.Round + count - 1,
            Index = match.Index,
            EngineAId = match.EngineBId,
            EngineAName = match.EngineBName,
            EngineBId = match.EngineAId,
            EngineBName = match.EngineAName,
            Status = TournamentMatchStatus.Pending
        })).ToList();
        tournament.CurrentRound = 0;
    }

    private void Notify() => StateChanged?.Invoke(this, EventArgs.Empty);
    private void NotifyMatch(TournamentMatchSession session) => MatchStateChanged?.Invoke(
        this, new TournamentMatchStateChangedEventArgs(session.Match.Id, session.Runner.Game.MoveHistory.Count));
}
