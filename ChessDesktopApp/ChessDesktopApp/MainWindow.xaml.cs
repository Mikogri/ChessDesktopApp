using Chess.Core;
using ChessDesktopApp.Services;
using ChessDesktopApp.Styling;
using ChessDesktopApp.ViewModels;
using System.ComponentModel;
using Microsoft.UI.Windowing;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Input;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;
using Windows.ApplicationModel.DataTransfer;
using Windows.Graphics;
using Windows.Storage.Pickers;

namespace ChessDesktopApp;

public sealed partial class MainWindow : Window
{
    private static Style DangerButtonStyle =>
        (Style)Application.Current.Resources["ChessDangerButtonStyle"];

    private readonly GameSessionService _session;
    private int _animatedMoveCount;
    private Storyboard? _statusPulse;
    private Storyboard? _matchStatusPulse;
    private Storyboard? _advantageStoryboard;
    private bool _advantageBarInitialized;
    private Storyboard? _matchAdvantageStoryboard;
    private bool _matchAdvantageBarInitialized;
    private Storyboard? _observerAdvantageStoryboard;
    private bool _observerAdvantageBarInitialized;
    private readonly DispatcherTimer _observerTelemetryTimer = new() { Interval = TimeSpan.FromMilliseconds(100) };
    private string _currentSection = "game";
    private string _observerReturnSection = "games";
    private string _tournamentDetailReturnSection = "tournaments";

    public MainWindow(GameViewModel viewModel, EngineHubViewModel hub, GameSessionService session)
    {
        ViewModel = viewModel;
        Hub = hub;
        _session = session;
        InitializeComponent();
        try
        {
            SystemBackdrop = new Microsoft.UI.Xaml.Media.MicaBackdrop();
        }
        catch
        {
        }
        RootLayout.DataContext = ViewModel;
        UpdateStatusPulse();
        UpdateMatchStatusPulse();
        UpdateAdvantageBar();
        UpdateMatchAdvantageBar();
        UpdateObserverAdvantageBar();
        ApplyLanguage();
        ViewModel.PromotionSelector = ShowPromotionDialogAsync;
        _session.StateChanged += SessionOnStateChanged;
        Hub.PropertyChanged += HubOnPropertyChanged;
        _observerTelemetryTimer.Tick += ObserverTelemetryTimerOnTick;
        _observerTelemetryTimer.Start();
        Closed += OnClosed;
        ConfigureWindow();
        _ = Hub.InitializeAsync();
        _ = ViewModel.InitializeAsync();
    }

    public GameViewModel ViewModel { get; }
    public EngineHubViewModel Hub { get; }

    private void ShellNavigation_SelectionChanged(NavigationView sender, NavigationViewSelectionChangedEventArgs args)
    {
        if (args.SelectedItemContainer?.Tag is string section)
        {
            ShowSection(section);
        }
    }

    private void ShowSection(string section)
    {
        _currentSection = section;
        RootLayout.Visibility = section == "game" ? Visibility.Visible : Visibility.Collapsed;
        EngineMatchView.Visibility = section == "match" ? Visibility.Visible : Visibility.Collapsed;
        TournamentView.Visibility = section == "tournaments" ? Visibility.Visible : Visibility.Collapsed;
        SavedGamesView.Visibility = section == "games" ? Visibility.Visible : Visibility.Collapsed;
        TournamentHistoryView.Visibility = section == "history" ? Visibility.Visible : Visibility.Collapsed;
        EnginesView.Visibility = section == "engines" ? Visibility.Visible : Visibility.Collapsed;
        TournamentDetailView.Visibility = section == "tournament-details" ? Visibility.Visible : Visibility.Collapsed;
        ObserverDetailView.Visibility = section == "observer" ? Visibility.Visible : Visibility.Collapsed;
        SettingsView.Visibility = section == "settings" ? Visibility.Visible : Visibility.Collapsed;
        if (section is "games" or "history") _ = Hub.ReloadHistoryAsync();
        if (section == "tournaments") Hub.ShowCurrentTournament();
        ApplyLanguage();
    }

    private async void AddEngine_Click(object sender, RoutedEventArgs e)
    {
        var picker = new FileOpenPicker();
        picker.FileTypeFilter.Add(".exe");
        picker.SuggestedStartLocation = PickerLocationId.Downloads;
        var handle = WinRT.Interop.WindowNative.GetWindowHandle(this);
        WinRT.Interop.InitializeWithWindow.Initialize(picker, handle);
        var file = await picker.PickSingleFileAsync();
        if (file is not null) await Hub.AddEngineAsync(file.Path);
    }

    private async void ValidateEngine_Click(object sender, RoutedEventArgs e) => await Hub.ValidateSelectedEngineAsync();

    private async void RemoveEngine_Click(object sender, RoutedEventArgs e)
    {
        if (Hub.SelectedManagedEngine is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = ShellNavigation.XamlRoot,
            Title = "Usunąć silnik z konfiguracji?",
            Content = $"Plik {Hub.SelectedManagedEngine.Name} pozostanie na dysku.",
            PrimaryButtonText = "Usuń",
            PrimaryButtonStyle = DangerButtonStyle,
            CloseButtonText = "Anuluj",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary) await Hub.RemoveSelectedEngineAsync();
    }

    private async void ToggleEngineMatch_Click(object sender, RoutedEventArgs e)
    {
        if (!Hub.IsMatchRunning)
        {
            await Hub.StartMatchAsync();
        }
        else if (Hub.IsMatchPaused)
        {
            Hub.ResumeMatch();
        }
        else
        {
            Hub.PauseMatch();
        }
    }

    private async void StopEngineMatch_Click(object sender, RoutedEventArgs e) => await Hub.StopMatchAsync();

