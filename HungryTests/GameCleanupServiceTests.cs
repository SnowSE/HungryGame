using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using HungryGame;
using HungryTests.TestInfrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace HungryTests;

[TestFixture]
public class GameCleanupServiceTests
{
    private sealed class FixedIdStrategy : IGameIdStrategy
    {
        private readonly Queue<string> _ids;

        public FixedIdStrategy(params string[] ids)
        {
            _ids = new Queue<string>(ids);
        }

        public string GenerateId(IEnumerable<string> existingIds) => _ids.Dequeue();
    }

    private sealed class TestableGameCleanupService : GameCleanupService
    {
        public TestableGameCleanupService(GameRegistry registry, TimeProvider timeProvider)
            : base(registry, NullLogger<GameCleanupService>.Instance, timeProvider)
        {
        }

        public Task ExecuteForTestAsync(CancellationToken cancellationToken) => ExecuteAsync(cancellationToken);
    }

    private static GameRegistry CreateRegistry(TimeProvider timeProvider)
    {
        return new GameRegistry(
            new FixedIdStrategy("AAA", "BBB", "CCC"),
            NullLogger<GameLogic>.Instance,
            new ConstantRandomService(),
            timeProvider);
    }

    [Test]
    public async Task ExecuteAsync_RemovesOnlyCompletedGamesOlderThanRetention()
    {
        var time = new ManualTimeProvider();
        var registry = CreateRegistry(time);
        var oldGame = registry.CreateGame("old", "creator", 2, 2);
        var recentGame = registry.CreateGame("recent", "creator", 2, 2);
        var activeGame = registry.CreateGame("active", "creator", 2, 2);

        oldGame.CompletedAt = time.GetUtcNow() - TimeSpan.FromDays(31);
        recentGame.CompletedAt = time.GetUtcNow() - TimeSpan.FromDays(29);
        activeGame.CompletedAt = null;

        using var cts = new CancellationTokenSource();
        var service = new TestableGameCleanupService(registry, time);
        var runTask = service.ExecuteForTestAsync(cts.Token);

        time.Advance(TimeSpan.FromHours(1));
        await AsyncTestHelpers.FlushBackgroundWorkAsync();

        registry.GetGame(oldGame.Id).Should().BeNull();
        registry.GetGame(recentGame.Id).Should().NotBeNull();
        registry.GetGame(activeGame.Id).Should().NotBeNull();

        cts.Cancel();
        await runTask;
    }

    [Test]
    public async Task ExecuteAsync_StopsCleanlyWhenCancelled()
    {
        var time = new ManualTimeProvider();
        var registry = CreateRegistry(time);
        var service = new TestableGameCleanupService(registry, time);

        using var cts = new CancellationTokenSource();
        var runTask = service.ExecuteForTestAsync(cts.Token);
        cts.Cancel();

        await runTask;
    }
}
