using System;
using System.Threading.Tasks;
using FluentAssertions;
using HungryGame;
using HungryTests.TestInfrastructure;
using NUnit.Framework;

namespace HungryTests;

[TestFixture]
public class TimedGameTests
{
    private static (ManualTimeProvider Time, GameLogic Game, string Alice, string Bob) CreateTimedGame()
    {
        var time = new ManualTimeProvider();
        var random = new ConstantRandomService();
        var game = TestGameFactory.CreateGame(random, time);
        var alice = game.JoinPlayer("Alice");
        var bob = game.JoinPlayer("Bob");

        game.ConfigureGame(new NewGameInfo
        {
            NumRows = 3,
            NumColumns = 2,
            IsTimed = true,
            TimeLimitInMinutes = 1
        });

        game.StartGame();
        return (time, game, alice, bob);
    }

    [Test]
    public async Task TimedRound_WhenClockExpires_GoesGameOverThenStartsFreshRound()
    {
        var (time, game, alice, bob) = CreateTimedGame();
        var gameOverEvents = 0;
        game.GameOver += (_, _) => gameOverEvents++;

        game.TimeLimit.Should().Be(TimeSpan.FromMinutes(1));
        game.GameEndsOn.Should().Be(time.GetUtcNow() + TimeSpan.FromMinutes(1));

        game.Move(alice, Direction.Down);
        game.Move(bob, Direction.Left);

        time.Advance(TimeSpan.FromMinutes(1));
        await AsyncTestHelpers.FlushBackgroundWorkAsync();

        game.CurrentGameState.Should().Be(GameState.GameOver);
        gameOverEvents.Should().Be(1);

        time.Advance(TimeSpan.FromSeconds(5));
        await AsyncTestHelpers.WaitUntilAsync(() => game.CurrentGameState == GameState.Eating);

        game.CurrentGameState.Should().Be(GameState.Eating);
        game.IsGameOver.Should().BeFalse();
        game.PlayerCount.Should().Be(2);
        game.ScoreHistory.Should().HaveCount(2);
        game.ScoreHistory.Should().OnlyContain(s => s.Score == 0 && s.Phase == GameState.Eating);
        game.BattleStartedAt.Should().BeNull();
        game.GameEndsOn.Should().Be(time.GetUtcNow() + TimeSpan.FromMinutes(1));
    }

    [Test]
    public async Task ResetGame_InvalidatesPendingTimedCallbacks()
    {
        var (time, game, _, _) = CreateTimedGame();

        game.ResetGame();

        time.Advance(TimeSpan.FromMinutes(2));
        await AsyncTestHelpers.FlushBackgroundWorkAsync();

        game.CurrentGameState.Should().Be(GameState.Joining);
        game.GameEndsOn.Should().BeNull();
        game.GetBoardState().Should().BeEmpty();
    }
}
