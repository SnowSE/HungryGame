using System.Linq;
using FluentAssertions;
using HungryGame;
using HungryTests.TestInfrastructure;
using NUnit.Framework;

namespace HungryTests;

[TestFixture]
public class DeathMatchTests
{
    private static (GameLogic Game, string Alice, string Bob, string Carol) CreateThreePlayerBattleGame()
    {
        var random = new SequenceRandomService(new[] { 0, 0, 2, 0, 2, 1 });
        var game = TestGameFactory.CreateGame(random);
        var alice = game.JoinPlayer("Alice");
        var bob = game.JoinPlayer("Bob");
        var carol = game.JoinPlayer("Carol");

        game.ConfigureGame(new NewGameInfo { NumRows = 3, NumColumns = 2 });
        game.StartGame();

        return (game, alice, bob, carol);
    }

    [Test]
    public void EatingFinalPill_WithSingleActivePlayer_EndsGameWithoutEnteringBattle()
    {
        var game = TestGameFactory.CreateGame(new SequenceRandomService(new[] { 0, 0 }));
        var gameOverEvents = 0;
        game.GameOver += (_, _) => gameOverEvents++;

        var token = game.JoinPlayer("Solo");
        game.ConfigureGame(new NewGameInfo { NumRows = 1, NumColumns = 2 });
        game.StartGame();

        var result = game.Move(token, Direction.Right);

        result.Should().NotBeNull();
        result!.AteAPill.Should().BeTrue();
        game.CurrentGameState.Should().Be(GameState.GameOver);
        game.BattleStartedAt.Should().BeNull();
        gameOverEvents.Should().Be(1);
    }

    [Test]
    public void DroppedSpecialPill_IsAwardedExactlyOnceToTheNextCollector()
    {
        var (game, alice, bob, carol) = CreateThreePlayerBattleGame();

        game.Move(alice, Direction.Right); // Alice score 1
        game.Move(bob, Direction.Up);      // Bob score 2
        game.Move(carol, Direction.Up);    // Carol score 3 -> battle

        game.CurrentGameState.Should().Be(GameState.Battle);

        game.Move(carol, Direction.Left);  // Carol attacks Bob, Bob dies, special pill worth 1 drops at (1,0)
        game.Move(alice, Direction.Left);  // reposition

        var specialPickup = game.Move(alice, Direction.Down);
        specialPickup!.AteAPill.Should().BeTrue();
        game.GetPlayersByScoreDescending().Single(p => p.Name == "Alice").Score.Should().Be(2);

        game.Move(alice, Direction.Up);
        var secondVisit = game.Move(alice, Direction.Down);

        secondVisit!.AteAPill.Should().BeFalse();
        game.GetPlayersByScoreDescending().Single(p => p.Name == "Alice").Score.Should().Be(2);
    }

    [Test]
    public void GetPlayersByGameOverRank_OrdersWinnerThenLaterEliminations()
    {
        var (game, alice, bob, carol) = CreateThreePlayerBattleGame();

        game.Move(alice, Direction.Right); // Alice score 1
        game.Move(bob, Direction.Up);      // Bob score 2
        game.Move(carol, Direction.Up);    // Carol score 3 -> battle
        game.Move(carol, Direction.Left);  // Bob eliminated first
        game.Move(alice, Direction.Left);
        game.Move(alice, Direction.Down);  // Alice collects dropped special pill -> score 2
        game.Move(alice, Direction.Right); // Alice attacks Carol -> Carol eliminated second

        game.IsGameOver.Should().BeTrue();

        var ranked = game.GetPlayersByGameOverRank().Select(p => p.Name).ToList();
        ranked.Should().Equal("Alice", "Carol", "Bob");
    }
}
