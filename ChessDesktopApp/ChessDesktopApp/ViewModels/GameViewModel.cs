using System.Collections.ObjectModel;
using Chess.Core;
using ChessDesktopApp.Engine;
using ChessDesktopApp.Models;
using ChessDesktopApp.Services;
using ChessDesktopApp.Styling;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ChessDesktopApp.ViewModels;

public sealed record ColorChoiceOption(string Name, PlayerColorChoice Value);
public sealed record PromotionOption(string Name, PieceType Value, string Glyph);
public sealed record MoveLogEntryViewModel(string Number, string WhiteMove, string BlackMove, int Ply = 0);

public sealed class GameViewModel : ObservableObject
{
    private static readonly SolidColorBrush NeutralStateBrush = ChessThemeResources.Brush(
        "ChessStatusNeutralBrush", Color.FromArgb(0xFF, 0x8A, 0x8A, 0x8A));
    private static readonly SolidColorBrush RunningStateBrush = ChessThemeResources.Brush(
        "ChessStatusRunningBrush", Color.FromArgb(0xFF, 0xF0, 0x98, 0x2F));
    private static readonly SolidColorBrush CompletedStateBrush = ChessThemeResources.Brush(
        "ChessStatusCompletedBrush", Color.FromArgb(0xFF, 0x4C, 0xB7, 0x62));
    private static readonly SolidColorBrush InterruptedStateBrush = ChessThemeResources.Brush(
        "ChessDangerBrush", Color.FromArgb(0xFF, 0xC4, 0x2B, 0x1C));

    private readonly GameSessionService _session;
    private readonly EngineManager _engineManager;
    private readonly DispatcherQueue _dispatcherQueue;
    private Square? _selectedSquare;
    private IReadOnlyList<Move> _selectedMoves = [];
    private PieceColor? _renderedOrientation;
    private PieceColor? _previewHumanColor;
    private ColorChoiceOption _selectedColorChoice;
    private EngineDifficulty _selectedDifficulty;
    private EngineDefinition? _selectedEngine;
    private double _selectedDifficultyIndex = 5;
    private string _statusText = "Wybierz ustawienia i rozpocznij nową partię";
    private string _turnText = "Gotowy do gry";
    private string _evaluationText = "—";
    private string _engineText = "Stockfish oczekuje";
    private string _topPlayerName = "Stockfish · czarne";
    private string _bottomPlayerName = "Ty · białe";
    private string _topCapturedPieces = "—";
    private string _bottomCapturedPieces = "—";
    private string _topAdvantageText = string.Empty;
    private string _bottomAdvantageText = string.Empty;
    private string _gameStateText = "Nie rozpoczęto";
    private string _startButtonText = "Rozpocznij partię";
    private double _topEvaluationShare = 0.5;
    private SolidColorBrush _gameStateBrush = NeutralStateBrush;
    private string? _errorText;
    private bool _isBusy;
    private ChessGame? _configurationPreviewGame;
    private ChessGame? _displayedHistoryGame;
    private ChessGame? _observedGame;
    private int _viewedPly;
    private int _lastKnownPly;

    public GameViewModel(GameSessionService session, EngineManager engineManager)
    {
        _session = session;
        _engineManager = engineManager;
        _dispatcherQueue = DispatcherQueue.GetForCurrentThread();
        ColorChoices =
        [
            new ColorChoiceOption("Białe", PlayerColorChoice.White),
            new ColorChoiceOption("Czarne", PlayerColorChoice.Black),
            new ColorChoiceOption("Losowo", PlayerColorChoice.Random)
        ];
        PromotionOptions =
        [
            new PromotionOption("Hetman", PieceType.Queen, "♛"),
            new PromotionOption("Wieża", PieceType.Rook, "♜"),
            new PromotionOption("Goniec", PieceType.Bishop, "♝"),
            new PromotionOption("Skoczek", PieceType.Knight, "♞")
        ];
        Difficulties = EngineDifficulty.Presets;
        _selectedColorChoice = ColorChoices[0];
        _selectedDifficulty = Difficulties[5];
        StartNewGameCommand = new AsyncRelayCommand(StartNewGameAsync);
        ResignCommand = new AsyncRelayCommand(ResignAsync, () => _session.CanResign);
        _session.StateChanged += SessionOnStateChanged;
        RefreshFromSession();
    }

