namespace ChessDesktopApp.Engine;

public sealed record EngineDifficulty(
    string Name,
    string Description,
    int SkillLevel,
    int? Elo,
    int DisplayElo,
    int MoveTimeMilliseconds,
    double RandomMoveChance = 0)
{
    public static IReadOnlyList<EngineDifficulty> Presets { get; } =
    [
        new("Pierwsze kroki", "Bardzo łagodny przeciwnik, dobry do nauki zasad", 0, 1320, 400, 100, 0.82),
        new("Bardzo łatwy", "Często przeoczy figurę lub prostą groźbę", 0, 1320, 700, 140, 0.62),
        new("Łatwy", "Gra prosto i regularnie popełnia błędy", 0, 1320, 1000, 200, 0.38),
        new("Początkujący", "Spokojna gra i częste niedokładności", 0, 1320, 1200, 250, 0.16),
        new("Amator", "Odpowiednik gracza klubowego na początku drogi", 4, 1400, 1400, 400),
        new("Średniozaawansowany", "Solidny przeciwnik taktyczny", 9, 1800, 1800, 650),
        new("Zaawansowany", "Silna, konsekwentna gra", 14, 2200, 2200, 900),
        new("Ekspert", "Bardzo wymagający przeciwnik", 18, 2700, 2700, 1200),
        new("Maksymalny", "Pełna siła lokalnego Stockfisha", 20, null, 3200, 1600)
    ];
}

public sealed record EngineEvaluation(
    int? WhiteCentipawns,
    int? WhiteMateIn,
    int? Depth,
    long? Nodes,
    string? PrincipalVariation)
{
    public string DisplayText => WhiteMateIn is { } mate
        ? mate > 0 ? $"Mat dla białych za {mate}" : $"Mat dla czarnych za {Math.Abs(mate)}"
        : WhiteCentipawns is { } centipawns
            ? $"{centipawns / 100.0:+0.00;-0.00;0.00}"
            : "—";
}

public sealed record EngineMoveResult(string BestMove, EngineEvaluation? Evaluation);

public sealed class ChessEngineException : Exception
{
    public ChessEngineException(string message) : base(message)
    {
    }

    public ChessEngineException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
