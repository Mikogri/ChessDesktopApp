namespace ChessDesktopApp.Engine;

public static class StockfishPathResolver
{
    public static string Resolve()
    {
        var environmentPath = Environment.GetEnvironmentVariable("CHESS_STOCKFISH_PATH");
        if (!string.IsNullOrWhiteSpace(environmentPath) && File.Exists(environmentPath))
        {
            return Path.GetFullPath(environmentPath);
        }

        var engineDirectory = Path.Combine(AppContext.BaseDirectory, "Stockfish");
        if (Directory.Exists(engineDirectory))
        {
            var executable = Directory
                .EnumerateFiles(engineDirectory, "*.exe", SearchOption.TopDirectoryOnly)
                .FirstOrDefault(path => Path.GetFileName(path).Contains("stockfish", StringComparison.OrdinalIgnoreCase));
            if (executable is not null)
            {
                return executable;
            }
        }

        return Path.Combine(engineDirectory, "stockfish.exe");
    }
}