    public ObservableCollection<BoardSquareViewModel> Squares { get; } = [];
    public ObservableCollection<MoveLogEntryViewModel> MoveLog { get; } = [];
    public ObservableCollection<EngineDefinition> AvailableEngines { get; } = [];
    public IReadOnlyList<ColorChoiceOption> ColorChoices { get; }
    public IReadOnlyList<EngineDifficulty> Difficulties { get; }
    public IReadOnlyList<PromotionOption> PromotionOptions { get; }
    public AsyncRelayCommand StartNewGameCommand { get; }
    public AsyncRelayCommand ResignCommand { get; }
    public Func<Task<PieceType?>>? PromotionSelector { get; set; }

    public EngineDefinition? SelectedEngine
    {
        get => _selectedEngine;
        set
        {
            if (!SetProperty(ref _selectedEngine, value)) return;
            OnPropertyChanged(nameof(DifficultyEloText));
            if (!_session.CanResign)
            {
                ApplyConfigurationPreview();
            }
        }
    }

    public ColorChoiceOption SelectedColorChoice
    {
        get => _selectedColorChoice;
        set
        {
            if (!SetProperty(ref _selectedColorChoice, value) || _session.CanResign) return;
            _previewHumanColor = value.Value switch
            {
                PlayerColorChoice.White => PieceColor.White,
                PlayerColorChoice.Black => PieceColor.Black,
                _ => PieceColor.White
            };
            _renderedOrientation = null;
            ApplyConfigurationPreview();
        }
    }

    public EngineDifficulty SelectedDifficulty
    {
        get => _selectedDifficulty;
        set
        {
            if (!SetProperty(ref _selectedDifficulty, value)) return;
            OnPropertyChanged(nameof(DifficultyEloText));
        }
    }

    public double SelectedDifficultyIndex
    {
        get => _selectedDifficultyIndex;
        set
        {
            if (!CanChangeDifficulty)
            {
                return;
            }

            var index = Math.Clamp((int)Math.Round(value), 0, Difficulties.Count - 1);
            if (!SetProperty(ref _selectedDifficultyIndex, index))
            {
                return;
            }

            SelectedDifficulty = Difficulties[index];
        }
    }

    public double DifficultyMaximum => Difficulties.Count - 1;
    public string DifficultyEloText
    {
        get
        {
            var estimate = SelectedDifficulty.SkillLevel == 20 && SelectedDifficulty.Elo is null
                ? $"{SelectedDifficulty.DisplayElo}+ Elo · pełna siła"
                : $"około {SelectedDifficulty.DisplayElo} Elo";
            return SelectedEngine is null ? estimate : $"{SelectedEngine.Name}: {estimate}";
        }
    }

