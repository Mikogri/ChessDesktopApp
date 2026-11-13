namespace Chess.Core;

public enum PieceColor
{
    White,
    Black
}

public enum PieceType
{
    Pawn,
    Knight,
    Bishop,
    Rook,
    Queen,
    King
}

public readonly record struct Piece(PieceColor Color, PieceType Type)
{
    public char FenSymbol => (Color == PieceColor.White ? char.ToUpperInvariant(TypeSymbol) : TypeSymbol);

    private char TypeSymbol => Type switch
    {
        PieceType.Pawn => 'p',
        PieceType.Knight => 'n',
        PieceType.Bishop => 'b',
        PieceType.Rook => 'r',
        PieceType.Queen => 'q',
        PieceType.King => 'k',
        _ => throw new ArgumentOutOfRangeException()
    };

    public static Piece FromFen(char symbol)
    {
        var color = char.IsUpper(symbol) ? PieceColor.White : PieceColor.Black;
        var type = char.ToLowerInvariant(symbol) switch
        {
            'p' => PieceType.Pawn,
            'n' => PieceType.Knight,
            'b' => PieceType.Bishop,
            'r' => PieceType.Rook,
            'q' => PieceType.Queen,
            'k' => PieceType.King,
            _ => throw new FormatException($"Unknown FEN piece '{symbol}'.")
        };

        return new Piece(color, type);
    }
}
