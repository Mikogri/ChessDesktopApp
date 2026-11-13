# Fluent Chess

Desktopowa aplikacja WinUI 3 do lokalnej gry człowiek kontra Stockfish. Interfejs
jest zoptymalizowany dla ciemnego motywu Windows 11 i obsługuje ruch przez kliknięcia
oraz przeciąganie.

## Struktura

- `Chess.Core` — niezależny od UI stan partii, FEN/UCI, legalne ruchy i reguły końca gry.
- `ChessDesktopApp/Engine` — asynchroniczny klient procesu Stockfish/UCI.
- `ChessDesktopApp/Services` — koordynacja tury człowieka i silnika.
- `ChessDesktopApp/ViewModels` — stan prezentacji i interakcje MVVM.
- `Chess.Core.Tests` — testy reguł i kontrola perft pozycji startowej.
- `Chess.App.Tests` — testy koordynacji partii z mockowanym silnikiem.

## Konfiguracja Stockfisha

1. Pobierz wersję Stockfisha odpowiednią dla procesora i Windows z oficjalnej strony: https://stockfishchess.org/download/ (dla większości współczesnych komputerów x64 właściwa jest wersja AVX2).
2. Skopiuj plik EXE do `ChessDesktopApp/ChessDesktopApp/Stockfish/stockfish.exe`.
3. Zamiast kopiowania możesz ustawić `CHESS_STOCKFISH_PATH` na pełną ścieżkę do EXE.

Plik silnika jest kopiowany do katalogu wynikowego podczas kompilacji. Nie jest
dołączony do repozytorium, ponieważ jego dystrybucja wymaga spełnienia warunków GPLv3.

## Kompilacja i testy

Wymagane są .NET 8, Windows App SDK i narzędzia WinUI 3/packaging z Visual Studio.

Testy infrastruktury i koordynacji aplikacji uruchamiaj dla platformy x64:

```powershell
dotnet test .\ChessDesktopApp\Chess.App.Tests\Chess.App.Tests.csproj -c Release -p:Platform=x64
```

Generowanie ruchów zaznaczonej figury filtruje kandydatów przed kopiowaniem
pozycji i kontrolą szacha. Pełna lista ruchów i lista dla pojedynczego pola
korzystają z tej samej walidacji; testy porównują je również dla roszady,
bicia w przelocie i promocji. Statystyki turnieju agregują głębokość i czas
analiz w jednym przebiegu, bez tworzenia dodatkowej listy analiz.

Zapis pojedynczych obiektów i list JSON korzysta ze wspólnej implementacji
z blokadą i podmianą pliku tymczasowego. Rezerwacja silników respektuje
anulowanie przed próbą zajęcia silnika, a wielokrotne zwolnienie tej samej
rezerwacji nie zwalnia rezerwacji innego meczu.

```powershell
dotnet test .\ChessDesktopApp\Chess.Core.Tests\Chess.Core.Tests.csproj -c Debug
dotnet build .\ChessDesktopApp\ChessDesktopApp\ChessDesktopApp.csproj -c Debug -p:Platform=x64
dotnet publish .\ChessDesktopApp\ChessDesktopApp\ChessDesktopApp.csproj -c Debug -r win-x64 --self-contained true -p:Platform=x64 -o .\artifacts\FluentChess
```

Samodzielną wersję uruchamiaj z `artifacts\FluentChess\ChessDesktopApp.exe`.
Nie uruchamiaj pliku z `bin\win-x64\publish`, ponieważ ten katalog może być używany
jako wewnętrzny katalog pośredni podczas tworzenia pakietu MSIX.

Projekt `ChessDesktopApp (Package)` tworzy pakiet MSIX i powinien być budowany przez
MSBuild zainstalowany wraz z Visual Studio albo bezpośrednio z poziomu Visual Studio.

## Sterowanie

Przed rozpoczęciem partii wybierz kolor i poziom trudności. Figurę można przesunąć
klikając ją, a następnie pole docelowe, albo używając drag & drop. Ustawienie
„Promocja pionka” określa figurę wybieraną po dotarciu pionka do ostatniej linii.