    private void GameMoveLog_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListView list || list.SelectedItem is not MoveLogEntryViewModel entry) return;
        if (ViewModel.CanBrowseHistory) ViewModel.ShowHistoryPosition(entry.Ply);
        else list.SelectedItem = null;
    }

    private void MatchMoveLog_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListView list || list.SelectedItem is not MoveLogEntryViewModel entry) return;
        if (Hub.CanBrowseMatchHistory) Hub.ShowMatchHistoryPosition(entry.Ply);
        else list.SelectedItem = null;
    }

    private void GameHistoryPrevious_Click(object sender, RoutedEventArgs e) => ViewModel.BrowsePreviousPosition();
    private void GameHistoryNext_Click(object sender, RoutedEventArgs e) => ViewModel.BrowseNextPosition();
    private void MatchHistoryPrevious_Click(object sender, RoutedEventArgs e) => Hub.BrowsePreviousMatchPosition();
    private void MatchHistoryNext_Click(object sender, RoutedEventArgs e) => Hub.BrowseNextMatchPosition();
    private void CreateTournament_Click(object sender, RoutedEventArgs e) => Hub.CreateTournamentDraft();
    private void RerollTournament_Click(object sender, RoutedEventArgs e) => Hub.RerollTournament();
    private async void StartTournament_Click(object sender, RoutedEventArgs e) => await Hub.StartTournamentAsync();
    private async void StopTournament_Click(object sender, RoutedEventArgs e) => await Hub.StopTournamentAsync();

    private void NewTournament_Click(object sender, RoutedEventArgs e)
    {
        Hub.PrepareNewTournament();
        NavigateToSection("tournaments");
    }

    private async void TournamentHistory_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ChessDesktopApp.Models.EngineTournament tournament) return;
        Hub.SelectedTournament = tournament;
        if (!await Hub.OpenSelectedTournamentDetailsAsync()) return;
        _tournamentDetailReturnSection = "history";
        NavigateToSection("tournament-details");
    }

    private void TournamentSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (TournamentStandingsContent is null || TournamentCompletedContent is null ||
            TournamentUpcomingContent is null || TournamentStatisticsContent is null) return;
        var index = sender.Items.IndexOf(sender.SelectedItem);
        TournamentStandingsContent.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        TournamentCompletedContent.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        TournamentUpcomingContent.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        TournamentStatisticsContent.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void TournamentDetailSelector_SelectionChanged(SelectorBar sender, SelectorBarSelectionChangedEventArgs args)
    {
        if (TournamentDetailStandingsContent is null || TournamentDetailMatchesContent is null ||
            TournamentDetailUpcomingContent is null || TournamentDetailStatisticsContent is null) return;
        var index = sender.Items.IndexOf(sender.SelectedItem);
        TournamentDetailStandingsContent.Visibility = index == 0 ? Visibility.Visible : Visibility.Collapsed;
        TournamentDetailMatchesContent.Visibility = index == 1 ? Visibility.Visible : Visibility.Collapsed;
        TournamentDetailUpcomingContent.Visibility = index == 2 ? Visibility.Visible : Visibility.Collapsed;
        TournamentDetailStatisticsContent.Visibility = index == 3 ? Visibility.Visible : Visibility.Collapsed;
    }

    private async void CompletedTournamentMatch_ItemClick(object sender, ItemClickEventArgs e)
    {
        if (e.ClickedItem is not ChessDesktopApp.Models.TournamentMatch match) return;
        Hub.SelectedTournamentMatch = match;
        await Hub.OpenTournamentMatchDetailAsync(match);
        _observerReturnSection = "tournaments";
        NavigateToSection("observer");
    }

    private async void DeleteTournament_Click(object sender, RoutedEventArgs e)
    {
        if (Hub.SelectedTournament is null) return;
        await ConfirmAndDeleteSelectedTournamentAsync();
    }

    private async void DeleteTournamentFromMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: ChessDesktopApp.Models.EngineTournament tournament }) return;
        Hub.SelectedTournament = tournament;
        await ConfirmAndDeleteSelectedTournamentAsync();
    }

    private async Task ConfirmAndDeleteSelectedTournamentAsync()
    {
        if (Hub.SelectedTournament is null) return;
        var dialog = new ContentDialog
        {
            XamlRoot = ShellNavigation.XamlRoot,
            Title = "Usunąć turniej z historii?",
            Content = $"Turniej „{Hub.SelectedTournament.Name}” zostanie usunięty z lokalnej historii.",
            PrimaryButtonText = "Usuń",
            PrimaryButtonStyle = DangerButtonStyle,
            CloseButtonText = "Anuluj",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
            await Hub.DeleteSelectedTournamentAsync();
    }

    private async void DeleteSavedGameFromMenu_Click(object sender, RoutedEventArgs e)
    {
        if (sender is not MenuFlyoutItem { Tag: ChessDesktopApp.Models.SavedEngineGame game }) return;
        Hub.SelectedSavedGame = game;
        await ConfirmAndDeleteSavedGameAsync(game);
    }

    private async void DeleteSelectedSavedGame_Click(object sender, RoutedEventArgs e)
    {
        if (Hub.SelectedSavedGame is { } game) await ConfirmAndDeleteSavedGameAsync(game);
    }

    private async Task ConfirmAndDeleteSavedGameAsync(ChessDesktopApp.Models.SavedEngineGame game)
    {
        var dialog = new ContentDialog
        {
            XamlRoot = ShellNavigation.XamlRoot,
            Title = "Usunąć zapisaną partię?",
            Content = $"Partia „{game.PairingText}” z {game.DateText} zostanie trwale usunięta z lokalnej historii.",
            PrimaryButtonText = "Usuń",
            PrimaryButtonStyle = DangerButtonStyle,
            CloseButtonText = "Anuluj",
            DefaultButton = ContentDialogButton.Close
        };
        if (await dialog.ShowAsync() == ContentDialogResult.Primary)
        {
            Hub.SelectedSavedGame = game;
            await Hub.DeleteSelectedSavedGameAsync();
        }
    }

    private void NavigateToSection(string section)
    {
        var item = ShellNavigation.MenuItems.OfType<NavigationViewItem>()
            .FirstOrDefault(entry => string.Equals(entry.Tag as string, section, StringComparison.Ordinal));
        if (item is not null) ShellNavigation.SelectedItem = item;
        ShowSection(section);
    }

    private async void OpenTournamentDetails_Click(object sender, RoutedEventArgs e)
    {
        if (!await Hub.OpenCurrentTournamentDetailsAsync()) return;
        _tournamentDetailReturnSection = "tournaments";
        NavigateToSection("tournament-details");
    }

    private void BackFromTournamentDetail_Click(object sender, RoutedEventArgs e) =>
        NavigateToSection(_tournamentDetailReturnSection);

    private void BackFromObserver_Click(object sender, RoutedEventArgs e) =>
        NavigateToSection(_observerReturnSection);

    private async void TournamentMatchDetail_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListView list || list.SelectedItem is not ChessDesktopApp.Models.TournamentMatch match) return;
        list.SelectedItem = null;
        await Hub.OpenTournamentMatchDetailAsync(match);
        _observerReturnSection = "tournament-details";
        NavigateToSection("observer");
    }

    private void ObserverMoveLog_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (sender is not ListView list || list.SelectedItem is not MoveLogEntryViewModel entry) return;
        if (Hub.CanBrowseObserverHistory) Hub.ShowObserverPosition(entry.Ply);
        else list.SelectedItem = null;
    }

    private void ObserverPrevious_Click(object sender, RoutedEventArgs e) => Hub.BrowsePreviousObserverPosition();
    private void ObserverNext_Click(object sender, RoutedEventArgs e) => Hub.BrowseNextObserverPosition();
    private void ToggleObservedMatch_Click(object sender, RoutedEventArgs e) => Hub.ToggleObservedTournamentMatch();
    private async void StopObservedMatch_Click(object sender, RoutedEventArgs e) => await Hub.StopObservedTournamentMatchAsync();

    private async void OpenTournamentReplay_Click(object sender, RoutedEventArgs e)
    {
        if (!await Hub.OpenSelectedTournamentReplayAsync()) return;
        _observerReturnSection = "tournaments";
        NavigateToSection("observer");
    }

    private void OpenSavedGameOnBoard_Click(object sender, RoutedEventArgs e)
    {
        if (!Hub.OpenSavedGameReplay()) return;
        _observerReturnSection = "games";
        NavigateToSection("observer");
    }

    private void WatchCurrentTournamentMatch_Click(object sender, RoutedEventArgs e)
    {
        _tournamentDetailReturnSection = "tournaments";
        NavigateToSection("tournament-details");
    }

    private async void Square_Click(object sender, RoutedEventArgs e)
    {
        if (GetSquare(sender) is { } square)
        {
            await ViewModel.SelectSquareAsync(square);
        }
    }

    private void Square_DragStarting(UIElement sender, DragStartingEventArgs args)
    {
        var square = GetSquare(sender);
        if (!ViewModel.CanInteract || square is null ||
            string.IsNullOrEmpty(square.PieceGlyph))
        {
            args.Cancel = true;
            return;
        }

        args.Data.SetText(square.SquareName);
        args.Data.RequestedOperation = DataPackageOperation.Move;
        args.DragUI.SetContentFromDataPackage();
    }

    private void Square_DragOver(object sender, DragEventArgs e)
    {
        e.AcceptedOperation = ViewModel.CanInteract ? DataPackageOperation.Move : DataPackageOperation.None;
        e.DragUIOverride.Caption = "Wykonaj ruch";
        e.DragUIOverride.IsCaptionVisible = true;
    }

    private async void Square_Drop(object sender, DragEventArgs e)
    {
        var target = GetSquare(sender);
        if (!e.DataView.Contains(StandardDataFormats.Text) ||
            target is null)
        {
            return;
        }

        var from = await e.DataView.GetTextAsync();
        await ViewModel.DropAsync(from, target);
    }

    private void ConfigureWindow()
    {
        try
        {
            var windowHandle = WinRT.Interop.WindowNative.GetWindowHandle(this);
            var windowId = Microsoft.UI.Win32Interop.GetWindowIdFromWindow(windowHandle);
            var appWindow = AppWindow.GetFromWindowId(windowId);
            if (AppWindowTitleBar.IsCustomizationSupported())
            {
                var titleBar = appWindow.TitleBar;
                var appBackground = ChessThemeResources.Color(
                    "ChessTitleBarColor", Windows.UI.Color.FromArgb(0xFF, 0x20, 0x20, 0x20));
                var darkHover = ChessThemeResources.Color(
                    "ChessTitleBarHoverColor", Windows.UI.Color.FromArgb(0xFF, 0x2B, 0x2B, 0x2B));
                var white = ChessThemeResources.Color(
                    "ChessWhitePieceColor", Windows.UI.Color.FromArgb(0xFF, 0xFF, 0xFF, 0xFF));
                titleBar.BackgroundColor = appBackground;
                titleBar.ForegroundColor = white;
                titleBar.ButtonBackgroundColor = appBackground;
                titleBar.ButtonForegroundColor = white;
                titleBar.ButtonHoverBackgroundColor = darkHover;
                titleBar.ButtonHoverForegroundColor = white;
                titleBar.ButtonPressedBackgroundColor = ChessThemeResources.Color(
                    "ChessTitleBarPressedColor", Windows.UI.Color.FromArgb(0xFF, 0x35, 0x35, 0x35));
                titleBar.ButtonPressedForegroundColor = white;
                titleBar.InactiveBackgroundColor = appBackground;
                titleBar.InactiveForegroundColor = ChessThemeResources.Color(
                    "ChessTitleBarInactiveTextColor", Windows.UI.Color.FromArgb(0xFF, 0xB8, 0xB8, 0xB8));
            }
            appWindow.Resize(new SizeInt32(1280, 840));
            if (appWindow.Presenter is OverlappedPresenter presenter)
            {
                presenter.PreferredMinimumWidth = 980;
                presenter.PreferredMinimumHeight = 680;
            }
        }
        catch
        {
            // Rozmiar okna jest tylko preferencją; brak obsługi AppWindow nie może przerwać startu aplikacji.
        }
    }

    private void RootLayout_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        // Plansza wyznacza szerokość lewej kolumny; prawa przejmuje całą resztę okna.
        const double rightColumnMinimum = 380;
        const double maximumBoardLength = 650;
        const double verticalContentOutsideBoard = 170;
        const double leftColumnMargin = 48;
        var boardFromHeight = Math.Max(360, e.NewSize.Height - verticalContentOutsideBoard);
        var boardFromWidth = Math.Max(360, e.NewSize.Width - rightColumnMinimum - leftColumnMargin);
        GameColumn.Width = Math.Min(maximumBoardLength, Math.Min(boardFromHeight, boardFromWidth));
    }

    private void EngineMatchView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        const double rightColumnMinimum = 420;
        const double maximumBoardLength = 650;
        const double verticalContentOutsideBoard = 170;
        const double leftColumnMargin = 48;
        var boardFromHeight = Math.Max(360, e.NewSize.Height - verticalContentOutsideBoard);
        var boardFromWidth = Math.Max(360, e.NewSize.Width - rightColumnMinimum - leftColumnMargin);
        EngineMatchColumn.Width = Math.Min(maximumBoardLength, Math.Min(boardFromHeight, boardFromWidth));
    }

    private void ObserverDetailView_SizeChanged(object sender, SizeChangedEventArgs e)
    {
        const double rightColumnMinimum = 420;
        const double maximumBoardLength = 650;
        const double verticalContentOutsideBoard = 170;
        const double leftColumnMargin = 48;
        var boardFromHeight = Math.Max(360, e.NewSize.Height - verticalContentOutsideBoard);
        var boardFromWidth = Math.Max(360, e.NewSize.Width - rightColumnMinimum - leftColumnMargin);
        ObserverMatchColumn.Width = Math.Min(maximumBoardLength, Math.Min(boardFromHeight, boardFromWidth));
    }

    private async void OnClosed(object sender, WindowEventArgs args)
    {
        _observerTelemetryTimer.Stop();
        _observerTelemetryTimer.Tick -= ObserverTelemetryTimerOnTick;
        _session.StateChanged -= SessionOnStateChanged;
        Hub.PropertyChanged -= HubOnPropertyChanged;
        await Hub.DisposeAsync();
        await _session.DisposeAsync();
    }

    private void ObserverTelemetryTimerOnTick(object? sender, object e)
    {
        if (ObserverDetailView.Visibility == Visibility.Visible && Hub.ObserverIsLive)
            Hub.RefreshObserverLiveTelemetry();
    }

    private async Task<Chess.Core.PieceType?> ShowPromotionDialogAsync()
    {
        var selector = new ListView
        {
            SelectionMode = ListViewSelectionMode.Single,
            SelectedIndex = 0,
            HorizontalAlignment = HorizontalAlignment.Stretch,
            MaxHeight = 310
        };
        foreach (var option in ViewModel.PromotionOptions)
        {
            var item = new Grid
            {
                Tag = option,
                MinHeight = 58,
                Padding = new Thickness(8, 4, 8, 4),
                ColumnSpacing = 14,
                HorizontalAlignment = HorizontalAlignment.Stretch
            };
            item.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });
            item.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });

            var glyph = new TextBlock
            {
                Text = option.Glyph,
                FontFamily = new FontFamily("Segoe UI Symbol"),
                FontSize = 34,
                VerticalAlignment = VerticalAlignment.Center
            };
            var label = new TextBlock
            {
                Text = option.Name,
                FontSize = 16,
                FontWeight = Microsoft.UI.Text.FontWeights.SemiBold,
                VerticalAlignment = VerticalAlignment.Center
            };
            Grid.SetColumn(label, 1);
            item.Children.Add(glyph);
            item.Children.Add(label);
            selector.Items.Add(item);
        }

        var dialog = new ContentDialog
        {
            XamlRoot = RootLayout.XamlRoot,
            Title = "Promocja pionka",
            Content = new StackPanel
            {
                Spacing = 10,
                Children =
                {
                    new TextBlock
                    {
                        Text = "Zaznacz figurę, w którą ma zmienić się pionek, a następnie potwierdź wybór.",
                        TextWrapping = TextWrapping.Wrap
                    },
                    selector
                }
            },
            PrimaryButtonText = "Potwierdź wybór",
            CloseButtonText = "Anuluj",
            DefaultButton = ContentDialogButton.Primary
        };

        var result = await dialog.ShowAsync();
        return result == ContentDialogResult.Primary &&
               selector.SelectedItem is Grid { Tag: PromotionOption selectedOption }
            ? selectedOption.Value
            : null;
    }

    private void SessionOnStateChanged(object? sender, EventArgs e)
    {
        DispatcherQueue.TryEnqueue(() =>
        {
            UpdateStatusPulse();
            UpdateAdvantageBar();
        });
        var moveCount = _session.Game.MoveHistory.Count;
        if (moveCount == 0)
        {
            _animatedMoveCount = 0;
            return;
        }

        if (moveCount <= _animatedMoveCount)
        {
            return;
        }

        _animatedMoveCount = moveCount;
        var move = _session.Game.MoveHistory[^1].Move;
        DispatcherQueue.TryEnqueue(() => AnimateMove(move));
    }

    private void HubOnPropertyChanged(object? sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(EngineHubViewModel.MatchResultText) or nameof(EngineHubViewModel.IsMatchStatePulsing))
            DispatcherQueue.TryEnqueue(UpdateMatchStatusPulse);
        if (e.PropertyName == nameof(EngineHubViewModel.MatchTopEvaluationShare))
            DispatcherQueue.TryEnqueue(UpdateMatchAdvantageBar);
        if (e.PropertyName == nameof(EngineHubViewModel.ObserverTopEvaluationShare))
            DispatcherQueue.TryEnqueue(UpdateObserverAdvantageBar);
        if (e.PropertyName == nameof(EngineHubViewModel.SelectedLanguageIndex))
            DispatcherQueue.TryEnqueue(ApplyLanguage);
    }

    private static readonly IReadOnlyDictionary<string, string> PolishToEnglish = new Dictionary<string, string>
    {
        ["Gra"] = "Game", ["Gra z botem"] = "Play against bot", ["Pojedynek silników"] = "Engine match", ["Turnieje silników"] = "Engine tournaments",
        ["Zapisane partie silników"] = "Saved engine games", ["Historia turniejów"] = "Tournament history",
        ["Silniki / konfiguracja"] = "Engines / configuration", ["Ustawienia"] = "Settings",
        ["Nowy turniej"] = "New tournament", ["Szczegóły"] = "Details", ["Konfiguracja"] = "Configuration",
        ["Nazwa turnieju"] = "Tournament name", ["Wpisz nazwę turnieju"] = "Enter tournament name",
        ["Czas (min)"] = "Time (min)", ["Inkrement (s)"] = "Increment (s)",
        ["Automatycznie uruchamiaj następną rundę"] = "Automatically start the next round",
        ["Zapisz turniej i powtórki lokalnie"] = "Save tournament and replays locally", ["Silniki"] = "Engines",
        ["Przetasuj"] = "Shuffle", ["Rozpocznij turniej"] = "Start tournament", ["Uruchom następną rundę"] = "Start next round",
        ["Zakończ turniej"] = "End tournament", ["Przebieg turnieju"] = "Tournament progress",
        ["Oglądaj bieżący mecz"] = "Watch current match", ["Tabela"] = "Standings", ["Zakończone mecze"] = "Completed matches",
        ["Nadchodzące"] = "Upcoming", ["Statystyki"] = "Statistics", ["Na żywo"] = "Live", ["Wszystkie mecze"] = "All matches",
        ["Otwórz powtórkę wybranego meczu"] = "Open selected match replay", ["Otwórz na planszy"] = "Open on board",
        ["Ruchy"] = "Moves", ["Pozycja końcowa"] = "Final position", ["Język aplikacji"] = "Application language",
        ["Wersja API"] = "API version", ["Środowisko uruchomieniowe"] = "Runtime environment",
        ["Język i informacje o aplikacji"] = "Language and application information",
        ["Usuń turniej"] = "Delete tournament", ["Szukaj po nazwie turnieju lub silnika"] = "Search by tournament or engine name",
        ["Start — koniec"] = "Start — end", ["Format"] = "Format", ["Zwycięzca"] = "Winner",
        ["Wróć"] = "Back", ["Trwa"] = "In progress", ["Oczekuje"] = "Waiting", ["Punkty"] = "Points",
        ["Silnik przeciwnika"] = "Opponent engine", ["Poziom trudności"] = "Difficulty", ["Kolor gracza"] = "Player color",
        ["Rozpocznij partię"] = "Start game", ["Poddaj partię"] = "Resign", ["Dziennik ruchów"] = "Move log",
        ["Brak wyników turnieju"] = "No tournament results", ["Brak zakończonych meczów"] = "No completed matches",
        ["Brak nadchodzących meczów"] = "No upcoming matches", ["Brak statystyk turnieju"] = "No tournament statistics",
        ["Brak meczów w turnieju"] = "No matches in this tournament", ["Białe"] = "White", ["Czarne"] = "Black",
        ["Czas"] = "Time", ["Dane lokalne"] = "Local data", ["Dodaj plik EXE"] = "Add EXE file",
        ["Informacje o partii"] = "Game information", ["Instalacja silników"] = "Engine installation",
        ["Kolor"] = "Color", ["Kolory"] = "Colors", ["Konfiguracja pojedynku"] = "Match configuration",
        ["Kontrola czasu"] = "Time control", ["Następny ruch"] = "Next move", ["Poprzedni ruch"] = "Previous move",
        ["Ocena pozycji"] = "Position evaluation", ["Poziom"] = "Level", ["Siła silnika"] = "Engine strength",
        ["Turniej silników"] = "Engine tournament", ["Ustawienia partii"] = "Game settings",
        ["Usuń"] = "Delete", ["Usuń z konfiguracji"] = "Remove from configuration",
        ["Usuń zapisaną partię"] = "Delete saved game", ["Wynik"] = "Result", ["Zakończ"] = "End",
        ["Zakończ partię"] = "End game", ["Zapisz powtórki meczów lokalnie"] = "Save match replays locally",
        ["Zdobyte:"] = "Captured:", ["Wykryte silniki"] = "Detected engines", ["Silniki UCI"] = "UCI engines",
        ["Testuj UCI"] = "Test UCI", ["PGN i analiza zapisane lokalnie"] = "PGN and analysis saved locally",
        ["Lokalne procesy — bez połączenia z internetem"] = "Local processes — no internet connection",
        ["Lokalnie zapisane turnieje — wybierz, aby otworzyć pełne dane"] = "Locally saved tournaments — select one to open full details",
        ["Liga dwurundowa · każdy z każdym, mecz i rewanż"] = "Double round-robin · every engine plays each opponent twice",
        ["Promocję wybierzesz w oknie, które pojawi się po wejściu pionka na ostatnią linię."] = "Choose promotion in the dialog shown when a pawn reaches the last rank.",
        ["Obsługiwane przez wspólny standard UCI"] = "Supported through the common UCI standard",
        ["Konfiguracja silników, PGN-y i turnieje są przechowywane tutaj:"] = "Engine configuration, PGNs and tournaments are stored here:",
        ["Usunięcie silnika w aplikacji usuwa tylko wpis konfiguracyjny — plik wykonywalny pozostaje na dysku."] = "Removing an engine from the app deletes only its configuration entry — the executable remains on disk.",
        ["z perspektywy białych"] = "from White's perspective", ["Silnik A"] = "Engine A", ["Silnik B"] = "Engine B",
        ["Pobierz silnik z oficjalnej strony projektu, rozpakuj go, a następnie wybierz właściwy plik .exe przyciskiem „Dodaj plik EXE”. Pliki możesz trzymać w dowolnym miejscu lub w przygotowanych katalogach aplikacji:"] = "Download an engine from its official website, extract it, then select the correct .exe file with the ‘Add EXE file’ button. You can keep engine files anywhere or in the app directories listed below:",
        ["Lc0: umieść plik sieci .pb.gz w katalogu silnika albo skonfiguruj ścieżkę weights zgodnie z wydaniem Lc0. Jeśli sieć nie zostanie znaleziona, test UCI pokaże komunikat diagnostyczny."] = "Lc0: place the .pb.gz network file in the engine directory or configure the weights path for your Lc0 release. If the network cannot be found, the UCI test will display a diagnostic message."
    };

    private void ApplyLanguage()
    {
        var english = Hub.IsEnglish;
        TranslateTree(ShellNavigation, english);
    }

    private static void TranslateTree(DependencyObject root, bool english)
    {
        string Translate(string value)
        {
            if (english && PolishToEnglish.TryGetValue(value, out var translated)) return translated;
            if (!english)
            {
                var pair = PolishToEnglish.FirstOrDefault(item => item.Value == value);
                if (!string.IsNullOrEmpty(pair.Key)) return pair.Key;
            }
            return value;
        }

        switch (root)
        {
            case TextBlock text when text.GetBindingExpression(TextBlock.TextProperty) is null &&
                                     !string.IsNullOrWhiteSpace(text.Text):
                text.Text = Translate(text.Text);
                break;
            case NavigationViewItem navigation when navigation.GetBindingExpression(ContentControl.ContentProperty) is null &&
                                                    navigation.Content is string value:
                navigation.Content = Translate(value);
                break;
            case Button button when button.GetBindingExpression(ContentControl.ContentProperty) is null &&
                                    button.Content is string value:
                button.Content = Translate(value);
                break;
            case CheckBox checkBox when checkBox.GetBindingExpression(ContentControl.ContentProperty) is null &&
                                        checkBox.Content is string value:
                checkBox.Content = Translate(value);
                break;
            case MenuFlyoutItem menuItem when menuItem.GetBindingExpression(MenuFlyoutItem.TextProperty) is null:
                menuItem.Text = Translate(menuItem.Text);
                break;
            case TabViewItem tab when tab.GetBindingExpression(TabViewItem.HeaderProperty) is null &&
                                       tab.Header is string value:
                tab.Header = Translate(value);
                break;
            case SelectorBarItem selectorItem when !string.IsNullOrWhiteSpace(selectorItem.Text):
                selectorItem.Text = Translate(selectorItem.Text);
                break;
            case ComboBox combo when combo.GetBindingExpression(ComboBox.HeaderProperty) is null &&
                                     combo.Header is string value:
                combo.Header = Translate(value);
                break;
            case TextBox box:
                if (box.GetBindingExpression(TextBox.HeaderProperty) is null && box.Header is string header)
                    box.Header = Translate(header);
                if (box.GetBindingExpression(TextBox.PlaceholderTextProperty) is null && !string.IsNullOrEmpty(box.PlaceholderText))
                    box.PlaceholderText = Translate(box.PlaceholderText);
                break;
            case NumberBox number when number.GetBindingExpression(NumberBox.HeaderProperty) is null &&
                                       number.Header is string value:
                number.Header = Translate(value);
                break;
        }

        for (var index = 0; index < VisualTreeHelper.GetChildrenCount(root); index++)
            TranslateTree(VisualTreeHelper.GetChild(root, index), english);
    }

    private void MatchAdvantageTrack_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateMatchAdvantageBar();

    private void ObserverAdvantageTrack_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateObserverAdvantageBar();

    private void UpdateObserverAdvantageBar()
    {
        if (ObserverAdvantageTrack.ActualHeight <= 0) return;

        var purpleTarget = ObserverAdvantageTrack.ActualHeight * Hub.ObserverTopEvaluationShare;
        var whiteTarget = ObserverAdvantageTrack.ActualHeight - purpleTarget;
        if (!_observerAdvantageBarInitialized)
        {
            ObserverAdvantageFill.Height = purpleTarget;
            ObserverAdvantageWhiteFill.Height = whiteTarget;
            _observerAdvantageBarInitialized = true;
            return;
        }

        _observerAdvantageStoryboard?.Stop();
        _observerAdvantageStoryboard = new Storyboard();
        var duration = new Duration(TimeSpan.FromMilliseconds(420));
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        foreach (var item in new[]
                 {
                     (Element: ObserverAdvantageFill, Target: purpleTarget),
                     (Element: ObserverAdvantageWhiteFill, Target: whiteTarget)
                 })
        {
            var animation = new DoubleAnimation
            {
                From = item.Element.ActualHeight,
                To = item.Target,
                Duration = duration,
                EasingFunction = easing,
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(animation, item.Element);
            Storyboard.SetTargetProperty(animation, nameof(FrameworkElement.Height));
            _observerAdvantageStoryboard.Children.Add(animation);
        }
        _observerAdvantageStoryboard.Begin();
    }

    private void UpdateMatchAdvantageBar()
    {
        if (MatchAdvantageTrack.ActualHeight <= 0) return;

        var purpleTarget = MatchAdvantageTrack.ActualHeight * Hub.MatchTopEvaluationShare;
        var whiteTarget = MatchAdvantageTrack.ActualHeight - purpleTarget;
        if (!_matchAdvantageBarInitialized)
        {
            MatchAdvantageFill.Height = purpleTarget;
            MatchAdvantageWhiteFill.Height = whiteTarget;
            _matchAdvantageBarInitialized = true;
            return;
        }

        _matchAdvantageStoryboard?.Stop();
        _matchAdvantageStoryboard = new Storyboard();
        var duration = new Duration(TimeSpan.FromMilliseconds(420));
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        foreach (var item in new[]
                 {
                     (Element: MatchAdvantageFill, Target: purpleTarget),
                     (Element: MatchAdvantageWhiteFill, Target: whiteTarget)
                 })
        {
            var animation = new DoubleAnimation
            {
                From = item.Element.ActualHeight,
                To = item.Target,
                Duration = duration,
                EasingFunction = easing,
                EnableDependentAnimation = true
            };
            Storyboard.SetTarget(animation, item.Element);
            Storyboard.SetTargetProperty(animation, nameof(FrameworkElement.Height));
            _matchAdvantageStoryboard.Children.Add(animation);
        }
        _matchAdvantageStoryboard.Begin();
    }

    private void AdvantageTrack_SizeChanged(object sender, SizeChangedEventArgs e) => UpdateAdvantageBar();

    private void UpdateAdvantageBar()
    {
        if (AdvantageTrack.ActualHeight <= 0)
        {
            return;
        }

        var purpleTarget = AdvantageTrack.ActualHeight * ViewModel.TopEvaluationShare;
        var whiteTarget = AdvantageTrack.ActualHeight - purpleTarget;
        if (!_advantageBarInitialized)
        {
            AdvantageFill.Height = purpleTarget;
            AdvantageWhiteFill.Height = whiteTarget;
            _advantageBarInitialized = true;
            return;
        }

        var purpleStart = AdvantageFill.ActualHeight;
        var whiteStart = AdvantageWhiteFill.ActualHeight;
        _advantageStoryboard?.Stop();
        AdvantageFill.Height = purpleStart;
        AdvantageWhiteFill.Height = whiteStart;

        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(420));
        var purpleAnimation = new DoubleAnimation
        {
            From = purpleStart,
            To = purpleTarget,
            Duration = duration,
            EasingFunction = easing,
            EnableDependentAnimation = true
        };
        var whiteAnimation = new DoubleAnimation
        {
            From = whiteStart,
            To = whiteTarget,
            Duration = duration,
            EasingFunction = easing,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(purpleAnimation, AdvantageFill);
        Storyboard.SetTargetProperty(purpleAnimation, nameof(FrameworkElement.Height));
        Storyboard.SetTarget(whiteAnimation, AdvantageWhiteFill);
        Storyboard.SetTargetProperty(whiteAnimation, nameof(FrameworkElement.Height));
        _advantageStoryboard = new Storyboard();
        _advantageStoryboard.Children.Add(purpleAnimation);
        _advantageStoryboard.Children.Add(whiteAnimation);
        _advantageStoryboard.Begin();
    }

    private void UpdateStatusPulse()
    {
        if (!ViewModel.CanResign)
        {
            _statusPulse?.Stop();
            _statusPulse = null;
            GameStatusIndicator.Opacity = 1;
            if (GameStatusIndicator.RenderTransform is CompositeTransform restingTransform)
            {
                restingTransform.ScaleX = 1;
                restingTransform.ScaleY = 1;
            }
            return;
        }

        if (_statusPulse is not null)
        {
            return;
        }

        var opacity = new DoubleAnimation
        {
            From = 1,
            To = 0.2,
            Duration = TimeSpan.FromMilliseconds(850),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTarget(opacity, GameStatusIndicator);
        Storyboard.SetTargetProperty(opacity, "Opacity");

        var scaleX = new DoubleAnimation
        {
            From = 1,
            To = 1.35,
            Duration = TimeSpan.FromMilliseconds(850),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        var scaleY = new DoubleAnimation
        {
            From = 1,
            To = 1.35,
            Duration = TimeSpan.FromMilliseconds(850),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        if (GameStatusIndicator.RenderTransform is CompositeTransform pulseTransform)
        {
            Storyboard.SetTarget(scaleX, pulseTransform);
            Storyboard.SetTargetProperty(scaleX, nameof(CompositeTransform.ScaleX));
            Storyboard.SetTarget(scaleY, pulseTransform);
            Storyboard.SetTargetProperty(scaleY, nameof(CompositeTransform.ScaleY));
        }

        _statusPulse = new Storyboard();
        _statusPulse.Children.Add(opacity);
        _statusPulse.Children.Add(scaleX);
        _statusPulse.Children.Add(scaleY);
        _statusPulse.Begin();
    }

    private void UpdateMatchStatusPulse()
    {
        if (!Hub.IsMatchStatePulsing)
        {
            _matchStatusPulse?.Stop();
            _matchStatusPulse = null;
            MatchStatusIndicator.Opacity = 1;
            if (MatchStatusIndicator.RenderTransform is CompositeTransform restingTransform)
            {
                restingTransform.ScaleX = 1;
                restingTransform.ScaleY = 1;
            }
            return;
        }

        if (_matchStatusPulse is not null) return;

        var opacity = new DoubleAnimation
        {
            From = 1,
            To = 0.2,
            Duration = TimeSpan.FromMilliseconds(850),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTarget(opacity, MatchStatusIndicator);
        Storyboard.SetTargetProperty(opacity, "Opacity");

        var scaleX = new DoubleAnimation
        {
            From = 1,
            To = 1.35,
            Duration = TimeSpan.FromMilliseconds(850),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        var scaleY = new DoubleAnimation
        {
            From = 1,
            To = 1.35,
            Duration = TimeSpan.FromMilliseconds(850),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever
        };
        if (MatchStatusIndicator.RenderTransform is CompositeTransform pulseTransform)
        {
            Storyboard.SetTarget(scaleX, pulseTransform);
            Storyboard.SetTargetProperty(scaleX, nameof(CompositeTransform.ScaleX));
            Storyboard.SetTarget(scaleY, pulseTransform);
            Storyboard.SetTargetProperty(scaleY, nameof(CompositeTransform.ScaleY));
        }

        _matchStatusPulse = new Storyboard();
        _matchStatusPulse.Children.Add(opacity);
        _matchStatusPulse.Children.Add(scaleX);
        _matchStatusPulse.Children.Add(scaleY);
        _matchStatusPulse.Begin();
    }

    private void AnimateMove(Move move)
    {
        AnimatePiece(move.From, move.To);
        if (move.Flags.HasFlag(MoveFlags.CastleKingSide))
        {
            AnimatePiece(new Square(7, move.From.Rank), new Square(5, move.From.Rank));
        }
        else if (move.Flags.HasFlag(MoveFlags.CastleQueenSide))
        {
            AnimatePiece(new Square(0, move.From.Rank), new Square(3, move.From.Rank));
        }
    }

    private void AnimatePiece(Square from, Square to)
    {
        var sourceSquare = GetSquareElement(from);
        var targetSquare = GetSquareElement(to);
        var target = GetPieceElement(to);
        if (sourceSquare is null || targetSquare is null || target is null || string.IsNullOrEmpty(target.Text))
        {
            return;
        }

        var sourcePoint = sourceSquare.TransformToVisual(AnimationLayer)
            .TransformPoint(new Windows.Foundation.Point(0, 0));
        var targetPoint = targetSquare.TransformToVisual(AnimationLayer)
            .TransformPoint(new Windows.Foundation.Point(0, 0));
        var movingPiece = new Grid
        {
            Width = 80,
            Height = 80,
            Children =
            {
                new TextBlock
                {
                    Text = target.Text,
                    Foreground = target.Foreground,
                    FontFamily = target.FontFamily,
                    FontSize = target.FontSize,
                    HorizontalAlignment = HorizontalAlignment.Center,
                    VerticalAlignment = VerticalAlignment.Center
                }
            }
        };
        var transform = new CompositeTransform();
        movingPiece.RenderTransform = transform;
        Canvas.SetLeft(movingPiece, sourcePoint.X);
        Canvas.SetTop(movingPiece, sourcePoint.Y);
        AnimationLayer.Children.Add(movingPiece);
        target.Opacity = 0;

        var crossedSquares = Math.Max(
            Math.Abs(to.File - from.File),
            Math.Abs(to.Rank - from.Rank));
        var easing = new CubicEase { EasingMode = EasingMode.EaseInOut };
        var duration = new Duration(TimeSpan.FromMilliseconds(Math.Max(200, 83 * crossedSquares)));
        var storyboard = new Storyboard();
        AddAnimation(storyboard, transform, nameof(CompositeTransform.TranslateX), targetPoint.X - sourcePoint.X, duration, easing);
        AddAnimation(storyboard, transform, nameof(CompositeTransform.TranslateY), targetPoint.Y - sourcePoint.Y, duration, easing);
        storyboard.Completed += (_, _) =>
        {
            target.Opacity = 1;
            AnimationLayer.Children.Remove(movingPiece);
        };
        storyboard.Begin();
    }

    private static void AddAnimation(
        Storyboard storyboard,
        DependencyObject target,
        string property,
        double to,
        Duration duration,
        EasingFunctionBase easing)
    {
        var animation = new DoubleAnimation
        {
            To = to,
            Duration = duration,
            EasingFunction = easing,
            EnableDependentAnimation = true
        };
        Storyboard.SetTarget(animation, target);
        Storyboard.SetTargetProperty(animation, property);
        storyboard.Children.Add(animation);
    }

    private TextBlock? GetPieceElement(Square square)
    {
        if (GetSquareElement(square) is not Button { Content: Grid content })
        {
            return null;
        }

        return content.Children
            .OfType<TextBlock>()
            .FirstOrDefault(text => string.Equals(text.Tag as string, "PieceGlyph", StringComparison.Ordinal));
    }

    private Button? GetSquareElement(Square square)
    {
        var index = ViewModel.Squares
            .Select((item, index) => (item, index))
            .FirstOrDefault(pair => pair.item.Square == square)
            .index;
        return BoardRepeater.TryGetElement(index) as Button;
    }

    private BoardSquareViewModel? GetSquare(object sender)
    {
        if (sender is not Button button)
        {
            return null;
        }

        if (button.DataContext is BoardSquareViewModel square)
        {
            return square;
        }

        var squareName = button.Tag as string;
        return ViewModel.Squares.FirstOrDefault(item =>
            string.Equals(item.SquareName, squareName, StringComparison.Ordinal));
    }
}
