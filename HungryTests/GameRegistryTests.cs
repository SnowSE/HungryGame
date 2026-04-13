using System;
using System.Collections.Generic;
using System.Linq;
using FluentAssertions;
using HungryGame;
using Microsoft.Extensions.Logging;
using Moq;
using NUnit.Framework;

namespace HungryTests;

[TestFixture]
public class GameRegistryTests
{
    private static GameRegistry MakeRegistry()
    {
        var logger = new Mock<ILogger<GameLogic>>();
        var random = new Mock<IRandomService>();
        random.Setup(r => r.Next(It.IsAny<int>())).Returns(0);
        return new GameRegistry(
            new ShortRandomIdStrategy(),
            logger.Object,
            random.Object,
            TimeProvider.System);
    }

    [Test]
    public void CreateGame_ReturnsInstanceWithExpectedMetadata()
    {
        var registry = MakeRegistry();
        var instance = registry.CreateGame("Test Game", "creator-token-abc", 20, 30);

        instance.Name.Should().Be("Test Game");
        instance.CreatorToken.Should().Be("creator-token-abc");
        instance.Id.Should().HaveLength(3);
        instance.CompletedAt.Should().BeNull();
        instance.Game.Should().NotBeNull();
    }

    [Test]
    public void CreateGame_WithInvalidDimensions_Throws()
    {
        var registry = MakeRegistry();

        var act = () => registry.CreateGame("Invalid", "creator-token-abc", 0, 30);

        act.Should().Throw<ArgumentOutOfRangeException>();
    }

    [Test]
    public void GetGame_ReturnsCreatedGame()
    {
        var registry = MakeRegistry();
        var created = registry.CreateGame("My Game", "tok", 10, 10);

        var found = registry.GetGame(created.Id);
        found.Should().NotBeNull();
        found!.Name.Should().Be("My Game");
    }

    [Test]
    public void GetGame_UnknownId_ReturnsNull()
    {
        var registry = MakeRegistry();
        registry.GetGame("ZZZ").Should().BeNull();
    }

    [Test]
    public void AllGames_ReturnsAllCreatedGames()
    {
        var registry = MakeRegistry();
        registry.CreateGame("A", "t1", 5, 5);
        registry.CreateGame("B", "t2", 5, 5);

        registry.AllGames().Should().HaveCount(2);
    }

    [Test]
    public void RemoveGame_RemovesFromRegistry()
    {
        var registry = MakeRegistry();
        var instance = registry.CreateGame("Temp", "t", 5, 5);
        registry.RemoveGame(instance.Id);

        registry.GetGame(instance.Id).Should().BeNull();
    }
}

[TestFixture]
public class ShortRandomIdStrategyTests
{
    private const string Charset = "ABCDEFGHJKMNPQRTUVWXYZ2346789";

    [Test]
    public void GenerateId_ProducesThreeCharacterString()
    {
        var strategy = new ShortRandomIdStrategy();
        var id = strategy.GenerateId(Enumerable.Empty<string>());
        id.Should().HaveLength(3);
    }

    [Test]
    public void GenerateId_UsesOnlyAllowedCharset()
    {
        const string allowed = "ABCDEFGHJKMNPQRTUVWXYZ2346789";
        var strategy = new ShortRandomIdStrategy();
        for (int i = 0; i < 100; i++)
        {
            var id = strategy.GenerateId(Enumerable.Empty<string>());
            id.Should().MatchRegex($"^[{allowed}]{{3}}$");
        }
    }

    [Test]
    public void GenerateId_GrowsToFourCharsWhenFewSlotsRemain()
    {
        var strategy = new ShortRandomIdStrategy();
        // 29^3 = 24389 total 3-char slots; seed 24290 valid 3-char IDs to leave only 99 slots
        var existing = Enumerable.Range(0, 24_290)
            .Select(i => new string(new[]
            {
                Charset[i % 29],
                Charset[(i / 29) % 29],
                Charset[(i / 841) % 29]
            }))
            .Distinct()
            .Take(24_290)
            .ToList();
        var id = strategy.GenerateId(existing);
        id.Length.Should().BeGreaterThan(3);
    }
}
