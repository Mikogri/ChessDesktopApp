using ChessDesktopApp.Models;
using ChessDesktopApp.Services;
using Xunit;

namespace Chess.App.Tests;

public sealed class TournamentManagerTests
{
    [Fact]
    public void CompletedTournament_WithSharedTopScore_DisplaysNoSingleWinner()
    {
        var firstId = Guid.NewGuid();
        var secondId = Guid.NewGuid();
        var tournament = new EngineTournament
        {
            Status = TournamentStatus.Completed,
            ParticipantIds = [firstId, secondId],
            ParticipantNames = ["Engine A", "Engine B"],
            Matches =
            [
                new TournamentMatch
                {
                    EngineAId = firstId,
                    EngineBId = secondId,
                    Status = TournamentMatchStatus.Completed,
                    Result = "1/2-1/2"
                },
                new TournamentMatch
                {
                    EngineAId = secondId,
                    EngineBId = firstId,
                    Status = TournamentMatchStatus.Completed,
                    Result = "1/2-1/2"
                }
            ],
            WinnerName = "Engine A"
        };

        Assert.Equal("--", tournament.DisplayWinnerName);
    }

    [Theory]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void CreateDraft_BuildsDoubleRoundRobinWithReversedColors(int engineCount)
    {
        var engines = Enumerable.Range(1, engineCount)
            .Select(index => new EngineDefinition { Id = Guid.NewGuid(), Name = $"Engine {index}" })
            .ToList();
        var manager = new TournamentManager(null!, null!, null!, null!);

        var tournament = manager.CreateDraft(engines, 60, 0);

        Assert.Equal(engineCount * (engineCount - 1), tournament.Matches.Count);
        foreach (var pair in engines.SelectMany((first, index) => engines.Skip(index + 1).Select(second => (first, second))))
        {
            var games = tournament.Matches.Where(match =>
                match.EngineAId == pair.first.Id && match.EngineBId == pair.second.Id ||
                match.EngineAId == pair.second.Id && match.EngineBId == pair.first.Id).ToList();
            Assert.Equal(2, games.Count);
            Assert.Contains(games, match => match.EngineAId == pair.first.Id);
            Assert.Contains(games, match => match.EngineAId == pair.second.Id);
        }
    }
}
