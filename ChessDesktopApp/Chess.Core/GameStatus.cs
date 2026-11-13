namespace Chess.Core;

public enum GameStatusKind
{
    InProgress,
    Check,
    Checkmate,
    Stalemate,
    DrawByInsufficientMaterial,
    DrawByFiftyMoveRule,
    DrawByThreefoldRepetition,
    Resignation
}

public readonly record struct GameStatus(GameStatusKind Kind, PieceColor? Winner = null)
{
    public bool IsGameOver => Kind is not GameStatusKind.InProgress and not GameStatusKind.Check;
}
