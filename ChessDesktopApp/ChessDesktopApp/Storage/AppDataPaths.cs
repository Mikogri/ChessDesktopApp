namespace ChessDesktopApp.Storage;

public static class AppDataPaths
{
    public static string Root { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "FluentChess");
    public static string Data { get; } = Path.Combine(Root, "data");
    public static string Engines { get; } = Path.Combine(Root, "engines");
    public static string EngineConfigurationFile { get; } = Path.Combine(Data, "engines.json");
    public static string GamesFile { get; } = Path.Combine(Data, "engine-games.json");
    public static string GamesDirectory { get; } = Path.Combine(Data, "games");
    public static string TournamentsFile { get; } = Path.Combine(Data, "tournaments.json");

    public static void EnsureCreated()
    {
        Directory.CreateDirectory(Data);
        Directory.CreateDirectory(GamesDirectory);
        Directory.CreateDirectory(Engines);
        foreach (var folder in new[] { "lc0", "berserk", "obsidian", "caissa", "alexandria", "koivisto", "rubichess", "ethereal", "smallbrain" })
        {
            Directory.CreateDirectory(Path.Combine(Engines, folder));
        }
    }
}
