using System.Collections.ObjectModel;
using Chess.Core;
using ChessDesktopApp.Engine;
using ChessDesktopApp.Models;
using ChessDesktopApp.Services;
using ChessDesktopApp.Storage;
using ChessDesktopApp.Styling;
using Microsoft.UI.Dispatching;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Media;
using Windows.UI;

namespace ChessDesktopApp.ViewModels;

public sealed class EngineSelectionItem : ObservableObject
{
    private bool _isSelected;
    public required EngineDefinition Engine { get; init; }
    public bool IsSelected { get => _isSelected; set => SetProperty(ref _isSelected, value); }
    public string Name => Engine.Name;
    public string Status => Engine.Status;
}

public sealed class EngineHubViewModel : ObservableObject, IAsyncDisposable
{
    private static readonly SolidColorBrush NeutralStateBrush = ChessThemeResources.Brush(
        "ChessStatusNeutralBrush", Color.FromArgb(0xFF, 0x8A, 0x8A, 0x8A));
    private static readonly SolidColorBrush RunningStateBrush = ChessThemeResources.Brush(
        "ChessStatusRunningBrush", Color.FromArgb(0xFF, 0xF0, 0x98, 0x2F));
    private static readonly SolidColorBrush CompletedStateBrush = ChessThemeResources.Brush(
        "ChessStatusCompletedBrush", Color.FromArgb(0xFF, 0x4C, 0xB7, 0x62));
    private static readonly SolidColorBrush InterruptedStateBrush = ChessThemeResources.Brush(
        "ChessDangerBrush", Color.FromArgb(0xFF, 0xC4, 0x2B, 0x1C));

    private readonly EngineManager _engineManager;
    private readonly EngineMatchManager _matchManager;
    private readonly TournamentManager _tournamentManager;
    private readonly EngineGameRepository _games;
    private readonly TournamentRepository _tournaments;
    private readonly DispatcherQueue _dispatcher;
    private EngineDefinition? _selectedEngineA;
    private EngineDefinition? _selectedEngineB;
    private EngineDefinition? _selectedManagedEngine;
    private EngineColorOption _selectedColorOption = null!;
    private double _initialTimeMinutes = 3;
    private double _incrementSeconds = 2;
    private string _message = "Gotowy";
    private string? _errorText;
    private bool _isBusy;
    private SavedEngineGame? _selectedSavedGame;
    private EngineTournament? _selectedTournament;
    private string _matchWhiteName = "Engine White";
    private string _matchBlackName = "Engine Black";
    private string _whiteClock = "03:00.0";
    private string _blackClock = "03:00.0";
    private string _matchStatus = "Wybierz dwa silniki";
    private string _matchEvaluation = "—";
    private string _matchDepth = "Depth —";
    private double _matchTopEvaluationShare = 0.5;
    private readonly Dictionary<string, double> _sessionMatchScores = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<ChessGame> _scoredMatchGames = [];
    private string _tournamentName = string.Empty;
    private bool _autoStartNextRound = true;
    private bool _saveTournamentLocally = true;
    private TournamentMatch? _selectedTournamentMatch;
    private string _tournamentSearchText = string.Empty;
    private readonly List<EngineTournament> _allTournaments = [];
    private bool _updatingTournamentChoices;
    private ChessGame? _savedReplayGame;
    private SavedEngineGame? _openedReplay;
    private ChessGame _observerGame = ChessGame.CreateInitial();
    private ChessGame? _observerDisplayedGame;
    private int _observerViewedPly;
    private ChessGame? _observerMoveLogGame;
    private int _observerMoveLogPly = -1;
    private Guid? _observedTournamentMatchId;
    private TournamentMatchSession? _observedTournamentSession;
    private SavedEngineGame? _observerSavedGame;
    private string _observerTitle = "Podgląd partii";
    private string _observerWhiteName = "Białe";
    private string _observerBlackName = "Czarne";
    private string _observerStatus = "Powtórka";
    private string _observerResult = "*";
    private bool _observerIsLive;
    private string _observerWhiteClock = "—";
    private string _observerBlackClock = "—";
    private string _observerFormatText = "Format —";
    private string _observerDurationText = "Czas trwania —";
    private double _observerTopEvaluationShare = 0.5;
    private int _selectedLanguageIndex;
    private EngineTournament? _detailTournament;
    private string _tournamentDetailName = "Szczegóły turnieju";
    private string _tournamentDetailStatus = string.Empty;
    private ChessGame? _matchDisplayedHistoryGame;
    private ChessGame? _matchConfigurationPreviewGame;
    private bool _isMatchConfigurationPreview;
    private ChessGame? _observedMatchGame;
    private int _matchViewedPly;
    private int _lastKnownMatchPly;
    private int _tournamentStatisticsLoadVersion;

    public EngineHubViewModel(
        EngineManager engineManager,
        EngineMatchManager matchManager,
        TournamentManager tournamentManager,
        EngineGameRepository games,
        TournamentRepository tournaments)
    {
        _engineManager = engineManager;
        _matchManager = matchManager;
        _tournamentManager = tournamentManager;
        _games = games;
        _tournaments = tournaments;
        _dispatcher = DispatcherQueue.GetForCurrentThread();
        _selectedLanguageIndex = string.Equals(LoadLanguageSetting(), "en", StringComparison.OrdinalIgnoreCase) ? 1 : 0;
        ColorAssignments =
        [
            new("Losuj kolory", EngineColorAssignment.Random),
            new("Engine A gra białymi", EngineColorAssignment.EngineAWhite),
            new("Engine B gra białymi", EngineColorAssignment.EngineBWhite)
        ];
        SelectedColorOption = ColorAssignments[0];
        _matchManager.StateChanged += MatchOnStateChanged;
        _tournamentManager.StateChanged += TournamentOnStateChanged;
        _tournamentManager.MatchStateChanged += TournamentMatchOnStateChanged;
    }

    public ObservableCollection<EngineDefinition> Engines { get; } = [];
    public ObservableCollection<EngineSelectionItem> TournamentEngineChoices { get; } = [];
    public ObservableCollection<SavedEngineGame> SavedGames { get; } = [];
    public ObservableCollection<EngineTournament> TournamentHistory { get; } = [];
    public ObservableCollection<TournamentMatch> BracketMatches { get; } = [];
    public ObservableCollection<TournamentMatch> CompletedTournamentMatches { get; } = [];
    public ObservableCollection<TournamentMatch> UpcomingTournamentMatches { get; } = [];
    public ObservableCollection<TournamentEngineStatistic> TournamentStatistics { get; } = [];
    public ObservableCollection<BoardSquareViewModel> MatchSquares { get; } = [];
    public ObservableCollection<MoveLogEntryViewModel> MatchMoveLog { get; } = [];
    public ObservableCollection<BoardSquareViewModel> ObserverSquares { get; } = [];
    public ObservableCollection<MoveLogEntryViewModel> ObserverMoveLog { get; } = [];
    public IReadOnlyList<EngineColorOption> ColorAssignments { get; }
    public IReadOnlyList<string> AvailableLanguages { get; } = ["Polski", "English"];
    public EngineColorOption SelectedColorOption
    {
        get => _selectedColorOption;
        set
        {
            if (!SetProperty(ref _selectedColorOption, value)) return;
            ApplyMatchConfigurationPreview();
        }
    }

