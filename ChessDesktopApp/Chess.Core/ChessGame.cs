using System.Text;

namespace Chess.Core;

public sealed class ChessGame
{
    public const string InitialFen = "rnbqkbnr/pppppppp/8/8/8/8/PPPPPPPP/RNBQKBNR w KQkq - 0 1";

    private static readonly (int File, int Rank)[] KnightOffsets =
    [
        (1, 2), (2, 1), (2, -1), (1, -2),
        (-1, -2), (-2, -1), (-2, 1), (-1, 2)
    ];

    private static readonly (int File, int Rank)[] KingOffsets =
    [
        (1, 0), (1, 1), (0, 1), (-1, 1),
        (-1, 0), (-1, -1), (0, -1), (1, -1)
    ];

    private readonly Piece?[] _board = new Piece?[64];
    private readonly Dictionary<string, int> _positionOccurrences = new(StringComparer.Ordinal);
    private readonly List<Piece> _capturedPieces = [];
    private readonly List<MoveRecord> _moveHistory = [];

    private ChessGame()
    {
    }

    public PieceColor SideToMove { get; private set; }
    public bool WhiteCanCastleKingSide { get; private set; }
    public bool WhiteCanCastleQueenSide { get; private set; }
    public bool BlackCanCastleKingSide { get; private set; }
    public bool BlackCanCastleQueenSide { get; private set; }
    public Square? EnPassantTarget { get; private set; }
    public int HalfmoveClock { get; private set; }
    public int FullmoveNumber { get; private set; } = 1;
    public Move? LastMove { get; private set; }
    public IReadOnlyList<Piece> CapturedPieces => _capturedPieces;
    public IReadOnlyList<MoveRecord> MoveHistory => _moveHistory;

    public Piece? this[Square square] => _board[square.Index];

    public static ChessGame CreateInitial() => FromFen(InitialFen);

    public static ChessGame FromFen(string fen)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(fen);
        var fields = fen.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (fields.Length != 6)
        {
            throw new FormatException("A FEN position must contain six fields.");
        }

        var game = new ChessGame();
        var ranks = fields[0].Split('/');
        if (ranks.Length != 8)
        {
            throw new FormatException("The FEN board must contain eight ranks.");
        }

        for (var fenRank = 0; fenRank < 8; fenRank++)
        {
            var file = 0;
            foreach (var symbol in ranks[fenRank])
            {
                if (symbol is >= '1' and <= '8')
                {
                    file += symbol - '0';
                }
                else
                {
                    if (file > 7)
                    {
                        throw new FormatException("A FEN rank contains too many squares.");
                    }

                    game._board[new Square(file++, 7 - fenRank).Index] = Piece.FromFen(symbol);
                }
            }

            if (file != 8)
            {
                throw new FormatException("Every FEN rank must contain eight squares.");
            }
        }

        game.SideToMove = fields[1] switch
        {
            "w" => PieceColor.White,
            "b" => PieceColor.Black,
            _ => throw new FormatException("Invalid FEN side to move.")
        };
        game.WhiteCanCastleKingSide = fields[2].Contains('K');
        game.WhiteCanCastleQueenSide = fields[2].Contains('Q');
        game.BlackCanCastleKingSide = fields[2].Contains('k');
        game.BlackCanCastleQueenSide = fields[2].Contains('q');
        game.EnPassantTarget = fields[3] == "-" ? null : Square.Parse(fields[3]);

        if (!int.TryParse(fields[4], out var halfmove) || halfmove < 0 ||
            !int.TryParse(fields[5], out var fullmove) || fullmove < 1)
        {
            throw new FormatException("Invalid FEN move counters.");
        }