    public string StatusText { get => _statusText; private set => SetProperty(ref _statusText, value); }
    public string TurnText { get => _turnText; private set => SetProperty(ref _turnText, value); }
    public string EvaluationText { get => _evaluationText; private set => SetProperty(ref _evaluationText, value); }
    public string EngineText { get => _engineText; private set => SetProperty(ref _engineText, value); }
    public string TopPlayerName { get => _topPlayerName; private set => SetProperty(ref _topPlayerName, value); }
    public string BottomPlayerName { get => _bottomPlayerName; private set => SetProperty(ref _bottomPlayerName, value); }
    public string TopCapturedPieces { get => _topCapturedPieces; private set => SetProperty(ref _topCapturedPieces, value); }
    public string BottomCapturedPieces { get => _bottomCapturedPieces; private set => SetProperty(ref _bottomCapturedPieces, value); }
    public string TopAdvantageText { get => _topAdvantageText; private set => SetProperty(ref _topAdvantageText, value); }
    public string BottomAdvantageText { get => _bottomAdvantageText; private set => SetProperty(ref _bottomAdvantageText, value); }
    public string GameStateText { get => _gameStateText; private set => SetProperty(ref _gameStateText, value); }
    public string StartButtonText { get => _startButtonText; private set => SetProperty(ref _startButtonText, value); }
    public double TopEvaluationShare { get => _topEvaluationShare; private set => SetProperty(ref _topEvaluationShare, value); }
    public SolidColorBrush GameStateBrush { get => _gameStateBrush; private set => SetProperty(ref _gameStateBrush, value); }
    public string? ErrorText { get => _errorText; private set => SetProperty(ref _errorText, value); }
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool IsGameThinking => _session.HasStarted && !_session.Status.IsGameOver;
    public Visibility GameThinkingVisibility => IsGameThinking ? Visibility.Visible : Visibility.Collapsed;
    public string GameActivityText => !_session.HasStarted
        ? "Oczekiwanie na rozpoczęcie partii"
        : _session.Status.IsGameOver
            ? GameStateText
            : _session.IsEngineThinking
                ? $"{_session.EngineName} myśli…"
                : _session.IsHumanTurn
                    ? "Ty myślisz"
                    : $"{_session.EngineName} myśli…";
    public double ErrorOpacity => string.IsNullOrWhiteSpace(ErrorText) ? 0 : 1;
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public bool IsViewingLivePosition => _configurationPreviewGame is not null || _viewedPly >= _session.Game.MoveHistory.Count;
    public bool CanBrowseHistory => _configurationPreviewGame is null && _session.Game.MoveHistory.Count > 0 && !IsBusy &&
        (_session.Status.IsGameOver || _session.IsHumanTurn);
    public bool CanBrowsePrevious => CanBrowseHistory && _viewedPly > 0;
    public bool CanBrowseNext => CanBrowseHistory && !IsViewingLivePosition;
    public string HistoryPositionText => IsViewingLivePosition
        ? _configurationPreviewGame is not null ? "Pozycja startowa" : "Pozycja aktualna"
        : $"Po ruchu {_viewedPly} z {_session.Game.MoveHistory.Count}";
    public bool CanInteract => _session.IsHumanTurn && !IsBusy && IsViewingLivePosition;
    public bool CanResign => _session.CanResign;
    public bool CanChangeDifficulty => !_session.CanResign;
    public bool CanChangeEngine => !_session.CanResign;

    public async Task InitializeAsync()
    {
        var engines = await _engineManager.LoadAsync();
        var selectedId = SelectedEngine?.Id;
        AvailableEngines.Clear();
        foreach (var engine in engines.Where(item => item.IsAvailable)) AvailableEngines.Add(engine);
        SelectedEngine = AvailableEngines.FirstOrDefault(item => item.Id == selectedId)
            ?? AvailableEngines.FirstOrDefault(item => item.IsBundled)
            ?? AvailableEngines.FirstOrDefault();
    }

    public async Task SelectSquareAsync(BoardSquareViewModel square)
    {
        if (!CanInteract)
        {
            return;
        }

        if (_selectedSquare is { } selected &&
            _selectedMoves.Any(move => move.To == square.Square))
        {
            await MakeMoveAsync(selected, square.Square);
            return;
        }

        // Wygodna roszada: po zaznaczeniu króla można kliknąć właściwą wieżę
        // (lub odwrotnie). Silnik reguł nadal sprawdza wszystkie warunki roszady.
        if (_selectedSquare is { } firstSquare &&
            TryGetCastlingMove(firstSquare, square.Square, out var castlingMove))
        {
            _selectedSquare = castlingMove.From;
            _selectedMoves = _session.GetLegalMovesFrom(castlingMove.From);
            await MakeMoveAsync(castlingMove.From, castlingMove.To);
            return;
        }

        var piece = _session.Game[square.Square];
        if (piece is { } selectedPiece && selectedPiece.Color == _session.HumanColor)
        {
            _selectedSquare = square.Square;
            _selectedMoves = _session.GetLegalMovesFrom(square.Square);
        }
        else
        {
            ClearSelection();
        }

        RefreshBoard();
    }

