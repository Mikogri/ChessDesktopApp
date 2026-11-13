using Chess.Core;
using Xunit;

namespace Chess.Core.Tests;

public sealed class ChessGameTests
{
    [Theory]
    [InlineData(ChessGame.InitialFen)]
    [InlineData("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1")]
    [InlineData("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 1")]
    [InlineData("4k3/P7/8/8/8/8/4r3/4K3 w - - 0 1")]
    public void LegalMovesFrom_MatchesFullGenerationForEverySquare(string fen)
    {
        var game = ChessGame.FromFen(fen);
        var allMoves = game.GetLegalMoves();
        foreach (var file in "abcdefgh")
        foreach (var rank in "12345678")
        {
            var square = Square.Parse($"{file}{rank}");
            Assert.Equal(allMoves.Where(move => move.From == square), game.GetLegalMovesFrom(square));
        }
        Assert.Equal(fen, game.ToFen());
    }

    [Fact]
    public void InitialPosition_AllowsLegalPawnMove()
    {
        var game = ChessGame.CreateInitial();

        var result = game.TryMakeUciMove("e2e4", out var move);

        Assert.True(result);
        Assert.Equal("e2e4", move.Uci);
        Assert.Equal(new Piece(PieceColor.White, PieceType.Pawn), game[Square.Parse("e4")]);
        Assert.Equal(PieceColor.Black, game.SideToMove);
        Assert.Single(game.MoveHistory);
        Assert.Equal("e2–e4", game.MoveHistory[0].Notation);
    }

    [Fact]
    public void InitialPosition_RejectsIllegalMove()
    {
        var game = ChessGame.CreateInitial();

        Assert.False(game.TryMakeUciMove("e2e5", out _));
        Assert.Equal(ChessGame.InitialFen, game.ToFen());
    }

    [Fact]
    public void Position_DetectsCheck()
    {
        var game = ChessGame.FromFen("4k3/8/8/8/8/8/4R3/4K3 b - - 0 1");

        Assert.True(game.IsKingInCheck(PieceColor.Black));
        Assert.Equal(GameStatusKind.Check, game.GetStatus().Kind);
    }

    [Fact]
    public void Position_DetectsCheckmate()
    {
        var game = ChessGame.FromFen("7k/6Q1/6K1/8/8/8/8/8 b - - 0 1");

        var status = game.GetStatus();

        Assert.Equal(GameStatusKind.Checkmate, status.Kind);
        Assert.Equal(PieceColor.White, status.Winner);
    }

    [Fact]
    public void Position_DetectsStalemate()
    {
        var game = ChessGame.FromFen("7k/5Q2/6K1/8/8/8/8/8 b - - 0 1");

        Assert.Equal(GameStatusKind.Stalemate, game.GetStatus().Kind);
    }

    [Fact]
    public void Castling_MovesKingAndRook()
    {
        var game = ChessGame.FromFen("r3k2r/8/8/8/8/8/8/R3K2R w KQkq - 0 1");

        Assert.True(game.TryMakeUciMove("e1g1", out var move));
        Assert.True(move.Flags.HasFlag(MoveFlags.CastleKingSide));
        Assert.Equal(new Piece(PieceColor.White, PieceType.King), game[Square.Parse("g1")]);
        Assert.Equal(new Piece(PieceColor.White, PieceType.Rook), game[Square.Parse("f1")]);
        Assert.Null(game[Square.Parse("h1")]);
    }

    [Fact]
    public void Castling_ThroughAttackedSquareIsRejected()
    {
        var game = ChessGame.FromFen("r3k2r/8/8/8/2b5/8/8/R3K2R w KQkq - 0 1");

        Assert.False(game.TryMakeUciMove("e1g1", out _));
    }

    [Fact]
    public void EnPassant_RemovesCapturedPawn()
    {
        var game = ChessGame.FromFen("4k3/8/8/3pP3/8/8/8/4K3 w - d6 0 2");

        Assert.True(game.TryMakeUciMove("e5d6", out var move));
        Assert.True(move.Flags.HasFlag(MoveFlags.EnPassant));
        Assert.Equal(new Piece(PieceColor.White, PieceType.Pawn), game[Square.Parse("d6")]);
        Assert.Null(game[Square.Parse("d5")]);
        Assert.Equal([new Piece(PieceColor.Black, PieceType.Pawn)], game.CapturedPieces);
    }

    [Fact]
    public void Promotion_ReplacesPawnWithSelectedPiece()
    {
        var game = ChessGame.FromFen("4k3/P7/8/8/8/8/8/4K3 w - - 0 1");

        Assert.True(game.TryMakeUciMove("a7a8n", out _));
        Assert.Equal(new Piece(PieceColor.White, PieceType.Knight), game[Square.Parse("a8")]);
    }

    [Fact]
    public void Position_DetectsInsufficientMaterial()
    {
        var game = ChessGame.FromFen("4k3/8/8/8/8/8/8/4K3 w - - 0 1");

        Assert.Equal(GameStatusKind.DrawByInsufficientMaterial, game.GetStatus().Kind);
    }

    [Fact]
    public void Position_DetectsFiftyMoveRule()
    {
        var game = ChessGame.FromFen("4k3/8/8/8/8/8/R7/4K3 w - - 100 51");

        Assert.Equal(GameStatusKind.DrawByFiftyMoveRule, game.GetStatus().Kind);
    }

    [Fact]
    public void RepeatedPosition_DetectsThreefoldRepetition()
    {
        var game = ChessGame.CreateInitial();
        var moves = new[] { "g1f3", "g8f6", "f3g1", "f6g8", "g1f3", "g8f6", "f3g1", "f6g8" };

        foreach (var move in moves)
        {
            Assert.True(game.TryMakeUciMove(move, out _));
        }

        Assert.Equal(GameStatusKind.DrawByThreefoldRepetition, game.GetStatus().Kind);
    }

    [Fact]
    public void InitialPosition_MatchesKnownPerftDepthThree()
    {
        Assert.Equal(8902, CountPositions(ChessGame.CreateInitial(), 3));
    }

    [Fact]
    public void ComplexPosition_MatchesKnownPerftDepthTwo()
    {
        var game = ChessGame.FromFen("r3k2r/p1ppqpb1/bn2pnp1/3PN3/1p2P3/2N2Q1p/PPPBBPPP/R3K2R w KQkq - 0 1");

        Assert.Equal(2039, CountPositions(game, 2));
    }

    private static long CountPositions(ChessGame game, int depth)
    {
        if (depth == 0)
        {
            return 1;
        }

        long count = 0;
        foreach (var move in game.GetLegalMoves())
        {
            var next = ChessGame.FromFen(game.ToFen());
            Assert.True(next.TryMakeUciMove(move.Uci, out _));
            count += CountPositions(next, depth - 1);
        }

        return count;
    }
}
