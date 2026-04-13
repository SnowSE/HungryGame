using System.Linq;
using Bunit;
using FluentAssertions;
using HungryGame;
using HungryGame.Shared;
using HungryTests.TestInfrastructure;
using Microsoft.AspNetCore.Components;
using Microsoft.Extensions.DependencyInjection;
using NUnit.Framework;

namespace HungryTests;

[TestFixture]
public class AddPlayerComponentTests
{
    [Test]
    public void SubmittingJoinForm_AfterGameHasStartedWithFreeSpace_JoinsThePlayer()
    {
        using var context = new Bunit.TestContext();
        context.JSInterop.Mode = JSRuntimeMode.Loose;
        context.Services.AddLogging();

        var random = new SequenceRandomService(new[] { 0, 0, 1, 1 });
        var game = TestGameFactory.CreateGame(random);
        game.JoinPlayer("Existing");
        game.ConfigureGame(new NewGameInfo { NumRows = 2, NumColumns = 2 });
        game.StartGame();

        var cut = context.RenderComponent<CascadingValue<GameLogic>>(parameters => parameters
            .Add(p => p.Value, game)
            .AddChildContent<AddPlayer>());

        cut.Find("input").Change("Late Joiner");
        cut.Find("form").Submit();

        cut.Markup.Should().Contain("Use arrow buttons or keyboard arrows to move");
        cut.Markup.Should().NotContain("Too late");
        game.PlayerCount.Should().Be(2);
        game.GetPlayersByScoreDescending().Select(p => p.Name).Should().Contain("Late Joiner");
    }
}
