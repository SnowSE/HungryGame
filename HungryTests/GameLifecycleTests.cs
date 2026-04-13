using System;
using System.Linq;
using FluentAssertions;
using HungryGame;
using HungryTests.TestInfrastructure;
using NUnit.Framework;

namespace HungryTests;

[TestFixture]
public class GameLifecycleTests
{
    private static (GameLogic Game, string AliceToken, string BobToken) CreateTwoPlayerGame()
    {
        var random = new ConstantRandomService();
        var game = TestGameFactory.CreateGame(random);
        var alice = game.JoinPlayer("Alice");
        var bob = game.JoinPlayer("Bob");

        game.ConfigureGame(new NewGameInfo { NumRows = 3, NumColumns = 2 });
        game.StartGame();

        return (game, alice, bob);
    }

    [Test]
    public void StartGame_RemovesPlayersWhoDidNotMoveInThePreviousRound()
    {
        var (game, aliceToken, _) = CreateTwoPlayerGame();

        game.Move(aliceToken, Direction.Down);
        game.ResetGame();
        game.StartGame();

        var players = game.GetPlayersByScoreDescending().Select(p => p.Name).ToList();
        players.Should().Equal("Alice");
        game.PlayerCount.Should().Be(1);
    }

    [Test]
    public void BootPlayer_RemovesTargetFromRosterAndBoard()
    {
        var (game, _, bobToken) = CreateTwoPlayerGame();
        var bobId = game.GetPlayersByScoreDescending().Single(p => p.Name == "Bob").Id;

        game.BootPlayer(bobId);

        game.PlayerCount.Should().Be(1);
        game.GetPlayersByScoreDescending().Select(p => p.Name).Should().Equal("Alice");
        game.GetBoardState().Any(c => c.OccupiedBy is not null && c.OccupiedBy.Name == "Bob").Should().BeFalse();

        Action act = () => game.Move(bobToken, Direction.Left);
        act.Should().Throw<Exception>()
            .Where(ex => ex.GetType().Name == "PlayerNotFoundException");
    }

    [Test]
    public void ClearAllPlayers_RemovesRosterAndRestoresEveryCell()
    {
        var (game, aliceToken, _) = CreateTwoPlayerGame();

        game.ClearAllPlayers();

        game.PlayerCount.Should().Be(0);
        game.GetPlayersByScoreDescending().Should().BeEmpty();
        game.GetBoardState().Should().OnlyContain(c => c.OccupiedBy == null && c.IsPillAvailable);

        Action act = () => game.Move(aliceToken, Direction.Left);
        act.Should().Throw<Exception>()
            .Where(ex => ex.GetType().Name == "PlayerNotFoundException");
    }
}