        game.HalfmoveClock = halfmove;
        game.FullmoveNumber = fullmove;
        game.ValidateKings();
        game.RegisterCurrentPosition();
        return game;
    }

    public IReadOnlyList<Move> GetLegalMoves() => GetLegalMovesCore(null);

    public IReadOnlyList<Move> GetLegalMovesFrom(Square square) => GetLegalMovesCore(square);

    private IReadOnlyList<Move> GetLegalMovesCore(Square? from)
    {
        var legal = new List<Move>(48);
        var movingColor = SideToMove;
        foreach (var move in GeneratePseudoLegalMoves())
        {
            // Filter before cloning and checking the resulting position.
            if (from is { } square && move.From != square) continue;
            var copy = Clone();
            copy.ApplyUnchecked(move, registerPosition: false);
            if (!copy.IsKingInCheck(movingColor))
            {
                legal.Add(move);
            }
        }

        return legal;
    }

    public bool TryMakeMove(Move requestedMove, out Move appliedMove)
    {
        foreach (var legalMove in GetLegalMoves())
        {
            if (legalMove.From == requestedMove.From &&
                legalMove.To == requestedMove.To &&
                legalMove.Promotion == requestedMove.Promotion)
            {
                var movingPiece = _board[legalMove.From.Index]!.Value;
                var capturedPiece = legalMove.Flags.HasFlag(MoveFlags.EnPassant)
                    ? _board[new Square(legalMove.To.File, legalMove.From.Rank).Index]
                    : _board[legalMove.To.Index];
                var moveNumber = FullmoveNumber;
                var movingColor = SideToMove;
                appliedMove = legalMove;
                ApplyUnchecked(legalMove, registerPosition: true);
                var status = GetStatus();
                _moveHistory.Add(new MoveRecord(
                    moveNumber,
                    movingColor,
                    legalMove,
                    movingPiece,
                    capturedPiece,
                    FormatMoveNotation(legalMove, movingPiece, status)));
                return true;
            }
        }

        appliedMove = default;
        return false;
    }

    public bool TryMakeUciMove(string uci, out Move appliedMove)
    {
        if (!Move.TryParseUci(uci, out var from, out var to, out var promotion))
        {
            appliedMove = default;
            return false;
        }

        return TryMakeMove(new Move(from, to, promotion), out appliedMove);
    }

    public bool IsKingInCheck(PieceColor color)
    {
        var kingIndex = Array.FindIndex(_board, piece => piece is { Type: PieceType.King } && piece.Value.Color == color);
        return kingIndex >= 0 && IsSquareAttacked(Square.FromIndex(kingIndex), Opposite(color));
    }

    public GameStatus GetStatus()
    {
        var inCheck = IsKingInCheck(SideToMove);
        if (GetLegalMoves().Count == 0)
        {
            return inCheck
                ? new GameStatus(GameStatusKind.Checkmate, Opposite(SideToMove))
                : new GameStatus(GameStatusKind.Stalemate);
        }

        if (HalfmoveClock >= 100)
        {
            return new GameStatus(GameStatusKind.DrawByFiftyMoveRule);
        }

        if (_positionOccurrences.TryGetValue(GetPositionKey(), out var occurrences) && occurrences >= 3)
        {
            return new GameStatus(GameStatusKind.DrawByThreefoldRepetition);
        }

        if (HasInsufficientMaterial())
        {
            return new GameStatus(GameStatusKind.DrawByInsufficientMaterial);
        }

        return new GameStatus(inCheck ? GameStatusKind.Check : GameStatusKind.InProgress);
    }

    public string ToFen()
    {
        var board = BuildBoardFen();
        var castling = new StringBuilder(4);
        if (WhiteCanCastleKingSide) castling.Append('K');
        if (WhiteCanCastleQueenSide) castling.Append('Q');
        if (BlackCanCastleKingSide) castling.Append('k');
        if (BlackCanCastleQueenSide) castling.Append('q');

        return $"{board} {(SideToMove == PieceColor.White ? "w" : "b")} " +
               $"{(castling.Length == 0 ? "-" : castling)} {EnPassantTarget?.Name ?? "-"} {HalfmoveClock} {FullmoveNumber}";
    }

    private IEnumerable<Move> GeneratePseudoLegalMoves()
    {
        for (var index = 0; index < _board.Length; index++)
        {
            var piece = _board[index];
            if (piece is null || piece.Value.Color != SideToMove)
            {
                continue;
            }

            var from = Square.FromIndex(index);
            var moves = piece.Value.Type switch
            {
                PieceType.Pawn => GeneratePawnMoves(from, piece.Value.Color),
                PieceType.Knight => GenerateJumpMoves(from, piece.Value.Color, KnightOffsets),
                PieceType.Bishop => GenerateSlidingMoves(from, piece.Value.Color, [(1, 1), (1, -1), (-1, 1), (-1, -1)]),
                PieceType.Rook => GenerateSlidingMoves(from, piece.Value.Color, [(1, 0), (-1, 0), (0, 1), (0, -1)]),
                PieceType.Queen => GenerateSlidingMoves(from, piece.Value.Color, [(1, 1), (1, -1), (-1, 1), (-1, -1), (1, 0), (-1, 0), (0, 1), (0, -1)]),
                PieceType.King => GenerateKingMoves(from, piece.Value.Color),
                _ => []
            };

            foreach (var move in moves)
            {
                yield return move;
            }
        }
    }

    private IEnumerable<Move> GeneratePawnMoves(Square from, PieceColor color)
    {
        var direction = color == PieceColor.White ? 1 : -1;
        var startRank = color == PieceColor.White ? 1 : 6;
        var promotionRank = color == PieceColor.White ? 7 : 0;
        var oneRank = from.Rank + direction;

        if (IsOnBoard(from.File, oneRank) && _board[new Square(from.File, oneRank).Index] is null)
        {
            foreach (var move in CreatePawnMoves(from, new Square(from.File, oneRank), MoveFlags.None, promotionRank))
            {
                yield return move;
            }

            var twoRank = from.Rank + (2 * direction);
            if (from.Rank == startRank && _board[new Square(from.File, twoRank).Index] is null)
            {
                yield return new Move(from, new Square(from.File, twoRank), null, MoveFlags.PawnDoublePush);
            }
        }

        foreach (var fileDelta in new[] { -1, 1 })
        {
            var targetFile = from.File + fileDelta;
            if (!IsOnBoard(targetFile, oneRank))
            {
                continue;
            }

            var target = new Square(targetFile, oneRank);
            var targetPiece = _board[target.Index];
            if (targetPiece is not null && targetPiece.Value.Color != color)
            {
                foreach (var move in CreatePawnMoves(from, target, MoveFlags.Capture, promotionRank))
                {
                    yield return move;
                }
            }
            else if (EnPassantTarget == target)
            {
                var captured = _board[new Square(target.File, from.Rank).Index];
                if (captured is { Type: PieceType.Pawn } && captured.Value.Color != color)
                {
                    yield return new Move(from, target, null, MoveFlags.Capture | MoveFlags.EnPassant);
                }
            }
        }
    }

    private static IEnumerable<Move> CreatePawnMoves(Square from, Square to, MoveFlags flags, int promotionRank)
    {
        if (to.Rank != promotionRank)
        {
            yield return new Move(from, to, null, flags);
            yield break;
        }

        foreach (var type in new[] { PieceType.Queen, PieceType.Rook, PieceType.Bishop, PieceType.Knight })
        {
            yield return new Move(from, to, type, flags | MoveFlags.Promotion);
        }
    }

    private IEnumerable<Move> GenerateJumpMoves(Square from, PieceColor color, IEnumerable<(int File, int Rank)> offsets)
    {
        foreach (var (fileOffset, rankOffset) in offsets)
        {
            var file = from.File + fileOffset;
            var rank = from.Rank + rankOffset;
            if (!IsOnBoard(file, rank))
            {
                continue;
            }

            var to = new Square(file, rank);
            var target = _board[to.Index];
            if (target is null)
            {
                yield return new Move(from, to);
            }
            else if (target.Value.Color != color)
            {
                yield return new Move(from, to, null, MoveFlags.Capture);
            }
        }
    }

    private IEnumerable<Move> GenerateSlidingMoves(Square from, PieceColor color, IEnumerable<(int File, int Rank)> directions)
    {
        foreach (var (fileDirection, rankDirection) in directions)
        {
            var file = from.File + fileDirection;
            var rank = from.Rank + rankDirection;
            while (IsOnBoard(file, rank))
            {
                var to = new Square(file, rank);
                var target = _board[to.Index];
                if (target is null)
                {
                    yield return new Move(from, to);
                }
                else
                {
                    if (target.Value.Color != color)
                    {
                        yield return new Move(from, to, null, MoveFlags.Capture);
                    }

                    break;
                }

                file += fileDirection;
                rank += rankDirection;
            }
        }
    }

    private IEnumerable<Move> GenerateKingMoves(Square from, PieceColor color)
    {
        foreach (var move in GenerateJumpMoves(from, color, KingOffsets))
        {
            yield return move;
        }

        var homeRank = color == PieceColor.White ? 0 : 7;
        if (from != new Square(4, homeRank) || IsKingInCheck(color))
        {
            yield break;
        }

        var opponent = Opposite(color);
        var canCastleKingSide = color == PieceColor.White ? WhiteCanCastleKingSide : BlackCanCastleKingSide;
        if (canCastleKingSide &&
            HasRook(new Square(7, homeRank), color) &&
            _board[new Square(5, homeRank).Index] is null &&
            _board[new Square(6, homeRank).Index] is null &&
            !IsSquareAttacked(new Square(5, homeRank), opponent) &&
            !IsSquareAttacked(new Square(6, homeRank), opponent))
        {
            yield return new Move(from, new Square(6, homeRank), null, MoveFlags.CastleKingSide);
        }

        var canCastleQueenSide = color == PieceColor.White ? WhiteCanCastleQueenSide : BlackCanCastleQueenSide;
        if (canCastleQueenSide &&
            HasRook(new Square(0, homeRank), color) &&
            _board[new Square(1, homeRank).Index] is null &&
            _board[new Square(2, homeRank).Index] is null &&
            _board[new Square(3, homeRank).Index] is null &&
            !IsSquareAttacked(new Square(3, homeRank), opponent) &&
            !IsSquareAttacked(new Square(2, homeRank), opponent))
        {
            yield return new Move(from, new Square(2, homeRank), null, MoveFlags.CastleQueenSide);
        }
    }

    private bool IsSquareAttacked(Square square, PieceColor byColor)
    {
        var pawnSourceRank = square.Rank + (byColor == PieceColor.White ? -1 : 1);
        foreach (var fileDelta in new[] { -1, 1 })
        {
            var sourceFile = square.File + fileDelta;
            if (IsOnBoard(sourceFile, pawnSourceRank) &&
                _board[new Square(sourceFile, pawnSourceRank).Index] is { Type: PieceType.Pawn } pawn &&
                pawn.Color == byColor)
            {
                return true;
            }
        }

        if (IsAttackedByJumpingPiece(square, byColor, PieceType.Knight, KnightOffsets) ||
            IsAttackedByJumpingPiece(square, byColor, PieceType.King, KingOffsets))
        {
            return true;
        }

        return IsAttackedOnRay(square, byColor, [(1, 0), (-1, 0), (0, 1), (0, -1)], PieceType.Rook) ||
               IsAttackedOnRay(square, byColor, [(1, 1), (1, -1), (-1, 1), (-1, -1)], PieceType.Bishop);
    }

    private bool IsAttackedByJumpingPiece(
        Square square,
        PieceColor color,
        PieceType type,
        IEnumerable<(int File, int Rank)> offsets)
    {
        foreach (var (fileOffset, rankOffset) in offsets)
        {
            var file = square.File + fileOffset;
            var rank = square.Rank + rankOffset;
            if (IsOnBoard(file, rank) &&
                _board[new Square(file, rank).Index] is { } piece &&
                piece.Color == color && piece.Type == type)
            {
                return true;
            }
        }

        return false;
    }

    private bool IsAttackedOnRay(
        Square square,
        PieceColor color,
        IEnumerable<(int File, int Rank)> directions,
        PieceType requiredType)
    {
        foreach (var (fileDirection, rankDirection) in directions)
        {
            var file = square.File + fileDirection;
            var rank = square.Rank + rankDirection;
            while (IsOnBoard(file, rank))
            {
                var piece = _board[new Square(file, rank).Index];
                if (piece is not null)
                {
                    if (piece.Value.Color == color &&
                        (piece.Value.Type == requiredType || piece.Value.Type == PieceType.Queen))
                    {
                        return true;
                    }

                    break;
                }

                file += fileDirection;
                rank += rankDirection;
            }
        }

        return false;
    }

    private void ApplyUnchecked(Move move, bool registerPosition)
    {
        var piece = _board[move.From.Index]!.Value;
        var captured = _board[move.To.Index];

        if (move.Flags.HasFlag(MoveFlags.EnPassant))
        {
            var capturedSquare = new Square(move.To.File, move.From.Rank);
            captured = _board[capturedSquare.Index];
            _board[capturedSquare.Index] = null;
        }

        _board[move.From.Index] = null;
        _board[move.To.Index] = move.Promotion is { } promotion
            ? new Piece(piece.Color, promotion)
            : piece;

        if (captured is { } capturedPiece)
        {
            _capturedPieces.Add(capturedPiece);
        }

        if (move.Flags.HasFlag(MoveFlags.CastleKingSide))
        {
            MoveRookForCastle(piece.Color, kingSide: true);
        }
        else if (move.Flags.HasFlag(MoveFlags.CastleQueenSide))
        {
            MoveRookForCastle(piece.Color, kingSide: false);
        }

        UpdateCastlingRights(piece, move.From, move.To, captured);
        EnPassantTarget = move.Flags.HasFlag(MoveFlags.PawnDoublePush)
            ? new Square(move.From.File, (move.From.Rank + move.To.Rank) / 2)
            : null;
        HalfmoveClock = piece.Type == PieceType.Pawn || captured is not null ? 0 : HalfmoveClock + 1;
        if (SideToMove == PieceColor.Black)
        {
            FullmoveNumber++;
        }

        SideToMove = Opposite(SideToMove);
        LastMove = move;
        if (registerPosition)
        {
            RegisterCurrentPosition();
        }
    }

    private void MoveRookForCastle(PieceColor color, bool kingSide)
    {
        var rank = color == PieceColor.White ? 0 : 7;
        var from = new Square(kingSide ? 7 : 0, rank);
        var to = new Square(kingSide ? 5 : 3, rank);
        _board[to.Index] = _board[from.Index];
        _board[from.Index] = null;
    }

    private void UpdateCastlingRights(Piece movingPiece, Square from, Square to, Piece? captured)
    {
        if (movingPiece.Type == PieceType.King)
        {
            if (movingPiece.Color == PieceColor.White)
            {
                WhiteCanCastleKingSide = WhiteCanCastleQueenSide = false;
            }
            else
            {
                BlackCanCastleKingSide = BlackCanCastleQueenSide = false;
            }
        }

        if (movingPiece.Type == PieceType.Rook)
        {
            DisableCastlingRightForRookSquare(from);
        }

        if (captured is { Type: PieceType.Rook })
        {
            DisableCastlingRightForRookSquare(to);
        }
    }

    private void DisableCastlingRightForRookSquare(Square square)
    {
        if (square == new Square(0, 0)) WhiteCanCastleQueenSide = false;
        if (square == new Square(7, 0)) WhiteCanCastleKingSide = false;
        if (square == new Square(0, 7)) BlackCanCastleQueenSide = false;
        if (square == new Square(7, 7)) BlackCanCastleKingSide = false;
    }

    private bool HasInsufficientMaterial()
    {
        var nonKings = _board
            .Select((piece, index) => (Piece: piece, Square: Square.FromIndex(index)))
            .Where(item => item.Piece is not null && item.Piece.Value.Type != PieceType.King)
            .ToArray();

        if (nonKings.Length == 0)
        {
            return true;
        }

        if (nonKings.Length == 1)
        {
            return nonKings[0].Piece!.Value.Type is PieceType.Bishop or PieceType.Knight;
        }

        return nonKings.All(item => item.Piece!.Value.Type == PieceType.Bishop) &&
               nonKings.Select(item => (item.Square.File + item.Square.Rank) % 2).Distinct().Count() == 1;
    }

    private string BuildBoardFen()
    {
        var result = new StringBuilder(72);
        for (var rank = 7; rank >= 0; rank--)
        {
            var empty = 0;
            for (var file = 0; file < 8; file++)
            {
                var piece = _board[new Square(file, rank).Index];
                if (piece is null)
                {
                    empty++;
                    continue;
                }

                if (empty > 0)
                {
                    result.Append(empty);
                    empty = 0;
                }

                result.Append(piece.Value.FenSymbol);
            }

            if (empty > 0) result.Append(empty);
            if (rank > 0) result.Append('/');
        }

        return result.ToString();
    }

    private string GetPositionKey()
    {
        var fen = ToFen().Split(' ');
        var enPassant = EnPassantTarget is not null && GetLegalMoves().Any(move => move.Flags.HasFlag(MoveFlags.EnPassant))
            ? fen[3]
            : "-";
        return string.Join(' ', fen[0], fen[1], fen[2], enPassant);
    }

    private void RegisterCurrentPosition()
    {
        var key = GetPositionKey();
        _positionOccurrences[key] = _positionOccurrences.GetValueOrDefault(key) + 1;
    }

    private ChessGame Clone()
    {
        var copy = new ChessGame
        {
            SideToMove = SideToMove,
            WhiteCanCastleKingSide = WhiteCanCastleKingSide,
            WhiteCanCastleQueenSide = WhiteCanCastleQueenSide,
            BlackCanCastleKingSide = BlackCanCastleKingSide,
            BlackCanCastleQueenSide = BlackCanCastleQueenSide,
            EnPassantTarget = EnPassantTarget,
            HalfmoveClock = HalfmoveClock,
            FullmoveNumber = FullmoveNumber,
            LastMove = LastMove
        };
        Array.Copy(_board, copy._board, _board.Length);
        foreach (var occurrence in _positionOccurrences)
        {
            copy._positionOccurrences.Add(occurrence.Key, occurrence.Value);
        }
        copy._capturedPieces.AddRange(_capturedPieces);
        copy._moveHistory.AddRange(_moveHistory);

        return copy;
    }

    private void ValidateKings()
    {
        foreach (var color in new[] { PieceColor.White, PieceColor.Black })
        {
            if (_board.Count(piece => piece is { Type: PieceType.King } && piece.Value.Color == color) != 1)
            {
                throw new FormatException("A legal chess position must contain exactly one king of each color.");
            }
        }
    }

    private bool HasRook(Square square, PieceColor color) =>
        _board[square.Index] is { Type: PieceType.Rook } rook && rook.Color == color;

    private static string FormatMoveNotation(Move move, Piece piece, GameStatus status)
    {
        var notation = move.Flags.HasFlag(MoveFlags.CastleKingSide)
            ? "O-O"
            : move.Flags.HasFlag(MoveFlags.CastleQueenSide)
                ? "O-O-O"
                : $"{PieceNotation(piece.Type)}{move.From.Name}{(move.IsCapture ? "×" : "–")}{move.To.Name}" +
                  (move.Promotion is { } promotion ? $"={PieceNotation(promotion)}" : string.Empty) +
                  (move.Flags.HasFlag(MoveFlags.EnPassant) ? " e.p." : string.Empty);

        return notation + (status.Kind == GameStatusKind.Checkmate ? "#" : status.Kind == GameStatusKind.Check ? "+" : string.Empty);
    }

    private static string PieceNotation(PieceType type) => type switch
    {
        PieceType.Knight => "N",
        PieceType.Bishop => "B",
        PieceType.Rook => "R",
        PieceType.Queen => "Q",
        PieceType.King => "K",
        _ => string.Empty
    };

    private static bool IsOnBoard(int file, int rank) => file is >= 0 and < 8 && rank is >= 0 and < 8;
    private static PieceColor Opposite(PieceColor color) => color == PieceColor.White ? PieceColor.Black : PieceColor.White;
}
