using ChessDesktopApp.Engine;
using ChessDesktopApp.Services;
using Xunit;

namespace Chess.App.Tests;

public sealed class EngineUsageCoordinatorTests
{
    [Fact]
    public async Task AcquireAsync_CancelledRequestDoesNotReserveAvailableEngine()
    {
        var coordinator = new EngineUsageCoordinator();
        var engineId = Guid.NewGuid();
        using var cancellation = new CancellationTokenSource();
        cancellation.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            coordinator.AcquireAsync([engineId], false, cancellation.Token));

        Assert.False(coordinator.IsBusy(engineId));
    }

    [Fact]
    public async Task DisposeAsync_RepeatedDisposalDoesNotReleaseAnotherLease()
    {
        var coordinator = new EngineUsageCoordinator();
        var engineId = Guid.NewGuid();
        var first = await coordinator.AcquireAsync([engineId], false);
        await first.DisposeAsync();
        await using var second = await coordinator.AcquireAsync([engineId], false);

        await first.DisposeAsync();

        Assert.True(coordinator.IsBusy(engineId));
    }

    [Fact]
    public async Task AcquireAsync_RejectsNonWaitingMatchWhenEngineIsBusy()
    {
        var coordinator = new EngineUsageCoordinator();
        var engineId = Guid.NewGuid();
        await using var first = await coordinator.AcquireAsync([engineId], false);

        await Assert.ThrowsAsync<ChessEngineException>(() =>
            coordinator.AcquireAsync([engineId], false));
    }

    [Fact]
    public async Task AcquireAsync_WaitingMatchStartsAfterEngineIsReleased()
    {
        var coordinator = new EngineUsageCoordinator();
        var engineId = Guid.NewGuid();
        var first = await coordinator.AcquireAsync([engineId], false);
        var waiting = coordinator.AcquireAsync([engineId], true);

        Assert.False(waiting.IsCompleted);
        await first.DisposeAsync();
        await using var second = await waiting.WaitAsync(TimeSpan.FromSeconds(1));
        Assert.True(coordinator.IsBusy(engineId));
    }
}
