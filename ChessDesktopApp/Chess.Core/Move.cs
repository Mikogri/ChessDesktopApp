namespace Chess.Core;

[Flags]
public enum MoveFlags
{
    None = 0,
    Capture = 1,
    EnPassant = 2,
    CastleKingSide = 4,
    CastleQueenSide = 8,
    PawnDoublePush = 16,
    Promotion = 32
}

public readonly record struct Move(Square From, Square To, PieceType? Promotion = null, MoveFlags Flags = MoveFlags.None)
{
    public string Uci => From.Name + To.Name + (Promotion is null ? string.Empty : PromotionSymbol(Promotion.Value));

    public bool IsCapture => Flags.HasFlag(MoveFlags.Capture);

    public static bool TryParseUci(string? value, out Square from, out Square to, out PieceType? promotion)
    {
        from = default;
        to = default;
        promotion = null;
        if (value is null || value.Length is < 4 or > 5 ||
            !Square.TryParse(value[..2], out from) || !Square.TryParse(value.Substring(2, 2), out to))
        {
            return false;
        }

        if (value.Length == 5)
        {
            promotion = char.ToLowerInvariant(value[4]) switch
            {
                'q' => PieceType.Queen,
                'r' => PieceType.Rook,
                'b' => PieceType.Bishop,
                'n' => PieceType.Knight,
                _ => null
            };

            if (promotion is null)
            {
                return false;
            }
        }

        return true;
    }

    private static string PromotionSymbol(PieceType type) => type switch
    {
        PieceType.Queen => "q",
        PieceType.Rook => "r",
        PieceType.Bishop => "b",
        PieceType.Knight => "n",
        _ => throw new ArgumentException("Only queen, rook, bishop or knight promotion is legal.", nameof(type))
    };

    public override string ToString() => Uci;
}

public sealed record MoveRecord(
    int MoveNumber,
    PieceColor Color,
    Move Move,
    Piece MovingPiece,
    Piece? CapturedPiece,
    string Notation);
