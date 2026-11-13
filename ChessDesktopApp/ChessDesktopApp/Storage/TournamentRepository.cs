using ChessDesktopApp.Models;

namespace ChessDesktopApp.Storage;

public sealed class TournamentRepository(JsonFileStore store)
{
    public Task<List<EngineTournament>> GetAllAsync(CancellationToken cancellationToken = default) =>
        store.LoadListAsync<EngineTournament>(AppDataPaths.TournamentsFile, cancellationToken);

    public async Task SaveAsync(EngineTournament tournament, CancellationToken cancellationToken = default)
    {
        var tournaments = await GetAllAsync(cancellationToken);
        var index = tournaments.FindIndex(item => item.Id == tournament.Id);
        if (index >= 0) tournaments[index] = tournament;
        else tournaments.Insert(0, tournament);
        await store.SaveListAsync(AppDataPaths.TournamentsFile, tournaments, cancellationToken);
    }

    public async Task DeleteAsync(Guid tournamentId, CancellationToken cancellationToken = default)
    {
        var tournaments = await GetAllAsync(cancellationToken);
        tournaments.RemoveAll(item => item.Id == tournamentId);
        await store.SaveListAsync(AppDataPaths.TournamentsFile, tournaments, cancellationToken);
    }
}