    private bool TryGetCastlingMove(Square first, Square second, out Move castlingMove)
    {
        castlingMove = default;
        var firstPiece = _session.Game[first];
        var secondPiece = _session.Game[second];
        if (firstPiece is null || secondPiece is null ||
            firstPiece.Value.Color != _session.HumanColor ||
            secondPiece.Value.Color != _session.HumanColor ||
            first.Rank != second.Rank)
        {
            return false;
        }

        Square kingSquare;
        Square rookSquare;
        if (firstPiece.Value.Type == PieceType.King && secondPiece.Value.Type == PieceType.Rook)
        {
            kingSquare = first;
            rookSquare = second;
        }
        else if (firstPiece.Value.Type == PieceType.Rook && secondPiece.Value.Type == PieceType.King)
        {
            kingSquare = second;
            rookSquare = first;
        }
        else
        {
            return false;
        }

        var requiredFlag = rookSquare.File > kingSquare.File
            ? MoveFlags.CastleKingSide
            : MoveFlags.CastleQueenSide;
        var legalCastle = _session.GetLegalMovesFrom(kingSquare)
            .FirstOrDefault(move => move.Flags.HasFlag(requiredFlag));
        if (!legalCastle.Flags.HasFlag(requiredFlag))
        {
            return false;
        }

        castlingMove = legalCastle;
        return true;
    }

    public async Task DropAsync(string fromName, BoardSquareViewModel target)
    {
        if (!Square.TryParse(fromName, out var from) || !CanInteract)
        {
            return;
        }

        var piece = _session.Game[from];
        if (piece is null || piece.Value.Color != _session.HumanColor)
        {
            return;
        }

        _selectedSquare = from;
        _selectedMoves = _session.GetLegalMovesFrom(from);
        if (_selectedMoves.Any(move => move.To == target.Square))
        {
            await MakeMoveAsync(from, target.Square);
        }
        else
        {
            RefreshBoard();
        }
    }

    private async Task StartNewGameAsync()
    {
        if (SelectedEngine is null)
        {
            ErrorText = "Wybierz silnik do gry.";
            OnPropertyChanged(nameof(ErrorOpacity));
            OnPropertyChanged(nameof(HasError));
            return;
        }

        IsBusy = true;
        _configurationPreviewGame = null;
        ErrorText = null;
        OnPropertyChanged(nameof(ErrorOpacity));
        OnPropertyChanged(nameof(HasError));
        ClearSelection();
        try
        {
            await _session.StartNewGameAsync(SelectedColorChoice.Value, SelectedDifficulty, SelectedEngine);
        }
        catch (OperationCanceledException)
        {
        }
        catch (ChessEngineException exception)
        {
            ErrorText = exception.Message;
            OnPropertyChanged(nameof(ErrorOpacity));
            OnPropertyChanged(nameof(HasError));
        }
        finally
        {
            IsBusy = false;
            RefreshFromSession();
        }
    }

    private async Task MakeMoveAsync(Square from, Square to)
    {
        var candidates = _selectedMoves.Where(move => move.To == to).ToArray();
        PieceType? promotion = null;
        if (candidates.Any(move => move.Promotion is not null))
        {
            promotion = PromotionSelector is null ? PieceType.Queen : await PromotionSelector();
            if (promotion is null)
            {
                return;
            }
        }

        ClearSelection();
        RefreshBoard();
        IsBusy = true;
        try
        {
            await _session.TryMakeHumanMoveAsync(from, to, promotion);
        }
        finally
        {
            IsBusy = false;
            RefreshFromSession();
        }
    }

    private async Task ResignAsync()
    {
        await _session.ResignAsync();
        RefreshFromSession();
    }

    public void BrowsePreviousPosition()
    {
        if (CanBrowsePrevious) ShowHistoryPosition(_viewedPly - 1);
    }

    public void BrowseNextPosition()
    {
        if (CanBrowseNext) ShowHistoryPosition(_viewedPly + 1);
    }

    public void ShowHistoryPosition(int ply)
    {
        if (!CanBrowseHistory) return;
        _configurationPreviewGame = null;
        _viewedPly = Math.Clamp(ply, 0, _session.Game.MoveHistory.Count);
        _displayedHistoryGame = IsViewingLivePosition ? null : ReplayToPly(_session.Game, _viewedPly);
        ClearSelection();
        NotifyHistoryState();
        RefreshBoard();
        RefreshPlayerSummary();
    }

    private void SessionOnStateChanged(object? sender, EventArgs e)
    {
        if (_dispatcherQueue.HasThreadAccess)
        {
            RefreshFromSession();
        }
        else
        {
            _dispatcherQueue.TryEnqueue(RefreshFromSession);
        }
    }

