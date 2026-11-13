# Lokalne silniki UCI w Fluent Chess

Aplikacja komunikuje się ze wszystkimi silnikami przez ten sam standard UCI. Nie pobiera silników automatycznie i nie dołącza cudzych binariów. Po jednorazowym pobraniu silniki działają całkowicie offline.

## Gdzie trzymać pliki

Możesz wskazać plik `.exe` z dowolnego miejsca. Aplikacja przygotowuje również katalogi:

`%LOCALAPPDATA%\FluentChess\engines\<nazwa-silnika>\`

Konfiguracja, partie i turnieje są zapisywane w:

`%LOCALAPPDATA%\FluentChess\data\`

## Oficjalne źródła

- Lc0: https://lczero.org/play/download/ lub https://github.com/LeelaChessZero/lc0/releases
- Berserk: https://github.com/jhonnold/berserk/releases
- Obsidian: https://github.com/gab8192/Obsidian
- Caissa: https://github.com/Witek902/Caissa/releases
- Alexandria: https://github.com/PGG106/Alexandria/releases
- Koivisto: https://github.com/Luecx/Koivisto/releases
- RubiChess: https://github.com/Matthies/RubiChess/releases
- Ethereal Standard: https://github.com/AndyGrant/Ethereal
- Smallbrain: https://github.com/Disservin/Smallbrain/releases

Zawsze sprawdź licencję wybranego wydania przed jego dalszym rozpowszechnianiem.

## Dodawanie

1. Pobierz wydanie przeznaczone dla Windows i architektury procesora.
2. Rozpakuj cały pakiet — zachowaj sieci NNUE i inne pliki towarzyszące obok programu.
3. W aplikacji otwórz **Silniki / konfiguracja**.
4. Kliknij **Dodaj plik EXE** i wybierz właściwy program, np. `lc0.exe` albo `RubiChess.exe`.
5. Kliknij **Testuj UCI**. Status „Gotowy” potwierdza poprawny handshake.

## Lc0 i weights

Lc0 wymaga sieci neuronowej. Pobierz kompatybilny plik weights ze strony projektu/training server i umieść go w katalogu `lc0.exe` bez rozpakowywania, jeżeli instrukcja danego wydania tak zaleca. Niektóre paczki zawierają już domyślną sieć lub wymagają ustawienia opcji UCI `WeightsFile`.

## Pojedynek i turniej

- **Pojedynek silników:** wybierz Engine A, Engine B, kolory oraz kontrolę czasu, następnie kliknij **Start pojedynku**.
- **Turniej:** zaznacz konkretne silniki, kliknij **Utwórz drabinkę**, opcjonalnie **Losuj ponownie**, a potem **Start turnieju**.
- Zapisane PGN-y i wyniki pozostają dostępne po ponownym uruchomieniu aplikacji.
