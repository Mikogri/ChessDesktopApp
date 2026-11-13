namespace ChessDesktopApp.Models;

public sealed class EngineDefinition
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "Silnik UCI";
    public string ExecutablePath { get; set; } = string.Empty;
    public string? WorkingDirectory { get; set; }
    public Dictionary<string, string> UciOptions { get; set; } = [];
    public DateTimeOffset AddedAt { get; set; } = DateTimeOffset.Now;
    public DateTimeOffset? LastValidatedAt { get; set; }
    public string Status { get; set; } = "Nie sprawdzono";
    public bool IsBundled { get; set; }
    public string DisplayPath => ExecutablePath;
    public bool IsAvailable => File.Exists(ExecutablePath);
}

public sealed record EngineValidationResult(bool IsValid, string Name, string Message);

public sealed class EngineAnalysisSnapshot
{
    public int Ply { get; set; }
    public string EngineName { get; set; } = string.Empty;
    public int? WhiteCentipawns { get; set; }
    public int? WhiteMateIn { get; set; }
    public int? Depth { get; set; }
    public long? Nodes { get; set; }
    public long ElapsedMilliseconds { get; set; }
}

public sealed class SavedEngineGame
{
    public int SchemaVersion { get; set; } = 1;
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid? TournamentId { get; set; }
    public Guid? TournamentMatchId { get; set; }
    public string WhiteEngine { get; set; } = string.Empty;
    public string BlackEngine { get; set; } = string.Empty;
    public Guid WhiteEngineId { get; set; }
    public Guid BlackEngineId { get; set; }
    public DateTimeOffset StartedAt { get; set; }
    public DateTimeOffset FinishedAt { get; set; }
    public string Result { get; set; } = "*";
    public string ResultReason { get; set; } = string.Empty;
    public string GameKind { get; set; } = "EngineMatch";
    public string FormatDescription { get; set; } = string.Empty;
    public int InitialTimeSeconds { get; set; }
    public int IncrementSeconds { get; set; }
    public string Pgn { get; set; } = string.Empty;
    public List<string> UciMoves { get; set; } = [];
    public List<EngineAnalysisSnapshot> Analysis { get; set; } = [];
    public int MoveCount => UciMoves.Count;
    public string DateText => FinishedAt.LocalDateTime.ToString("dd.MM.yyyy HH:mm");
    public bool IsPlayerGame => GameKind == "PlayerVsEngine";
    public string PairingText => $"{WhiteEngine} — {BlackEngine}";
}