    private void RefreshFromSession()
    {
        if (_session.CanResign) _previewHumanColor = null;
        UpdateHistoryPosition();
        var status = _session.Status;
        StatusText = status.Kind switch
        {
            GameStatusKind.InProgress => "Partia w toku",
            GameStatusKind.Check => $"Szach — {ColorName(_session.Game.SideToMove)} musi odpowiedzieć",
            GameStatusKind.Checkmate => $"Mat — wygrywają {ColorName(status.Winner!.Value)}",
            GameStatusKind.Stalemate => "Remis przez pat",
            GameStatusKind.DrawByInsufficientMaterial => "Remis — niewystarczający materiał",
            GameStatusKind.DrawByFiftyMoveRule => "Remis — zasada 50 posunięć",
            GameStatusKind.DrawByThreefoldRepetition => "Remis — trzykrotne powtórzenie pozycji",
            GameStatusKind.Resignation => $"Poddanie — wygrywają {ColorName(status.Winner!.Value)}",
            _ => "Partia w toku"
        };
        if (!_session.HasStarted && string.IsNullOrWhiteSpace(_session.EngineError))
        {
            StatusText = "Wybierz ustawienia i rozpocznij nową partię";
        }

        TurnText = !_session.HasStarted
            ? "Gotowy do gry"
            : status.IsGameOver
                ? "Partia zakończona"
                : _session.IsEngineThinking
                    ? $"{_session.EngineName} analizuje pozycję…"
                    : _session.IsHumanTurn
                        ? "Twój ruch"
                        : $"Ruch: {_session.EngineName}";
        if (!_session.HasStarted)
        {
            GameStateText = "Nie rozpoczęto";
            GameStateBrush = NeutralStateBrush;
            StartButtonText = "Rozpocznij partię";
        }
        else if (status.IsGameOver)
        {
            var endedManually = status.Kind == GameStatusKind.Resignation;
            GameStateText = status.Winner is { } winner
                ? $"Wygrywają {ColorName(winner)}"
                : "Remis";
            GameStateBrush = endedManually ? InterruptedStateBrush : CompletedStateBrush;
            StartButtonText = "Nowa partia";
        }
        else
        {
            GameStateText = "Partia trwa";
            GameStateBrush = RunningStateBrush;
            StartButtonText = "Nowa partia";
        }
        EngineText = _session.IsEngineThinking ? "Analiza w toku" : $"Poziom: {_session.Difficulty.Name}";
        EvaluationText = _session.Evaluation?.DisplayText ?? "—";
        RefreshPlayerSummary();
        RefreshMoveLog();
        ErrorText = _session.EngineError ?? ErrorText;
        OnPropertyChanged(nameof(ErrorOpacity));
        OnPropertyChanged(nameof(HasError));
        OnPropertyChanged(nameof(CanInteract));
        OnPropertyChanged(nameof(CanResign));
        OnPropertyChanged(nameof(CanChangeDifficulty));
        OnPropertyChanged(nameof(CanChangeEngine));
        OnPropertyChanged(nameof(IsGameThinking));
        OnPropertyChanged(nameof(GameThinkingVisibility));
        OnPropertyChanged(nameof(GameActivityText));
        NotifyHistoryState();
        StartNewGameCommand.RaiseCanExecuteChanged();
        ResignCommand.RaiseCanExecuteChanged();
        RefreshBoard();
    }

    private double CalculateTopEvaluationShare(PieceColor topColor)
    {
        if (_session.Evaluation?.WhiteMateIn is { } mate)
        {
            var whiteWinning = mate > 0;
            return (topColor == PieceColor.White) == whiteWinning ? 0.98 : 0.02;
        }

        var whiteCentipawns = _session.Evaluation?.WhiteCentipawns ?? 0;
        var topCentipawns = topColor == PieceColor.White ? whiteCentipawns : -whiteCentipawns;
        return 0.5 + Math.Clamp(topCentipawns / 1600d, -0.48, 0.48);
    }

    private ChessGame DisplayedGame => _configurationPreviewGame ?? _displayedHistoryGame ?? _session.Game;