    public EngineDefinition? SelectedEngineA
    {
        get => _selectedEngineA;
        set
        {
            if (!SetProperty(ref _selectedEngineA, value)) return;
            ApplyMatchConfigurationPreview();
        }
    }
    public EngineDefinition? SelectedEngineB
    {
        get => _selectedEngineB;
        set
        {
            if (!SetProperty(ref _selectedEngineB, value)) return;
            ApplyMatchConfigurationPreview();
        }
    }
    public EngineDefinition? SelectedManagedEngine { get => _selectedManagedEngine; set => SetProperty(ref _selectedManagedEngine, value); }
    public SavedEngineGame? SelectedSavedGame
    {
        get => _selectedSavedGame;
        set { if (SetProperty(ref _selectedSavedGame, value)) { OnPropertyChanged(nameof(SelectedGamePgn)); OnPropertyChanged(nameof(SelectedGameSummary)); OnPropertyChanged(nameof(HasSelectedSavedGame)); } }
    }
    public EngineTournament? SelectedTournament
    {
        get => _selectedTournament;
        set
        {
            if (SetProperty(ref _selectedTournament, value) &&
                (!IsTournamentRunning || value?.Id == _tournamentManager.Current?.Id))
                _ = LoadTournamentDetailsAsync(value);
        }
    }
    public double InitialTimeMinutes { get => _initialTimeMinutes; set => SetProperty(ref _initialTimeMinutes, Math.Clamp(value, 0.25, 60)); }
    public double IncrementSeconds { get => _incrementSeconds; set => SetProperty(ref _incrementSeconds, Math.Clamp(value, 0, 60)); }
    public string TournamentName { get => _tournamentName; set => SetProperty(ref _tournamentName, value); }
    public bool AutoStartNextRound { get => _autoStartNextRound; set => SetProperty(ref _autoStartNextRound, value); }
    public bool SaveTournamentLocally { get => _saveTournamentLocally; set => SetProperty(ref _saveTournamentLocally, value); }
    public TournamentMatch? SelectedTournamentMatch { get => _selectedTournamentMatch; set => SetProperty(ref _selectedTournamentMatch, value); }
    public string TournamentSearchText
    {
        get => _tournamentSearchText;
        set { if (SetProperty(ref _tournamentSearchText, value)) ApplyTournamentFilter(); }
    }
    public string Message { get => _message; private set => SetProperty(ref _message, value); }
    public string? ErrorText { get => _errorText; private set { SetProperty(ref _errorText, value); OnPropertyChanged(nameof(HasError)); } }
    public bool HasError => !string.IsNullOrWhiteSpace(ErrorText);
    public bool IsBusy { get => _isBusy; private set => SetProperty(ref _isBusy, value); }
    public bool IsMatchRunning => _matchManager.IsRunning;
    public bool IsMatchPaused => _matchManager.IsPaused;
    public bool IsMatchConfigurationEnabled => !_matchManager.IsRunning;
    public bool IsMatchThinking => IsMatchRunning && !IsMatchPaused && _matchManager.StatusText.StartsWith("Myśli:", StringComparison.Ordinal);
    public bool IsWhiteThinking => IsMatchThinking && _matchManager.Game.SideToMove == PieceColor.White;
    public bool IsBlackThinking => IsMatchThinking && _matchManager.Game.SideToMove == PieceColor.Black;
    public string PrimaryMatchButtonText => !IsMatchRunning ? "Start pojedynku" : IsMatchPaused ? "Wznów" : "Pauzuj";
    public string MatchActivityText => _isMatchConfigurationPreview
        ? MatchStatus
        : IsMatchRunning
        ? IsMatchPaused
            ? "Pojedynek wstrzymany"
            : _matchManager.StatusText.StartsWith("Myśli:", StringComparison.Ordinal)
                ? _matchManager.StatusText
                : $"Ruch: {CurrentMatchEngineName}"
        : _matchManager.Result != "*"
            ? "Pojedynek zakończony"
            : MatchStatus;
    public string MatchOutcomeText => _isMatchConfigurationPreview ? string.Empty : _matchManager.Result switch
    {
        "1-0" => $"Wygrywa {MatchWhiteName} (1–0)",
        "0-1" => $"Wygrywa {MatchBlackName} (0–1)",
        "1/2-1/2" => "Remis (½–½)",
        _ when _matchManager.WasStoppedManually => "Zakończono ręcznie",
        _ => string.Empty
    };
    public string MatchStateText => MatchResultText;
    public string MatchResultText => _isMatchConfigurationPreview
        ? "Gotowy"
        : _openedReplay is not null
        ? $"Powtórka · {_openedReplay.Result}"
        : IsMatchRunning
        ? IsMatchPaused ? "Pauza" : "Trwa"
        : _matchManager.WasStoppedManually ? "Zakończono ręcznie"
        : _matchManager.Result != "*" ? FormatSessionMatchResult()
        : "Gotowy";
    public SolidColorBrush MatchStateBrush => _isMatchConfigurationPreview
        ? NeutralStateBrush
        : _openedReplay is not null
            ? CompletedStateBrush
            : IsMatchRunning
                ? RunningStateBrush
                : _matchManager.WasStoppedManually
                    ? InterruptedStateBrush
                    : _matchManager.Result != "*" ? CompletedStateBrush : NeutralStateBrush;
    public bool IsMatchStatePulsing => IsMatchRunning && !IsMatchPaused;
    public bool IsViewingMatchLivePosition => _isMatchConfigurationPreview || _matchViewedPly >= MatchHistoryGame.MoveHistory.Count;
    public bool CanBrowseMatchHistory => !_isMatchConfigurationPreview && MatchHistoryGame.MoveHistory.Count > 0 &&
        (_openedReplay is not null || !IsMatchRunning || IsMatchPaused);
    public bool CanBrowsePreviousMatchPosition => CanBrowseMatchHistory && _matchViewedPly > 0;
    public bool CanBrowseNextMatchPosition => CanBrowseMatchHistory && !IsViewingMatchLivePosition;
    public string MatchHistoryPositionText => IsViewingMatchLivePosition
        ? _isMatchConfigurationPreview ? "Pozycja startowa" : _openedReplay is null ? "Pozycja aktualna" : "Pozycja końcowa"
        : $"Po ruchu {_matchViewedPly} z {MatchHistoryGame.MoveHistory.Count}";
    public bool IsTournamentDraft => _tournamentManager.Current?.Status == TournamentStatus.Draft;
    public bool IsTournamentRunning => _tournamentManager.IsRunning;
    public bool CanStopTournament => _tournamentManager.Current?.Status is TournamentStatus.Running or TournamentStatus.Paused;
    public bool CanCreateNewTournament => _tournamentManager.Current?.Status is not (TournamentStatus.Running or TournamentStatus.Paused);
    public bool IsTournamentConfigurationEnabled => _tournamentManager.Current is null || IsTournamentDraft;
    public bool CanStartTournamentRound => TournamentEngineChoices.Count(item => item.IsSelected) >= 2 &&
        _tournamentManager.Current?.Status is TournamentStatus.Draft or TournamentStatus.Paused;
    public string TournamentStartButtonText => _tournamentManager.Current?.Status == TournamentStatus.Paused
        ? "Uruchom następną rundę" : "Rozpocznij turniej";
    public string TournamentStatusText => _tournamentManager.Current is { } tournament
        ? $"{tournament.StatusText}  ·  {tournament.ProgressText}" : "Skonfiguruj nowy turniej";
    public string TournamentRoundText => _tournamentManager.Current is { CurrentRound: > 0 } tournament
        ? $"Runda {tournament.CurrentRound} z {tournament.RoundCount}" : "Oczekuje na start";
    public string MatchWhiteName { get => _matchWhiteName; private set => SetProperty(ref _matchWhiteName, value); }
    public string MatchBlackName { get => _matchBlackName; private set => SetProperty(ref _matchBlackName, value); }
    public string WhiteClock { get => _whiteClock; private set => SetProperty(ref _whiteClock, value); }
    public string BlackClock { get => _blackClock; private set => SetProperty(ref _blackClock, value); }
    public string MatchStatus { get => _matchStatus; private set => SetProperty(ref _matchStatus, value); }
    public string MatchEvaluation { get => _matchEvaluation; private set => SetProperty(ref _matchEvaluation, value); }
    public string MatchDepth { get => _matchDepth; private set => SetProperty(ref _matchDepth, value); }
    public double MatchTopEvaluationShare { get => _matchTopEvaluationShare; private set => SetProperty(ref _matchTopEvaluationShare, value); }
    public string MatchWhiteCapturedPieces => FormatMatchCapturedPieces(PieceColor.White);
    public string MatchBlackCapturedPieces => FormatMatchCapturedPieces(PieceColor.Black);
    public string MatchWhiteAdvantageText => FormatMatchAdvantage(PieceColor.White);
    public string MatchBlackAdvantageText => FormatMatchAdvantage(PieceColor.Black);
    public string TimeControlText => $"{InitialTimeMinutes:0.##} min + {IncrementSeconds:0} s";
    public string EngineCountText => $"{TournamentEngineChoices.Count(item => item.IsSelected)} wybranych";
    public Visibility TournamentStatisticsEmptyVisibility => TournamentStatistics.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility BracketMatchesEmptyVisibility => BracketMatches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility CompletedMatchesEmptyVisibility => CompletedTournamentMatches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public Visibility UpcomingMatchesEmptyVisibility => UpcomingTournamentMatches.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    public string SelectedGamePgn => SelectedSavedGame?.Pgn ?? "Wybierz partię, aby zobaczyć PGN.";
    public string SelectedGameSummary => SelectedSavedGame is null ? string.Empty :
        $"{SelectedSavedGame.PairingText}";
    public string StoragePath => AppDataPaths.Data;
    public bool HasSelectedSavedGame => SelectedSavedGame is not null;
    public string EnginesPath => AppDataPaths.Engines;
    public int SelectedLanguageIndex
    {
        get => _selectedLanguageIndex;
        set
        {
            if (!SetProperty(ref _selectedLanguageIndex, Math.Clamp(value, 0, 1))) return;
            SaveLanguageSetting(value == 1 ? "en" : "pl");
            OnPropertyChanged(nameof(IsEnglish));
        }
    }
    public bool IsEnglish => SelectedLanguageIndex == 1;

