using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using HungryGame;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace HungryTests;

[TestFixture]
public class ScoreHistoryTests
{
    // ── shared helpers ─────────────────────────────────────────────────────

    private static GameLogic MakeGame()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["SECRET_CODE"]).Returns("secret");
        var logger = new Mock<ILogger<GameLogic>>();
        var random = new Mock<IRandomService>();
        random.Setup(r => r.Next(It.IsAny<int>())).Returns(0);
        return new GameLogic(config.Object, logger.Object, random.Object);
    }

    // ── Task 1: initial state ───────────────────────────────────────────────

    [Test]
    public void ScoreHistory_InitiallyEmpty()
    {
        var game = MakeGame();
        game.ScoreHistory.Should().BeEmpty();
    }

    [Test]
    public void BattleStartedAt_InitiallyNull()
    {
        var game = MakeGame();
        game.BattleStartedAt.Should().BeNull();
    }

    [Test]
    public void PlayerEliminationTimes_InitiallyEmpty()
    {
        var game = MakeGame();
        game.PlayerEliminationTimes.Should().BeEmpty();
    }

    // ── shared game factory with two players on a 3×2 board ───────────────────
    // Player1 (Alice) placed at (0,0), Player2 (Bob) placed at (2,1).
    // Pills fill all other cells: values 1,2,3,4 in order of eating.
    private static (GameLogic game, string token1, string token2) MakeTwoPlayerGame()
    {
        var config = new Mock<IConfiguration>();
        config.Setup(c => c["SECRET_CODE"]).Returns("secret");
        var logger = new Mock<ILogger<GameLogic>>();
        var random = new Mock<IRandomService>();

        // Player placement calls: Next(3) for row, Next(2) for col, alternating per player.
        // Alice: row=0, col=0  →  (0,0)
        // Bob:   row=2, col=1  →  (2,1)
        var rowValues = new Queue<int>(new[] { 0, 2 });
        var colValues = new Queue<int>(new[] { 0, 1 });
        random.Setup(r => r.Next(3)).Returns(() => rowValues.Dequeue());
        random.Setup(r => r.Next(2)).Returns(() => colValues.Dequeue());

        var game = new GameLogic(config.Object, logger.Object, random.Object);
        var token1 = game.JoinPlayer("Alice");
        var token2 = game.JoinPlayer("Bob");
        game.StartGame(new NewGameInfo
        {
            NumRows = 3,
            NumColumns = 2,
            SecretCode = "secret",
            CellIcon = "💊",
            UseCustomEmoji = false,
        });
        return (game, token1, token2);
    }

    [Test]
    public void AfterGameStart_ScoreHistory_HasOneEntryPerPlayer_AllAtScoreZero()
    {
        var (game, _, _) = MakeTwoPlayerGame();

        game.ScoreHistory.Should().HaveCount(2);
        game.ScoreHistory.Should().AllSatisfy(s =>
        {
            s.Score.Should().Be(0);
            s.ElapsedSeconds.Should().Be(0.0);
            s.Phase.Should().Be(GameState.Eating);
        });
    }

    [Test]
    public void AfterEatingAPill_ScoreHistory_GrowsWithPositiveScore()
    {
        var (game, token1, _) = MakeTwoPlayerGame();
        var initialCount = game.ScoreHistory.Count; // 2

        // Alice is at (0,0). Move Down → (1,0): pill cell.
        game.Move(token1, Direction.Down);

        game.ScoreHistory.Count.Should().BeGreaterThan(initialCount);
        var aliceEntries = game.ScoreHistory.Where(s => s.PlayerName == "Alice").ToList();
        aliceEntries.Last().Score.Should().BeGreaterThan(0);
        aliceEntries.Last().Phase.Should().Be(GameState.Eating);
    }

    [Test]
    public void AfterAllPillsEaten_BattleStartedAt_IsSet()
    {
        var (game, token1, token2) = MakeTwoPlayerGame();

        // Eat all 4 pills on the 3×2 board:
        game.Move(token1, Direction.Down);   // Alice (0,0)→(1,0) eats pill
        game.Move(token1, Direction.Right);  // Alice (1,0)→(1,1) eats pill
        game.Move(token2, Direction.Left);   // Bob   (2,1)→(2,0) eats pill
        game.Move(token1, Direction.Up);     // Alice (1,1)→(0,1) eats pill — remainingPills=0 → Battle!

        game.BattleStartedAt.Should().NotBeNull();
        game.BattleStartedAt!.Value.Should().BeGreaterThan(0);
        game.CurrentGameState.Should().Be(GameState.Battle);
    }

    [Test]
    public void AfterAttackElimination_PlayerEliminationTimes_ContainsLoser()
    {
        var (game, token1, token2) = MakeTwoPlayerGame();

        // Eating phase — eat all 4 pills:
        game.Move(token1, Direction.Down);   // Alice (0,0)→(1,0)
        game.Move(token1, Direction.Right);  // Alice (1,0)→(1,1)
        game.Move(token2, Direction.Left);   // Bob   (2,1)→(2,0)
        game.Move(token1, Direction.Up);     // Alice (1,1)→(0,1) → Battle!

        // Battle phase — maneuver to attack:
        game.Move(token1, Direction.Down);   // Alice (0,1)→(1,1)
        game.Move(token2, Direction.Up);     // Bob   (2,0)→(1,0)
        game.Move(token1, Direction.Left);   // Alice (1,1)→(1,0) ATTACKS Bob! Bob score→0 → GameOver!

        // Bob joined second → Id=2. Alice joined first → Id=1.
        game.PlayerEliminationTimes.Should().ContainKey(2); // Bob's Id
        game.PlayerEliminationTimes.Should().NotContainKey(1); // Alice survived
        game.IsGameOver.Should().BeTrue();
    }

    [Test]
    public void AfterResetGame_ScoreHistoryFields_AreCleared()
    {
        // Play a full game to game-over
        var (game, token1, token2) = MakeTwoPlayerGame();
        game.Move(token1, Direction.Down);
        game.Move(token1, Direction.Right);
        game.Move(token2, Direction.Left);
        game.Move(token1, Direction.Up);   // → Battle
        game.Move(token1, Direction.Down);
        game.Move(token2, Direction.Up);
        game.Move(token1, Direction.Left); // → GameOver

        game.ScoreHistory.Count.Should().BeGreaterThan(2, "history should exist before reset");
        game.PlayerEliminationTimes.Should().NotBeEmpty("elimination times should exist before reset");
        game.BattleStartedAt.Should().NotBeNull("battle should have started");

        // Now reset — this calls resetGame() which should clear the fields
        game.ResetGame("secret");

        game.ScoreHistory.Should().BeEmpty();
        game.BattleStartedAt.Should().BeNull();
        game.PlayerEliminationTimes.Should().BeEmpty();
    }

    [Test]
    public void GetPlayersByGameOverRank_WinnerFirst_ThenEliminatedByTime()
    {
        var (game, token1, token2) = MakeTwoPlayerGame();

        game.Move(token1, Direction.Down);
        game.Move(token1, Direction.Right);
        game.Move(token2, Direction.Left);
        game.Move(token1, Direction.Up);   // → Battle
        game.Move(token1, Direction.Down);
        game.Move(token2, Direction.Up);
        game.Move(token1, Direction.Left); // → Alice attacks Bob → GameOver

        var ranked = game.GetPlayersByGameOverRank().ToList();
        ranked.Should().HaveCount(2);
        ranked[0].Name.Should().Be("Alice"); // winner (score > 0)
        ranked[1].Name.Should().Be("Bob");   // eliminated
    }
}