    private void UpdateHistoryPosition()
    {
        var currentGame = _session.Game;
        var currentPly = currentGame.MoveHistory.Count;
        if (!ReferenceEquals(_observedGame, currentGame))
        {
            _configurationPreviewGame = null;
            _observedGame = currentGame;
            _viewedPly = currentPly;
            _lastKnownPly = currentPly;
            _displayedHistoryGame = null;
            return;
        }

        if (_viewedPly >= _lastKnownPly || !_session.IsHumanTurn && !_session.Status.IsGameOver)
        {
            _viewedPly = currentPly;
            _displayedHistoryGame = null;
        }
        else if (_viewedPly < currentPly)
        {
            _displayedHistoryGame = ReplayToPly(currentGame, _viewedPly);
        }

        _lastKnownPly = currentPly;
    }

    private static ChessGame ReplayToPly(ChessGame source, int ply)
    {
        var replay = ChessGame.CreateInitial();
        foreach (var record in source.MoveHistory.Take(Math.Clamp(ply, 0, source.MoveHistory.Count)))
        {
            if (!replay.TryMakeUciMove(record.Move.Uci, out _))
                throw new InvalidOperationException($"Nie można odtworzyć ruchu {record.Move.Uci}.");
        }
        return replay;
    }

    private void NotifyHistoryState()
    {
        OnPropertyChanged(nameof(IsViewingLivePosition));
        OnPropertyChanged(nameof(CanBrowseHistory));
        OnPropertyChanged(nameof(CanBrowsePrevious));
        OnPropertyChanged(nameof(CanBrowseNext));
        OnPropertyChanged(nameof(HistoryPositionText));
        OnPropertyChanged(nameof(CanInteract));
    }

    private void RefreshPlayerSummary()
    {
        var humanColor = PreviewHumanColor;
        var engineColor = humanColor == PieceColor.White ? PieceColor.Black : PieceColor.White;
        TopEvaluationShare = _configurationPreviewGame is null ? CalculateTopEvaluationShare(engineColor) : 0.5;
        TopPlayerName = $"{(_session.CanResign ? _session.EngineName : SelectedEngine?.Name ?? "Silnik UCI")} · {ColorName(engineColor)}";
        BottomPlayerName = $"User · {ColorName(humanColor)}";
        TopCapturedPieces = FormatCapturedPieces(engineColor);
        BottomCapturedPieces = FormatCapturedPieces(humanColor);
        RefreshPlayerAdvantages(engineColor, humanColor);
    }

    private void ApplyConfigurationPreview()
    {
        if (_session.CanResign) return;

        if (_session.HasStarted && _session.Status.IsGameOver)
        {
            _configurationPreviewGame = ChessGame.CreateInitial();
            _displayedHistoryGame = null;
            _viewedPly = 0;
            _lastKnownPly = 0;
            MoveLog.Clear();
            StatusText = "Ustawienia zmienione — rozpocznij nową partię";
            TurnText = "Gotowy do gry";
            GameStateText = "Gotowy";
            GameStateBrush = NeutralStateBrush;
            StartButtonText = "Rozpocznij partię";
            EvaluationText = "—";
            EngineText = SelectedEngine is null ? "Wybierz silnik" : $"{SelectedEngine.Name} oczekuje";
        }

        ClearSelection();
        RefreshPlayerSummary();
        NotifyHistoryState();
        RefreshBoard();
    }

