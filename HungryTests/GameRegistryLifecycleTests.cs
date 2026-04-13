using System.Collections.Generic;
using FluentAssertions;
using HungryGame;
using HungryTests.TestInfrastructure;
using Microsoft.Extensions.Logging.Abstractions;
using NUnit.Framework;

namespace HungryTests;

[TestFixture]
public class GameRegistryLifecycleTests
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

    [Test]
    public void CreateGame_SetsCompletedAt_WhenTheGameEnds()
    {
        var time = new ManualTimeProvider();
        var registry = new GameRegistry(
            new FixedIdStrategy("AAA"),
            NullLogger<GameLogic>.Instance,
            new SequenceRandomService(new[] { 0, 0 }),
            time);

        var instance = registry.CreateGame("Solo Game", "creator", 1, 2);
        var token = instance.Game.JoinPlayer("Solo");
        instance.Game.StartGame();

        instance.Game.Move(token, Direction.Right);

        instance.CompletedAt.Should().Be(time.GetUtcNow());
    }
}
