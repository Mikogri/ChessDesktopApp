using System.Text.Json.Serialization;
using ChessDesktopApp.Styling;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ChessDesktopApp.Models;

public enum TournamentStatus
{
    Draft,
    Running,
    Paused,
    Completed,
    Interrupted
}

public enum TournamentMatchStatus
{
    Pending,
    Running,
    Completed,
    Bye,
    Interrupted
}

public sealed class TournamentMatch
{
    private static readonly SolidColorBrush LiveStatusBrush = ChessThemeResources.Brush(
        "ChessStatusLiveBrush", Color.FromArgb(0xFF, 0xE7, 0x48, 0x56));
    private static readonly SolidColorBrush CompletedStatusBrush = ChessThemeResources.Brush(
        "ChessStatusMatchCompletedBrush", Color.FromArgb(0xFF, 0x6C, 0xCB, 0x7F));
    private static readonly SolidColorBrush PendingStatusBrush = ChessThemeResources.Brush(
        "ChessStatusNeutralBrush", Color.FromArgb(0xFF, 0x8A, 0x8A, 0x8A));
    private static readonly SolidColorBrush InterruptedStatusBrush = ChessThemeResources.Brush(
        "ChessDangerBrush", Color.FromArgb(0xFF, 0xC4, 0x2B, 0x1C));

    public Guid Id { get; set; } = Guid.NewGuid();
    public int Round { get; set; }
    public int Index { get; set; }
    public Guid? EngineAId { get; set; }
    public string EngineAName { get; set; } = "Oczekuje";
    public Guid? EngineBId { get; set; }
    public string EngineBName { get; set; } = "Oczekuje";
    public Guid? WinnerEngineId { get; set; }
    public string WinnerName { get; set; } = string.Empty;
    public string Result { get; set; } = string.Empty;
    public TournamentMatchStatus Status { get; set; }
    public Guid? SavedGameId { get; set; }
    public string RoundText => $"Runda {Round}";
    public string PairingText => $"{EngineAName}  —  {EngineBName}";
    public string ColorText => $"Białe: {EngineAName}";
    public string ResultText => string.IsNullOrWhiteSpace(Result) ? StatusText : Result;
    public string StatusText => Status switch
    {
        TournamentMatchStatus.Pending => "Oczekuje",
        TournamentMatchStatus.Running => "W toku",
        TournamentMatchStatus.Completed => "Zakończony",
        TournamentMatchStatus.Bye => "Wolny los",
        _ => "Przerwany"
    };
    [JsonIgnore]
    public string StatusDisplayText => Status switch
    {
        TournamentMatchStatus.Running => "Na żywo",
        TournamentMatchStatus.Completed => "Zakończony",
        TournamentMatchStatus.Pending => "Nadchodzący",
        TournamentMatchStatus.Bye => "Wolny los",
        _ => "Przerwany"
    };
    [JsonIgnore]
    public SolidColorBrush StatusBrush => Status switch
    {
        TournamentMatchStatus.Running => LiveStatusBrush,
        TournamentMatchStatus.Completed => CompletedStatusBrush,
        TournamentMatchStatus.Pending or TournamentMatchStatus.Bye => PendingStatusBrush,
        _ => InterruptedStatusBrush
    };
}

public sealed class EngineTournament
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = string.Empty;
    public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? StartedAt { get; set; }
    public DateTimeOffset? FinishedAt { get; set; }
    public TournamentStatus Status { get; set; } = TournamentStatus.Draft;
    public int InitialTimeSeconds { get; set; } = 180;
    public int IncrementSeconds { get; set; } = 2;
    public bool AutoStartNextRound { get; set; } = true;
    public bool SaveLocally { get; set; } = true;
    public int CurrentRound { get; set; }
    public List<Guid> ParticipantIds { get; set; } = [];
    public List<string> ParticipantNames { get; set; } = [];
    public List<TournamentMatch> Matches { get; set; } = [];
    public Guid? WinnerEngineId { get; set; }
    public string WinnerName { get; set; } = string.Empty;
    public string DateText => CreatedAt.LocalDateTime.ToString("dd.MM.yyyy HH:mm");
    public string StatusText => Status switch
    {
        TournamentStatus.Draft => "Projekt",
        TournamentStatus.Running => "W toku",
        TournamentStatus.Paused => "Wstrzymany",
        TournamentStatus.Completed => "Zakończony",
        _ => "Przerwany"
    };
    public int ParticipantCount => ParticipantIds.Count;
    public int RoundCount => Matches.Count == 0 ? 0 : Matches.Max(item => item.Round);
    public int CompletedMatchCount => Matches.Count(item => item.Status == TournamentMatchStatus.Completed);
    public string ProgressText => $"{CompletedMatchCount} z {Matches.Count} meczów";
    public string DateRangeText
    {
        get
        {
            var start = (StartedAt ?? CreatedAt).LocalDateTime.ToString("dd.MM.yyyy HH:mm");
            var end = FinishedAt?.LocalDateTime.ToString("dd.MM.yyyy HH:mm") ?? "w toku";
            return $"{start}  —  {end}";
        }
    }
    public string SummaryText => $"{ParticipantCount} silników  ·  {RoundCount} rund  ·  {Matches.Count} meczów";
    [JsonIgnore]
    public string DisplayWinnerName
    {
        get
        {
            if (Status != TournamentStatus.Completed) return string.IsNullOrWhiteSpace(WinnerName) ? "--" : WinnerName;
            var points = ParticipantIds.Distinct().ToDictionary(id => id, _ => 0d);
            foreach (var match in Matches.Where(item => item.Status == TournamentMatchStatus.Completed))
            {
                if (match.EngineAId is not { } whiteId || match.EngineBId is not { } blackId ||
                    !points.ContainsKey(whiteId) || !points.ContainsKey(blackId)) continue;
                if (match.Result == "1-0") points[whiteId] += 1;
                else if (match.Result == "0-1") points[blackId] += 1;
                else if (match.Result == "1/2-1/2")
                {
                    points[whiteId] += 0.5;
                    points[blackId] += 0.5;
                }
            }
            if (points.Count == 0) return "--";
            var topPoints = points.Values.Max();
            var leaders = points.Where(item => Math.Abs(item.Value - topPoints) < 0.001).Select(item => item.Key).ToList();
            if (leaders.Count != 1) return "--";
            var index = ParticipantIds.IndexOf(leaders[0]);
            return index >= 0 ? ParticipantNames.ElementAtOrDefault(index) ?? "--" : "--";
        }
    }
}

public sealed class TournamentEngineStatistic
{
    public Guid EngineId { get; set; }
    public string EngineName { get; set; } = string.Empty;
    public int Played { get; set; }
    public int Wins { get; set; }
    public int Draws { get; set; }
    public int Losses { get; set; }
    public double Points { get; set; }
    public double WinPercentage => Played == 0 ? 0 : 100d * Wins / Played;
    public double AverageMoves { get; set; }
    public double? AverageDepth { get; set; }
    public double? AverageMoveTimeMilliseconds { get; set; }
    public int WhiteWins { get; set; }
    public int BlackWins { get; set; }
    public int Place { get; set; }
    public double MaximumPoints { get; set; } = 1;
    public string RecordText => $"{Wins}-{Draws}-{Losses}";
    public string PointsText => Points.ToString("0.0");
    public string WinRateText => $"{WinPercentage:0}%";
    public string DetailedRecordText => $"{Wins} wygr.  ·  {Draws} rem.  ·  {Losses} por.";
}