    private void RefreshBoard()
    {
        var orientation = PreviewHumanColor;
        if (Squares.Count != 64 || _renderedOrientation != orientation)
        {
            Squares.Clear();
            for (var displayRow = 0; displayRow < 8; displayRow++)
            {
                for (var displayColumn = 0; displayColumn < 8; displayColumn++)
                {
                    var file = orientation == PieceColor.White ? displayColumn : 7 - displayColumn;
                    var rank = orientation == PieceColor.White ? 7 - displayRow : displayRow;
                    Squares.Add(new BoardSquareViewModel(new Square(file, rank), displayRow, displayColumn));
                }
            }

            _renderedOrientation = orientation;
        }

        var displayGame = DisplayedGame;
        var status = displayGame.GetStatus();
        var checkedKingColor = status.Kind is GameStatusKind.Check or GameStatusKind.Checkmate
            ? displayGame.SideToMove
            : (PieceColor?)null;
        foreach (var squareViewModel in Squares)
        {
            var piece = displayGame[squareViewModel.Square];
            squareViewModel.Update(
                piece,
                _selectedSquare == squareViewModel.Square,
                _selectedMoves.Any(move => move.To == squareViewModel.Square) ||
                    (_selectedSquare is { } selected &&
                     TryGetCastlingMove(selected, squareViewModel.Square, out _)),
                displayGame.LastMove is { } lastMove &&
                    (lastMove.From == squareViewModel.Square || lastMove.To == squareViewModel.Square),
                piece is { Type: PieceType.King } && piece.Value.Color == checkedKingColor,
                squareViewModel.DisplayRow == 7,
                squareViewModel.DisplayColumn == 0);
        }
    }

    private void ClearSelection()
    {
        _selectedSquare = null;
        _selectedMoves = [];
    }

    private static string ColorName(PieceColor color) => color == PieceColor.White ? "białe" : "czarne";

    private string FormatCapturedPieces(PieceColor capturingColor)
    {
        var captured = DisplayedGame.CapturedPieces
            .Where(piece => piece.Color != capturingColor)
            .OrderByDescending(piece => PieceValue(piece.Type))
            .Select(PieceGlyph)
            .ToArray();
        return captured.Length == 0 ? "—" : string.Join(' ', captured);
    }

    private static int PieceValue(PieceType type) => type switch
    {
        PieceType.Queen => 9,
        PieceType.Rook => 5,
        PieceType.Bishop or PieceType.Knight => 3,
        PieceType.Pawn => 1,
        _ => 0
    };

    private void RefreshPlayerAdvantages(PieceColor engineColor, PieceColor humanColor)
    {
        var whiteCaptured = DisplayedGame.CapturedPieces
            .Where(piece => piece.Color == PieceColor.Black)
            .Sum(piece => PieceValue(piece.Type));
        var blackCaptured = DisplayedGame.CapturedPieces
            .Where(piece => piece.Color == PieceColor.White)
            .Sum(piece => PieceValue(piece.Type));
        var whiteBalance = whiteCaptured - blackCaptured;
        var topBalance = engineColor == PieceColor.White ? whiteBalance : -whiteBalance;
        var bottomBalance = humanColor == PieceColor.White ? whiteBalance : -whiteBalance;
        TopAdvantageText = FormatAdvantage(topBalance);
        BottomAdvantageText = FormatAdvantage(bottomBalance);
    }

    private static string FormatAdvantage(int value) => value > 0 ? $"+{value}" : string.Empty;

    private PieceColor PreviewHumanColor => _previewHumanColor ?? _session.HumanColor;

    private void RefreshMoveLog()
    {
        MoveLog.Clear();
        foreach (var group in _session.Game.MoveHistory
                     .Select((record, index) => (Record: record, Ply: index + 1))
                     .GroupBy(item => item.Record.MoveNumber))
        {
            MoveLog.Add(new MoveLogEntryViewModel(
                $"{group.Key}.",
                group.FirstOrDefault(item => item.Record.Color == PieceColor.White).Record?.Notation ?? string.Empty,
                group.FirstOrDefault(item => item.Record.Color == PieceColor.Black).Record?.Notation ?? string.Empty,
                group.Max(item => item.Ply)));
        }
    }

    private static string PieceGlyph(Piece piece) => (piece.Color, piece.Type) switch
    {
        (PieceColor.White, PieceType.Queen) => "♕",
        (PieceColor.White, PieceType.Rook) => "♖",
        (PieceColor.White, PieceType.Bishop) => "♗",
        (PieceColor.White, PieceType.Knight) => "♘",
        (PieceColor.White, PieceType.Pawn) => "♙",
        (PieceColor.Black, PieceType.Queen) => "♛",
        (PieceColor.Black, PieceType.Rook) => "♜",
        (PieceColor.Black, PieceType.Bishop) => "♝",
        (PieceColor.Black, PieceType.Knight) => "♞",
        (PieceColor.Black, PieceType.Pawn) => "♟",
        _ => string.Empty
    };
}