    private static string? LoadLanguageSetting()
    {
        try
        {
            return Windows.Storage.ApplicationData.Current.LocalSettings.Values["AppLanguage"] as string;
        }
        catch (InvalidOperationException)
        {
            var path = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "FluentChess", "language.txt");
            return File.Exists(path) ? File.ReadAllText(path).Trim() : null;
        }
    }

    private static void SaveLanguageSetting(string language)
    {
        try
        {
            Windows.Storage.ApplicationData.Current.LocalSettings.Values["AppLanguage"] = language;
        }
        catch (InvalidOperationException)
        {
            var directory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentChess");
            Directory.CreateDirectory(directory);
            File.WriteAllText(Path.Combine(directory, "language.txt"), language);
        }
    }
    public string ApiVersion => "Windows App SDK 2.3.1 · .NET 8";
    public string TournamentDetailName { get => _tournamentDetailName; private set => SetProperty(ref _tournamentDetailName, value); }
    public string TournamentDetailStatus { get => _tournamentDetailStatus; private set => SetProperty(ref _tournamentDetailStatus, value); }
    public string ObserverTitle { get => _observerTitle; private set => SetProperty(ref _observerTitle, value); }
    public string ObserverWhiteName { get => _observerWhiteName; private set => SetProperty(ref _observerWhiteName, value); }
    public string ObserverBlackName { get => _observerBlackName; private set => SetProperty(ref _observerBlackName, value); }
    public string ObserverStatus { get => _observerStatus; private set => SetProperty(ref _observerStatus, value); }
    public string ObserverResult { get => _observerResult; private set => SetProperty(ref _observerResult, value); }
    public bool ObserverIsLive { get => _observerIsLive; private set => SetProperty(ref _observerIsLive, value); }
    public string ObserverWhiteClock { get => _observerWhiteClock; private set => SetProperty(ref _observerWhiteClock, value); }
    public string ObserverBlackClock { get => _observerBlackClock; private set => SetProperty(ref _observerBlackClock, value); }
    public string ObserverFormatText { get => _observerFormatText; private set => SetProperty(ref _observerFormatText, value); }
    public string ObserverDurationText { get => _observerDurationText; private set => SetProperty(ref _observerDurationText, value); }
    public double ObserverTopEvaluationShare { get => _observerTopEvaluationShare; private set => SetProperty(ref _observerTopEvaluationShare, value); }
    public SolidColorBrush ObserverStateBrush => ObserverIsLive
        ? RunningStateBrush
        : ObserverResult == "*" ? NeutralStateBrush : CompletedStateBrush;
    public string ObserverWinnerText => ObserverResult switch
    {
        "1-0" => $"Wygrywa {ObserverWhiteName}",
        "0-1" => $"Wygrywa {ObserverBlackName}",
        "1/2-1/2" => "Remis",
        _ => ObserverIsLive ? "Partia trwa" : "Brak wyniku"
    };
    public string ObserverWhiteCapturedPieces => FormatObserverCapturedPieces(PieceColor.White);
    public string ObserverBlackCapturedPieces => FormatObserverCapturedPieces(PieceColor.Black);
    public string ObserverWhiteAdvantageText => FormatObserverAdvantage(PieceColor.White);
    public string ObserverBlackAdvantageText => FormatObserverAdvantage(PieceColor.Black);
    public bool ObserverIsPaused => _observedTournamentSession?.Runner.IsPaused == true;
    public bool CanControlObserverMatch => _observedTournamentSession is { IsActive: true } session && session.Runner.IsRunning;
    public string ObserverPrimaryButtonText => ObserverIsPaused ? "Wznów" : "Pauzuj";
    public bool CanBrowseObserverHistory => _observerGame.MoveHistory.Count > 0 && (!ObserverIsLive || ObserverIsPaused);
    public bool CanBrowsePreviousObserverPosition => CanBrowseObserverHistory && _observerViewedPly > 0;
    public bool CanBrowseNextObserverPosition => CanBrowseObserverHistory && _observerViewedPly < _observerGame.MoveHistory.Count;
    public string ObserverPositionText => _observerViewedPly >= _observerGame.MoveHistory.Count
        ? "Pozycja końcowa" : $"Po ruchu {_observerViewedPly} z {_observerGame.MoveHistory.Count}";

    public async Task InitializeAsync()
    {
        IsBusy = true;
        try
        {
            await ReloadEnginesAsync();
            await ReloadHistoryAsync();
            if (!await RestoreActiveTournamentAsync()) PrepareNewTournament();
            RefreshMatch();
        }
        catch (Exception exception)
        {
            ErrorText = exception.Message;
        }
        finally { IsBusy = false; }
    }

    public async Task AddEngineAsync(string path)
    {
        IsBusy = true; ErrorText = null; Message = "Sprawdzanie UCI…";
        try
        {
            var added = await _engineManager.AddAsync(path);
            Message = $"Dodano: {added.Name}";
            await ReloadEnginesAsync();
            SelectedManagedEngine = Engines.FirstOrDefault(item => item.Id == added.Id);
        }
        catch (Exception exception) { ErrorText = exception.Message; Message = "Nie udało się dodać silnika"; }
        finally { IsBusy = false; }
    }

    public async Task ValidateSelectedEngineAsync()
    {
        if (SelectedManagedEngine is null) return;
        IsBusy = true; ErrorText = null;
        var result = await _engineManager.ValidateAsync(SelectedManagedEngine);
        SelectedManagedEngine.Name = result.Name;
        SelectedManagedEngine.Status = result.IsValid ? "Gotowy" : result.Message;
        SelectedManagedEngine.LastValidatedAt = DateTimeOffset.Now;
        await _engineManager.SaveAsync(Engines);
        Message = result.Message;
        if (!result.IsValid) ErrorText = result.Message;
        await ReloadEnginesAsync();
        IsBusy = false;
    }

    public async Task RemoveSelectedEngineAsync()
    {
        if (SelectedManagedEngine is null) return;
        await _engineManager.RemoveAsync(SelectedManagedEngine.Id);
        Message = "Usunięto silnik z konfiguracji (plik pozostał na dysku).";
        await ReloadEnginesAsync();
    }

    public async Task StartMatchAsync()
    {
        if (SelectedEngineA is null || SelectedEngineB is null)
        {
            ErrorText = "Wybierz Engine A oraz Engine B."; return;
        }
        if (SelectedEngineA.Id == SelectedEngineB.Id)
        {
            ErrorText = "Wybierz dwa różne silniki.";
            return;
        }
        ErrorText = null;
        try
        {
            _isMatchConfigurationPreview = false;
            _matchConfigurationPreviewGame = null;
            SetPendingMatchPlayerNames();
            RefreshMatchBindings();
            var config = new EngineMatchConfiguration(
                SelectedEngineA, SelectedEngineB, SelectedColorOption.Value,
                (int)Math.Round(InitialTimeMinutes * 60), (int)Math.Round(IncrementSeconds));
            var matchTask = _matchManager.StartAsync(config);
            RefreshMatch();
            await matchTask;
            RefreshMatch();
            await ReloadHistoryAsync();
        }
        catch (Exception exception) when (exception is ChessEngineException or IOException)
        {
            ErrorText = exception.Message;
        }
    }

    public void PauseMatch()
    {
        _matchManager.Pause();
        RefreshMatch();
    }

    public void ResumeMatch()
    {
        _matchManager.Resume();
        RefreshMatch();
    }

    public async Task StopMatchAsync()
    {
        await _matchManager.StopAsync();
        RefreshMatch();
    }

    public void BrowsePreviousMatchPosition()
    {
        if (CanBrowsePreviousMatchPosition) ShowMatchHistoryPosition(_matchViewedPly - 1);
    }

    public void BrowseNextMatchPosition()
    {
        if (CanBrowseNextMatchPosition) ShowMatchHistoryPosition(_matchViewedPly + 1);
    }

    public void ShowMatchHistoryPosition(int ply)
    {
        if (!CanBrowseMatchHistory) return;
        var source = MatchHistoryGame;
        _matchViewedPly = Math.Clamp(ply, 0, source.MoveHistory.Count);
        _matchDisplayedHistoryGame = IsViewingMatchLivePosition
            ? null
            : ReplayMatchToPly(source, _matchViewedPly);
        NotifyMatchHistoryState();
        OnPropertyChanged(nameof(MatchWhiteCapturedPieces));
        OnPropertyChanged(nameof(MatchBlackCapturedPieces));
        OnPropertyChanged(nameof(MatchWhiteAdvantageText));
        OnPropertyChanged(nameof(MatchBlackAdvantageText));
        RefreshMatchBoard();
    }

    public void CreateTournamentDraft()
    {
        ErrorText = null;
        try
        {
            var selected = TournamentEngineChoices.Where(item => item.IsSelected).Select(item => item.Engine).ToList();
            var tournament = _tournamentManager.CreateDraft(selected,
                (int)Math.Round(InitialTimeMinutes * 60), (int)Math.Round(IncrementSeconds), TournamentName,
                AutoStartNextRound, SaveTournamentLocally);
            SelectedTournament = tournament;
            RefreshTournament();
            Message = "Terminarz ligi gotowy — każdy silnik zagra z każdym dwa razy.";
        }
        catch (Exception exception) { ErrorText = exception.Message; }
    }

    public void PrepareNewTournament()
    {
        if (_tournamentManager.Current?.Status is TournamentStatus.Running or TournamentStatus.Paused)
        {
            ErrorText = "Najpierw zakończ bieżący turniej.";
            return;
        }
        _updatingTournamentChoices = true;
        foreach (var choice in TournamentEngineChoices) choice.IsSelected = true;
        _updatingTournamentChoices = false;
        TournamentName = string.Empty;
        SelectedTournament = null;
        if (TournamentEngineChoices.Count >= 2) CreateTournamentDraft();
    }

    private async Task<bool> RestoreActiveTournamentAsync()
    {
        var active = _allTournaments
            .Where(item => item.Status is TournamentStatus.Running or TournamentStatus.Paused)
            .OrderByDescending(item => item.StartedAt ?? item.CreatedAt)
            .FirstOrDefault();
        if (active is null) return false;

        // Po restarcie procesy silników już nie istnieją. Trwającą rundę cofamy do
        // oczekiwania, dzięki czemu użytkownik może bezpiecznie wznowić turniej.
        if (active.Status == TournamentStatus.Running)
        {
            foreach (var match in active.Matches.Where(item => item.Status == TournamentMatchStatus.Running))
                match.Status = TournamentMatchStatus.Pending;
            active.Status = TournamentStatus.Paused;
            await _tournaments.SaveAsync(active);
        }

        _tournamentManager.Load(active);
        TournamentName = active.Name;
        InitialTimeMinutes = active.InitialTimeSeconds / 60d;
        IncrementSeconds = active.IncrementSeconds;
        AutoStartNextRound = active.AutoStartNextRound;
        SaveTournamentLocally = active.SaveLocally;
        _updatingTournamentChoices = true;
        foreach (var choice in TournamentEngineChoices)
            choice.IsSelected = active.ParticipantIds.Contains(choice.Engine.Id);
        _updatingTournamentChoices = false;
        SelectedTournament = active;
        RefreshTournament();
        return true;
    }

    public void RerollTournament()
    {
        _tournamentManager.Reroll(Engines);
        RefreshTournament();
    }

    public async Task StartTournamentAsync()
    {
        if (_tournamentManager.Current is null) { ErrorText = "Najpierw utwórz terminarz."; return; }
        ErrorText = null;
        try
        {
            if (_tournamentManager.Current.Status == TournamentStatus.Draft)
            {
                _tournamentManager.Current.AutoStartNextRound = AutoStartNextRound;
                _tournamentManager.Current.SaveLocally = SaveTournamentLocally;
            }
            await _tournamentManager.StartAsync(Engines);
            await ReloadHistoryAsync();
            if (_tournamentManager.Current is { } current) await LoadTournamentDetailsAsync(current);
        }
        catch (Exception exception) { ErrorText = exception.Message; }
    }

    public Task StopTournamentAsync() => _tournamentManager.StopAsync();

    public async Task<bool> OpenSelectedTournamentReplayAsync()
    {
        if (SelectedTournamentMatch?.SavedGameId is not { } gameId) return false;
        var game = (await _games.GetAllAsync()).FirstOrDefault(item => item.Id == gameId);
        if (game is null) return false;
        SelectedSavedGame = game;
        OpenSavedGameReplay();
        return true;
    }

    public bool OpenSavedGameReplay()
    {
        if (SelectedSavedGame is null) return false;
        var replay = ChessGame.CreateInitial();
        foreach (var move in SelectedSavedGame.UciMoves)
            if (!replay.TryMakeUciMove(move, out _)) return false;

        _observedTournamentSession = null;
        _observerSavedGame = SelectedSavedGame;
        SetObserverGame(replay, SelectedSavedGame.PairingText,
            SelectedSavedGame.WhiteEngine, SelectedSavedGame.BlackEngine,
            $"{(string.IsNullOrWhiteSpace(SelectedSavedGame.ResultReason) ? "Zapisana powtórka" : SelectedSavedGame.ResultReason)} · {SelectedSavedGame.DateText}",
            SelectedSavedGame.Result, false, null);
        return true;
    }

    public void OpenLiveTournamentMatch(TournamentMatchSession session)
    {
        var runner = session.Runner;
        _observedTournamentSession = session;
        _observerSavedGame = null;
        SetObserverGame(runner.Game, session.Title,
            runner.WhiteDefinition?.Name ?? session.Match.EngineAName,
            runner.BlackDefinition?.Name ?? session.Match.EngineBName,
            session.StatusText, runner.Result, session.IsActive, session.Match.Id);
    }

    public async Task OpenTournamentMatchDetailAsync(TournamentMatch match)
    {
        var session = _tournamentManager.Sessions.FirstOrDefault(item => item.Match.Id == match.Id);
        if (session is { IsActive: true })
        {
            OpenLiveTournamentMatch(session);
            return;
        }
        if (match.SavedGameId is { } gameId)
        {
            var saved = (await _games.GetAllAsync()).FirstOrDefault(item => item.Id == gameId);
            if (saved is not null)
            {
                SelectedSavedGame = saved;
                OpenSavedGameReplay();
                return;
            }
        }
        if (session is not null)
        {
            OpenLiveTournamentMatch(session);
            return;
        }
        _observedTournamentSession = null;
        _observerSavedGame = null;
        SetObserverGame(ChessGame.CreateInitial(), match.PairingText,
            match.EngineAName, match.EngineBName, match.StatusText,
            string.IsNullOrWhiteSpace(match.Result) ? "*" : match.Result, false, match.Id);
    }

    public void BrowsePreviousObserverPosition() => ShowObserverPosition(_observerViewedPly - 1);
    public void BrowseNextObserverPosition() => ShowObserverPosition(_observerViewedPly + 1);

    public void ShowObserverPosition(int ply)
    {
        if (ObserverIsLive && !ObserverIsPaused) return;
        _observerViewedPly = Math.Clamp(ply, 0, _observerGame.MoveHistory.Count);
        _observerDisplayedGame = _observerViewedPly >= _observerGame.MoveHistory.Count
            ? null : ReplayMatchToPly(_observerGame, _observerViewedPly);
        NotifyObserverState();
        RefreshObserverBoard();
        RefreshObserverMetadata();
    }

    public void ToggleObservedTournamentMatch()
    {
        if (!CanControlObserverMatch || _observedTournamentSession is null) return;
        if (_observedTournamentSession.Runner.IsPaused)
        {
            _observerViewedPly = _observerGame.MoveHistory.Count;
            _observerDisplayedGame = null;
            _observedTournamentSession.Runner.Resume();
        }
        else
        {
            _observedTournamentSession.Runner.Pause();
        }
        RefreshObservedTournamentMatch();
    }

    public async Task StopObservedTournamentMatchAsync()
    {
        if (!CanControlObserverMatch || _observedTournamentSession is null) return;
        await _observedTournamentSession.Runner.StopAsync();
        RefreshObservedTournamentMatch();
    }

    public void RefreshObserverLiveTelemetry()
    {
        if (_observedTournamentSession is not { IsActive: true } session) return;
        var runner = session.Runner;
        ObserverWhiteClock = FormatClock(runner.DisplayWhiteRemaining);
        ObserverBlackClock = FormatClock(runner.DisplayBlackRemaining);
        ObserverDurationText = runner.StartedAt == default
            ? "Czas trwania: —"
            : $"Czas trwania: {FormatDuration(DateTimeOffset.Now - runner.StartedAt)}";
    }

    private void SetObserverGame(ChessGame game, string title, string white, string black,
        string status, string result, bool isLive, Guid? tournamentMatchId)
    {
        _observerGame = game;
        _observerDisplayedGame = null;
        _observerViewedPly = game.MoveHistory.Count;
        _observedTournamentMatchId = tournamentMatchId;
        ObserverTitle = title;
        ObserverWhiteName = white;
        ObserverBlackName = black;
        ObserverStatus = status;
        ObserverResult = result;
        ObserverIsLive = isLive;
        OnPropertyChanged(nameof(ObserverStateBrush));
        OnPropertyChanged(nameof(ObserverWinnerText));
        RefreshObserverBoard();
        RefreshObserverMoveLog(force: true);
        RefreshObserverMetadata();
        NotifyObserverState();
    }

    private void RefreshObservedTournamentMatch()
    {
        if (_observedTournamentMatchId is not { } id) return;
        var session = _observedTournamentSession ??
            _tournamentManager.Sessions.FirstOrDefault(item => item.Match.Id == id);
        if (session is null) return;
        _observedTournamentSession = session;
        var wasAtEnd = _observerViewedPly >= _observerGame.MoveHistory.Count;
        _observerGame = session.Runner.Game;
        if (wasAtEnd || session.IsActive && !session.Runner.IsPaused)
        {
            _observerViewedPly = _observerGame.MoveHistory.Count;
            _observerDisplayedGame = null;
        }
        ObserverStatus = session.StatusText;
        ObserverResult = session.Runner.Result != "*"
            ? session.Runner.Result
            : session.Match.Result is "1-0" or "0-1" or "1/2-1/2" ? session.Match.Result : "*";
        ObserverIsLive = session.IsActive;
        OnPropertyChanged(nameof(ObserverStateBrush));
        OnPropertyChanged(nameof(ObserverWinnerText));
        RefreshObserverBoard();
        RefreshObserverMoveLog();
        RefreshObserverMetadata();
        NotifyObserverState();
    }

    private void RefreshObserverBoard()
    {
        if (ObserverSquares.Count != 64)
        {
            ObserverSquares.Clear();
            for (var row = 0; row < 8; row++)
                for (var column = 0; column < 8; column++)
                    ObserverSquares.Add(new BoardSquareViewModel(new Square(column, 7 - row), row, column));
        }
        var game = _observerDisplayedGame ?? _observerGame;
        foreach (var square in ObserverSquares)
        {
            var piece = game[square.Square];
            square.Update(piece, false, false,
                game.LastMove is { } move && (move.From == square.Square || move.To == square.Square),
                false, square.DisplayRow == 7, square.DisplayColumn == 0);
        }
        OnPropertyChanged(nameof(ObserverWhiteCapturedPieces));
        OnPropertyChanged(nameof(ObserverBlackCapturedPieces));
    }

    private string FormatObserverCapturedPieces(PieceColor capturingColor)
    {
        var game = _observerDisplayedGame ?? _observerGame;
        var captured = game.MoveHistory
            .Where(record => record.Color == capturingColor && record.CapturedPiece is not null)
            .Select(record => record.CapturedPiece!.Value)
            .OrderByDescending(piece => MatchPieceValue(piece.Type))
            .Select(MatchPieceGlyph).ToArray();
        return captured.Length == 0 ? "—" : string.Join(' ', captured);
    }

    private string FormatObserverAdvantage(PieceColor color)
    {
        var game = _observerDisplayedGame ?? _observerGame;
        var whiteCaptured = game.MoveHistory
            .Where(record => record.Color == PieceColor.White && record.CapturedPiece is not null)
            .Sum(record => MatchPieceValue(record.CapturedPiece!.Value.Type));
        var blackCaptured = game.MoveHistory
            .Where(record => record.Color == PieceColor.Black && record.CapturedPiece is not null)
            .Sum(record => MatchPieceValue(record.CapturedPiece!.Value.Type));
        var balance = color == PieceColor.White ? whiteCaptured - blackCaptured : blackCaptured - whiteCaptured;
        return balance > 0 ? $"+{balance}" : string.Empty;
    }

    private void RefreshObserverMetadata()
    {
        if (_observedTournamentSession is { } session)
        {
            var runner = session.Runner;
            ObserverWhiteName = runner.WhiteDefinition?.Name ?? session.Match.EngineAName;
            ObserverBlackName = runner.BlackDefinition?.Name ?? session.Match.EngineBName;
            var isHistoricalPosition = !session.IsActive || _observerViewedPly < _observerGame.MoveHistory.Count;
            EngineAnalysisSnapshot? evaluation;
            if (isHistoricalPosition)
            {
                (ObserverWhiteClock, ObserverBlackClock) = CalculateReplayClocks(
                    runner.InitialTimeSeconds, runner.IncrementSeconds, runner.Analysis, _observerViewedPly);
                evaluation = runner.Analysis
                    .Where(item => item.Ply <= _observerViewedPly)
                    .OrderBy(item => item.Ply)
                    .LastOrDefault();
            }
            else
            {
                ObserverWhiteClock = FormatClock(runner.DisplayWhiteRemaining);
                ObserverBlackClock = FormatClock(runner.DisplayBlackRemaining);
                evaluation = runner.Evaluation is null ? null : new EngineAnalysisSnapshot
                {
                    WhiteCentipawns = runner.Evaluation.WhiteCentipawns,
                    WhiteMateIn = runner.Evaluation.WhiteMateIn
                };
            }
            ObserverFormatText = $"Format: {FormatTimeControl(runner.InitialTimeSeconds, runner.IncrementSeconds)}";
            var finishedAt = session.IsActive ? DateTimeOffset.Now : runner.LastSavedGame?.FinishedAt ?? DateTimeOffset.Now;
            ObserverDurationText = runner.StartedAt == default
                ? "Czas trwania: —"
                : $"Czas trwania: {FormatDuration(finishedAt - runner.StartedAt)}";
            ObserverTopEvaluationShare = CalculateObserverTopEvaluationShare(
                evaluation?.WhiteCentipawns, evaluation?.WhiteMateIn);
        }
        else if (_observerSavedGame is { } saved)
        {
            var format = !string.IsNullOrWhiteSpace(saved.FormatDescription)
                ? saved.FormatDescription
                : saved.InitialTimeSeconds > 0
                    ? FormatTimeControl(saved.InitialTimeSeconds, saved.IncrementSeconds)
                    : saved.IsPlayerGame ? "Gracz kontra silnik" : "Nie zapisano";
            ObserverFormatText = $"Format: {format}";
            ObserverDurationText = $"Czas trwania: {FormatDuration(saved.FinishedAt - saved.StartedAt)}";
            (ObserverWhiteClock, ObserverBlackClock) = CalculateReplayClocks(
                saved.InitialTimeSeconds, saved.IncrementSeconds, saved.Analysis, _observerViewedPly);
            var evaluation = saved.Analysis
                .Where(item => item.Ply <= _observerViewedPly)
                .OrderBy(item => item.Ply)
                .LastOrDefault();
            ObserverTopEvaluationShare = CalculateObserverTopEvaluationShare(
                evaluation?.WhiteCentipawns, evaluation?.WhiteMateIn);
        }
        else
        {
            var tournament = _detailTournament;
            ObserverWhiteClock = tournament is null ? "—" : FormatClock(TimeSpan.FromSeconds(tournament.InitialTimeSeconds));
            ObserverBlackClock = ObserverWhiteClock;
            ObserverFormatText = tournament is null
                ? "Format: —"
                : $"Format: {FormatTimeControl(tournament.InitialTimeSeconds, tournament.IncrementSeconds)}";
            ObserverDurationText = "Czas trwania: —";
            ObserverTopEvaluationShare = 0.5;
        }

        OnPropertyChanged(nameof(ObserverWhiteAdvantageText));
        OnPropertyChanged(nameof(ObserverBlackAdvantageText));
        OnPropertyChanged(nameof(ObserverIsPaused));
        OnPropertyChanged(nameof(CanControlObserverMatch));
        OnPropertyChanged(nameof(ObserverPrimaryButtonText));
    }

    private static (string White, string Black) CalculateReplayClocks(
        int initialTimeSeconds,
        int incrementSeconds,
        IEnumerable<EngineAnalysisSnapshot> analysis,
        int ply)
    {
        if (initialTimeSeconds <= 0) return ("—", "—");
        var white = TimeSpan.FromSeconds(initialTimeSeconds);
        var black = white;
        foreach (var item in analysis.Where(item => item.Ply <= ply).OrderBy(item => item.Ply))
        {
            var elapsed = TimeSpan.FromMilliseconds(item.ElapsedMilliseconds);
            var increment = TimeSpan.FromSeconds(incrementSeconds);
            if (item.Ply % 2 == 1) white = white - elapsed + increment;
            else black = black - elapsed + increment;
        }
        return (FormatClock(white), FormatClock(black));
    }

    private static double CalculateObserverTopEvaluationShare(int? whiteCentipawns, int? whiteMateIn)
    {
        if (whiteMateIn is { } mate) return mate < 0 ? 0.98 : 0.02;
        return 0.5 + Math.Clamp(-(whiteCentipawns ?? 0) / 1600d, -0.48, 0.48);
    }

    private static string FormatTimeControl(int initialTimeSeconds, int incrementSeconds) =>
        $"{initialTimeSeconds / 60d:0.##} min + {incrementSeconds} s";

    private static string FormatDuration(TimeSpan duration)
    {
        if (duration < TimeSpan.Zero) duration = TimeSpan.Zero;
        return duration.TotalHours >= 1 ? duration.ToString(@"h\:mm\:ss") : duration.ToString(@"mm\:ss");
    }

    private void RefreshObserverMoveLog(bool force = false)
    {
        var currentPly = _observerGame.MoveHistory.Count;
        if (!force && ReferenceEquals(_observerMoveLogGame, _observerGame) && _observerMoveLogPly == currentPly)
            return;

        ObserverMoveLog.Clear();
        foreach (var item in _observerGame.MoveHistory.Select((record, index) => (Record: record, Ply: index + 1)))
        {
            ObserverMoveLog.Add(new MoveLogEntryViewModel(
                item.Record.Color == PieceColor.White ? $"{item.Record.MoveNumber}." : $"{item.Record.MoveNumber}…",
                item.Record.Color == PieceColor.White ? item.Record.Notation : string.Empty,
                item.Record.Color == PieceColor.Black ? item.Record.Notation : string.Empty,
                item.Ply));
        }
        _observerMoveLogGame = _observerGame;
        _observerMoveLogPly = currentPly;
    }

    private void NotifyObserverState()
    {
        OnPropertyChanged(nameof(CanBrowseObserverHistory));
        OnPropertyChanged(nameof(CanBrowsePreviousObserverPosition));
        OnPropertyChanged(nameof(CanBrowseNextObserverPosition));
        OnPropertyChanged(nameof(ObserverPositionText));
    }

    public async Task ReloadHistoryAsync()
    {
        var gameItems = await _games.GetAllAsync();
        SavedGames.Clear(); foreach (var game in gameItems) SavedGames.Add(game);
        var tournamentItems = await _tournaments.GetAllAsync();
        _allTournaments.Clear(); _allTournaments.AddRange(tournamentItems);
        ApplyTournamentFilter();
    }

    public async Task DeleteSelectedSavedGameAsync()
    {
        if (SelectedSavedGame is null) return;
        await _games.DeleteAsync(SelectedSavedGame.Id);
        SelectedSavedGame = null;
        await ReloadHistoryAsync();
    }

    public async Task<bool> OpenSelectedTournamentDetailsAsync()
    {
        if (SelectedTournament is null) return false;
        _detailTournament = SelectedTournament;
        TournamentDetailName = SelectedTournament.Name;
        TournamentDetailStatus = $"{SelectedTournament.StatusText}  ·  {SelectedTournament.ProgressText}  ·  {SelectedTournament.DateRangeText}";
        await LoadTournamentDetailsAsync(SelectedTournament);
        return true;
    }

    public async Task<bool> OpenCurrentTournamentDetailsAsync()
    {
        if (_tournamentManager.Current is not { } current) return false;
        _detailTournament = current;
        TournamentDetailName = current.Name;
        TournamentDetailStatus = $"{current.StatusText}  ·  {current.ProgressText}  ·  {current.DateRangeText}";
        await LoadTournamentDetailsAsync(current);
        return true;
    }

    public void ShowCurrentTournament()
    {
        _detailTournament = null;
        RefreshTournament();
    }

    public async Task DeleteSelectedTournamentAsync()
    {
        if (SelectedTournament is null || IsTournamentRunning && _tournamentManager.Current?.Id == SelectedTournament.Id) return;
        var id = SelectedTournament.Id;
        await _tournaments.DeleteAsync(id);
        SelectedTournament = null;
        await ReloadHistoryAsync();
    }

    private void ApplyTournamentFilter()
    {
        var query = TournamentSearchText.Trim();
        var filtered = string.IsNullOrEmpty(query)
            ? _allTournaments
            : _allTournaments.Where(item =>
                item.Name.Contains(query, StringComparison.CurrentCultureIgnoreCase) ||
                item.ParticipantNames.Any(name => name.Contains(query, StringComparison.CurrentCultureIgnoreCase))).ToList();
        TournamentHistory.Clear();
        foreach (var tournament in filtered) TournamentHistory.Add(tournament);
    }

    private async Task ReloadEnginesAsync()
    {
        var items = await _engineManager.LoadAsync();
        var selectedA = SelectedEngineA?.Id; var selectedB = SelectedEngineB?.Id;
        Engines.Clear(); foreach (var engine in items) Engines.Add(engine);
        foreach (var choice in TournamentEngineChoices) choice.PropertyChanged -= TournamentChoiceOnPropertyChanged;
        TournamentEngineChoices.Clear();
        _updatingTournamentChoices = true;
        foreach (var engine in items)
        {
            var choice = new EngineSelectionItem { Engine = engine, IsSelected = true };
            choice.PropertyChanged += TournamentChoiceOnPropertyChanged;
            TournamentEngineChoices.Add(choice);
        }
        _updatingTournamentChoices = false;
        SelectedEngineA = Engines.FirstOrDefault(item => item.Id == selectedA) ?? Engines.ElementAtOrDefault(0);
        SelectedEngineB = Engines.FirstOrDefault(item => item.Id == selectedB) ?? Engines.ElementAtOrDefault(1) ?? Engines.ElementAtOrDefault(0);
        OnPropertyChanged(nameof(EngineCountText));
    }

    private void TournamentChoiceOnPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_updatingTournamentChoices || e.PropertyName != nameof(EngineSelectionItem.IsSelected)) return;
        OnPropertyChanged(nameof(EngineCountText));
        OnPropertyChanged(nameof(CanStartTournamentRound));
        if (IsTournamentConfigurationEnabled && TournamentEngineChoices.Count(item => item.IsSelected) >= 2)
            CreateTournamentDraft();
    }

    private void MatchOnStateChanged(object? sender, EventArgs e)
    {
        if (_dispatcher.HasThreadAccess)
            RefreshMatch();
        else
            _dispatcher.TryEnqueue(RefreshMatch);
    }

    private void RefreshMatch()
    {
        if (_matchManager.IsRunning)
        {
            _isMatchConfigurationPreview = false;
            _matchConfigurationPreviewGame = null;
            _openedReplay = null;
            _savedReplayGame = null;
        }
        UpdateMatchHistoryPosition();
        MatchWhiteName = _matchManager.WhiteDefinition?.Name ?? MatchWhiteName;
        MatchBlackName = _matchManager.BlackDefinition?.Name ?? MatchBlackName;
        WhiteClock = FormatClock(_matchManager.WhiteRemaining);
        BlackClock = FormatClock(_matchManager.BlackRemaining);
        MatchStatus = _matchManager.StatusText;
        MatchEvaluation = _matchManager.Evaluation?.DisplayText ?? "—";
        MatchDepth = _matchManager.Evaluation?.Depth is { } depth ? $"Depth {depth}" : "Depth —";
        MatchTopEvaluationShare = CalculateMatchTopEvaluationShare();
        CountFinishedMatchInSession();
        RefreshMatchBindings();
        NotifyMatchHistoryState();
        RefreshMatchBoard(); RefreshMatchMoveLog();
    }

    private void RefreshMatchBindings()
    {
        OnPropertyChanged(nameof(IsMatchRunning));
        OnPropertyChanged(nameof(IsMatchPaused));
        OnPropertyChanged(nameof(IsMatchConfigurationEnabled));
        OnPropertyChanged(nameof(IsMatchThinking));
        OnPropertyChanged(nameof(IsWhiteThinking));
        OnPropertyChanged(nameof(IsBlackThinking));
        OnPropertyChanged(nameof(PrimaryMatchButtonText));
        OnPropertyChanged(nameof(MatchActivityText));
        OnPropertyChanged(nameof(MatchOutcomeText));
        OnPropertyChanged(nameof(MatchStateText));
        OnPropertyChanged(nameof(MatchResultText));
        OnPropertyChanged(nameof(MatchStateBrush));
        OnPropertyChanged(nameof(IsMatchStatePulsing));
        OnPropertyChanged(nameof(MatchWhiteCapturedPieces));
        OnPropertyChanged(nameof(MatchBlackCapturedPieces));
        OnPropertyChanged(nameof(MatchWhiteAdvantageText));
        OnPropertyChanged(nameof(MatchBlackAdvantageText));
    }

    private string CurrentMatchEngineName => _matchManager.Game.SideToMove == PieceColor.White
        ? MatchWhiteName
        : MatchBlackName;

    private void SetPendingMatchPlayerNames()
    {
        if (SelectedEngineA is null || SelectedEngineB is null) return;
        switch (SelectedColorOption.Value)
        {
            case EngineColorAssignment.EngineAWhite:
                MatchWhiteName = SelectedEngineA.Name;
                MatchBlackName = SelectedEngineB.Name;
                break;
            case EngineColorAssignment.EngineBWhite:
                MatchWhiteName = SelectedEngineB.Name;
                MatchBlackName = SelectedEngineA.Name;
                break;
            default:
                MatchWhiteName = SelectedEngineA.Name;
                MatchBlackName = SelectedEngineB.Name;
                break;
        }
    }

    private void ApplyMatchConfigurationPreview()
    {
        if (IsMatchRunning || SelectedColorOption is null) return;

        SetPendingMatchPlayerNames();
        if (_matchManager.Game.MoveHistory.Count > 0 || _openedReplay is not null || _matchManager.Result != "*")
        {
            _isMatchConfigurationPreview = true;
            _matchConfigurationPreviewGame = ChessGame.CreateInitial();
            _openedReplay = null;
            _savedReplayGame = null;
            _matchDisplayedHistoryGame = null;
            _matchViewedPly = 0;
            _lastKnownMatchPly = 0;
            MatchStatus = "Ustawienia zmienione — rozpocznij nowy pojedynek";
            MatchEvaluation = "—";
            MatchDepth = "Depth —";
            MatchTopEvaluationShare = 0.5;
        }

        RefreshMatchBindings();
        NotifyMatchHistoryState();
        RefreshMatchBoard();
        RefreshMatchMoveLog();
    }

    private double CalculateMatchTopEvaluationShare()
    {
        if (_matchManager.Evaluation?.WhiteMateIn is { } mate)
            return mate < 0 ? 0.98 : 0.02;

        var whiteCentipawns = _matchManager.Evaluation?.WhiteCentipawns ?? 0;
        return 0.5 + Math.Clamp(-whiteCentipawns / 1600d, -0.48, 0.48);
    }

    private void CountFinishedMatchInSession()
    {
        if (IsMatchRunning || _matchManager.WasStoppedManually || _matchManager.Result == "*" ||
            !_scoredMatchGames.Add(_matchManager.Game)) return;

        _sessionMatchScores.TryAdd(MatchWhiteName, 0);
        _sessionMatchScores.TryAdd(MatchBlackName, 0);
        if (_matchManager.Result == "1-0") _sessionMatchScores[MatchWhiteName] += 1;
        else if (_matchManager.Result == "0-1") _sessionMatchScores[MatchBlackName] += 1;
        else
        {
            _sessionMatchScores[MatchWhiteName] += 0.5;
            _sessionMatchScores[MatchBlackName] += 0.5;
        }
    }

    private string FormatSessionMatchResult()
    {
        var whiteScore = _sessionMatchScores.GetValueOrDefault(MatchWhiteName);
        var blackScore = _sessionMatchScores.GetValueOrDefault(MatchBlackName);
        var high = Math.Max(whiteScore, blackScore);
        var low = Math.Min(whiteScore, blackScore);
        var score = $"{FormatScore(high)}–{FormatScore(low)}";
        if (Math.Abs(whiteScore - blackScore) < 0.001) return $"Remis ({score})";
        return whiteScore > blackScore
            ? $"Wygrywa {MatchWhiteName} ({score})"
            : $"Wygrywa {MatchBlackName} ({score})";
    }

    private static string FormatScore(double score) =>
        score.ToString("0.#", System.Globalization.CultureInfo.CurrentCulture);

    private ChessGame MatchHistoryGame => _matchConfigurationPreviewGame ?? _savedReplayGame ?? _matchManager.Game;
    private ChessGame DisplayedMatchGame => _matchConfigurationPreviewGame ?? _matchDisplayedHistoryGame ?? MatchHistoryGame;

    private void UpdateMatchHistoryPosition()
    {
        var game = _matchManager.Game;
        var currentPly = game.MoveHistory.Count;
        if (!ReferenceEquals(_observedMatchGame, game))
        {
            _observedMatchGame = game;
            _matchViewedPly = currentPly;
            _lastKnownMatchPly = currentPly;
            _matchDisplayedHistoryGame = null;
            return;
        }

        if (_matchViewedPly >= _lastKnownMatchPly || IsMatchRunning && !IsMatchPaused)
        {
            _matchViewedPly = currentPly;
            _matchDisplayedHistoryGame = null;
        }
        else if (_matchViewedPly < currentPly)
        {
            _matchDisplayedHistoryGame = ReplayMatchToPly(game, _matchViewedPly);
        }

        _lastKnownMatchPly = currentPly;
    }

    private static ChessGame ReplayMatchToPly(ChessGame source, int ply)
    {
        var replay = ChessGame.CreateInitial();
        foreach (var record in source.MoveHistory.Take(Math.Clamp(ply, 0, source.MoveHistory.Count)))
        {
            if (!replay.TryMakeUciMove(record.Move.Uci, out _))
                throw new InvalidOperationException($"Nie można odtworzyć ruchu {record.Move.Uci}.");
        }
        return replay;
    }

    private void NotifyMatchHistoryState()
    {
        OnPropertyChanged(nameof(IsViewingMatchLivePosition));
        OnPropertyChanged(nameof(CanBrowseMatchHistory));
        OnPropertyChanged(nameof(CanBrowsePreviousMatchPosition));
        OnPropertyChanged(nameof(CanBrowseNextMatchPosition));
        OnPropertyChanged(nameof(MatchHistoryPositionText));
    }

    private string FormatMatchCapturedPieces(PieceColor capturingColor)
    {
        var captured = DisplayedMatchGame.MoveHistory
            .Where(record => record.Color == capturingColor && record.CapturedPiece is not null)
            .Select(record => record.CapturedPiece!.Value)
            .OrderByDescending(piece => MatchPieceValue(piece.Type))
            .Select(MatchPieceGlyph)
            .ToArray();
        return captured.Length == 0 ? "—" : string.Join(' ', captured);
    }

    private string FormatMatchAdvantage(PieceColor color)
    {
        var whiteCaptured = DisplayedMatchGame.MoveHistory
            .Where(record => record.Color == PieceColor.White && record.CapturedPiece is not null)
            .Sum(record => MatchPieceValue(record.CapturedPiece!.Value.Type));
        var blackCaptured = DisplayedMatchGame.MoveHistory
            .Where(record => record.Color == PieceColor.Black && record.CapturedPiece is not null)
            .Sum(record => MatchPieceValue(record.CapturedPiece!.Value.Type));
        var balance = color == PieceColor.White ? whiteCaptured - blackCaptured : blackCaptured - whiteCaptured;
        return balance > 0 ? $"+{balance}" : string.Empty;
    }

    private static int MatchPieceValue(PieceType type) => type switch
    {
        PieceType.Queen => 9,
        PieceType.Rook => 5,
        PieceType.Bishop or PieceType.Knight => 3,
        PieceType.Pawn => 1,
        _ => 0
    };

    private static string MatchPieceGlyph(Piece piece) => (piece.Color, piece.Type) switch
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

    private void RefreshMatchBoard()
    {
        if (MatchSquares.Count != 64)
        {
            MatchSquares.Clear();
            for (var row = 0; row < 8; row++)
                for (var column = 0; column < 8; column++)
                    MatchSquares.Add(new BoardSquareViewModel(new Square(column, 7 - row), row, column));
        }
        foreach (var square in MatchSquares)
        {
            var piece = DisplayedMatchGame[square.Square];
            square.Update(piece, false, false,
                DisplayedMatchGame.LastMove is { } move && (move.From == square.Square || move.To == square.Square),
                false, square.DisplayRow == 7, square.DisplayColumn == 0);
        }
    }

    private void RefreshMatchMoveLog()
    {
        MatchMoveLog.Clear();
        foreach (var group in MatchHistoryGame.MoveHistory
                     .Select((record, index) => (Record: record, Ply: index + 1))
                     .GroupBy(item => item.Record.MoveNumber))
            MatchMoveLog.Add(new MoveLogEntryViewModel($"{group.Key}.",
                group.FirstOrDefault(item => item.Record.Color == PieceColor.White).Record?.Notation ?? string.Empty,
                group.FirstOrDefault(item => item.Record.Color == PieceColor.Black).Record?.Notation ?? string.Empty,
                group.Max(item => item.Ply)));
    }

    private void TournamentOnStateChanged(object? sender, EventArgs e) => _dispatcher.TryEnqueue(RefreshTournament);

    private void TournamentMatchOnStateChanged(object? sender, TournamentMatchStateChangedEventArgs e) =>
        _dispatcher.TryEnqueue(() =>
        {
            if (_observedTournamentMatchId == e.MatchId)
                RefreshObservedTournamentMatch();
        });

    private void RefreshTournament()
    {
        if (_detailTournament is not null && _detailTournament.Id != _tournamentManager.Current?.Id)
        {
            NotifyTournamentCollectionState();
            OnPropertyChanged(nameof(IsTournamentRunning));
            return;
        }
        BracketMatches.Clear();
        CompletedTournamentMatches.Clear();
        UpcomingTournamentMatches.Clear();
        if (_tournamentManager.Current is { } current)
        {
            foreach (var match in current.Matches.OrderBy(item => item.Round).ThenBy(item => item.Index))
            {
                BracketMatches.Add(match);
                if (match.Status == TournamentMatchStatus.Completed) CompletedTournamentMatches.Add(match);
                else if (match.Status == TournamentMatchStatus.Pending) UpcomingTournamentMatches.Add(match);
            }
            _ = RefreshCurrentTournamentStatisticsAsync(current.Id);
        }
        if (_detailTournament is { } detail && detail.Id == _tournamentManager.Current?.Id)
        {
            TournamentDetailStatus = $"{detail.StatusText}  ·  {detail.ProgressText}  ·  {detail.DateRangeText}";
        }
        OnPropertyChanged(nameof(IsTournamentDraft));
        OnPropertyChanged(nameof(IsTournamentRunning));
        OnPropertyChanged(nameof(CanStopTournament));
        OnPropertyChanged(nameof(CanCreateNewTournament));
        OnPropertyChanged(nameof(IsTournamentConfigurationEnabled));
        OnPropertyChanged(nameof(CanStartTournamentRound));
        OnPropertyChanged(nameof(TournamentStartButtonText));
        OnPropertyChanged(nameof(TournamentStatusText));
        OnPropertyChanged(nameof(TournamentRoundText));
        NotifyTournamentCollectionState();
    }

    private async Task RefreshCurrentTournamentStatisticsAsync(Guid tournamentId)
    {
        await ReplaceTournamentStatisticsAsync(tournamentId, clearBeforeLoad: false);
    }

    private async Task LoadTournamentDetailsAsync(EngineTournament? tournament)
    {
        if (tournament is null)
        {
            Interlocked.Increment(ref _tournamentStatisticsLoadVersion);
            TournamentStatistics.Clear();
            NotifyTournamentCollectionState();
            return;
        }
        BracketMatches.Clear();
        CompletedTournamentMatches.Clear();
        UpcomingTournamentMatches.Clear();
        foreach (var match in tournament.Matches.OrderBy(item => item.Round).ThenBy(item => item.Index))
        {
            BracketMatches.Add(match);
            if (match.Status == TournamentMatchStatus.Completed) CompletedTournamentMatches.Add(match);
            else if (match.Status == TournamentMatchStatus.Pending) UpcomingTournamentMatches.Add(match);
        }
        NotifyTournamentCollectionState();
        await ReplaceTournamentStatisticsAsync(tournament.Id, clearBeforeLoad: true);
    }

    private async Task ReplaceTournamentStatisticsAsync(Guid tournamentId, bool clearBeforeLoad)
    {
        var loadVersion = Interlocked.Increment(ref _tournamentStatisticsLoadVersion);
        if (clearBeforeLoad)
        {
            TournamentStatistics.Clear();
            NotifyTournamentCollectionState();
        }

        var statistics = await _tournamentManager.GetStatisticsAsync(tournamentId);
        if (loadVersion != Volatile.Read(ref _tournamentStatisticsLoadVersion)) return;

        TournamentStatistics.Clear();
        foreach (var statistic in statistics
                     .GroupBy(item => item.EngineId)
                     .Select(group => group.First()))
            TournamentStatistics.Add(statistic);
        NotifyTournamentCollectionState();
    }

    private void NotifyTournamentCollectionState()
    {
        OnPropertyChanged(nameof(TournamentStatisticsEmptyVisibility));
        OnPropertyChanged(nameof(BracketMatchesEmptyVisibility));
        OnPropertyChanged(nameof(CompletedMatchesEmptyVisibility));
        OnPropertyChanged(nameof(UpcomingMatchesEmptyVisibility));
    }

    private static string FormatClock(TimeSpan value)
    {
        if (value < TimeSpan.Zero) value = TimeSpan.Zero;
        return value.TotalHours >= 1 ? value.ToString(@"h\:mm\:ss") : value.ToString(@"mm\:ss\.f");
    }

    public async ValueTask DisposeAsync()
    {
        _matchManager.StateChanged -= MatchOnStateChanged;
        _tournamentManager.StateChanged -= TournamentOnStateChanged;
        _tournamentManager.MatchStateChanged -= TournamentMatchOnStateChanged;
        await _tournamentManager.StopAsync();
        await _matchManager.DisposeAsync();
    }
}

public sealed record EngineColorOption(string Name, EngineColorAssignment Value);
