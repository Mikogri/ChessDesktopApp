using Chess.Core;
using ChessDesktopApp.Styling;
using Microsoft.UI;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ChessDesktopApp.ViewModels;

public sealed class BoardSquareViewModel : ObservableObject
{
    private static readonly SolidColorBrush LightSquareBrush = ThemeBrush("ChessBoardLightBrush", 0x39, 0x43, 0x52);
    private static readonly SolidColorBrush DarkSquareBrush = ThemeBrush("ChessBoardDarkBrush", 0x24, 0x2B, 0x36);
    private static readonly SolidColorBrush LastMoveLightBrush = ThemeBrush("ChessBoardLastMoveLightBrush", 0x66, 0x61, 0x3A);
    private static readonly SolidColorBrush LastMoveDarkBrush = ThemeBrush("ChessBoardLastMoveDarkBrush", 0x52, 0x50, 0x2D);
    private static readonly SolidColorBrush SelectedBrush = ThemeBrush("ChessBoardSelectedBrush", 0x16, 0x70, 0x91);
    private static readonly SolidColorBrush CheckBrush = ThemeBrush("ChessBoardCheckBrush", 0x83, 0x32, 0x43);
    private static readonly SolidColorBrush WhitePieceBrush = ThemeBrush("ChessWhitePieceBrush", 0xFF, 0xFF, 0xFF);
    private static readonly SolidColorBrush BlackPieceBrush = ThemeBrush("ChessBlackPieceBrush", 0xD4, 0x8A, 0xFF);

    private string _pieceGlyph = string.Empty;
    private SolidColorBrush _background = DarkSquareBrush;
    private SolidColorBrush _pieceBrush = WhitePieceBrush;
    private double _moveHintOpacity;
    private double _captureHintOpacity;

    public BoardSquareViewModel(Square square, int displayRow, int displayColumn)
    {
        Square = square;
        DisplayRow = displayRow;
        DisplayColumn = displayColumn;
    }

    public Square Square { get; }
    public string SquareName => Square.Name;
    public int DisplayRow { get; }
    public int DisplayColumn { get; }
    public string FileLabel { get; private set; } = string.Empty;
    public string RankLabel { get; private set; } = string.Empty;
    public string PieceGlyph { get => _pieceGlyph; private set => SetProperty(ref _pieceGlyph, value); }
    public SolidColorBrush Background { get => _background; private set => SetProperty(ref _background, value); }
    public SolidColorBrush PieceBrush { get => _pieceBrush; private set => SetProperty(ref _pieceBrush, value); }
    public double MoveHintOpacity { get => _moveHintOpacity; private set => SetProperty(ref _moveHintOpacity, value); }
    public double CaptureHintOpacity { get => _captureHintOpacity; private set => SetProperty(ref _captureHintOpacity, value); }

    public void Update(
        Piece? piece,
        bool isSelected,
        bool isLegalTarget,
        bool isLastMove,
        bool isKingInCheck,
        bool showFileLabel,
        bool showRankLabel)
    {
        PieceGlyph = piece is null ? string.Empty : GetGlyph(piece.Value);
        PieceBrush = piece?.Color == PieceColor.Black ? BlackPieceBrush : WhitePieceBrush;
        var isLight = (Square.File + Square.Rank) % 2 != 0;
        Background = isKingInCheck
            ? CheckBrush
            : isSelected
                ? SelectedBrush
                : isLastMove
                    ? isLight ? LastMoveLightBrush : LastMoveDarkBrush
                    : isLight ? LightSquareBrush : DarkSquareBrush;
        MoveHintOpacity = isLegalTarget ? 1 : 0;
        CaptureHintOpacity = isLegalTarget && piece is not null ? 1 : 0;
        FileLabel = showFileLabel ? ((char)('a' + Square.File)).ToString() : string.Empty;
        RankLabel = showRankLabel ? (Square.Rank + 1).ToString() : string.Empty;
        OnPropertyChanged(nameof(FileLabel));
        OnPropertyChanged(nameof(RankLabel));
    }

    private static string GetGlyph(Piece piece) => (piece.Color, piece.Type) switch
    {
        (PieceColor.White, PieceType.King) => "♔",
        (PieceColor.White, PieceType.Queen) => "♕",
        (PieceColor.White, PieceType.Rook) => "♖",
        (PieceColor.White, PieceType.Bishop) => "♗",
        (PieceColor.White, PieceType.Knight) => "♘",
        (PieceColor.White, PieceType.Pawn) => "♙",
        (PieceColor.Black, PieceType.King) => "♚",
        (PieceColor.Black, PieceType.Queen) => "♛",
        (PieceColor.Black, PieceType.Rook) => "♜",
        (PieceColor.Black, PieceType.Bishop) => "♝",
        (PieceColor.Black, PieceType.Knight) => "♞",
        (PieceColor.Black, PieceType.Pawn) => "♟",
        _ => string.Empty
    };

    private static SolidColorBrush ThemeBrush(string resourceKey, byte red, byte green, byte blue) =>
        ChessThemeResources.Brush(resourceKey, Color.FromArgb(0xFF, red, green, blue));
}
