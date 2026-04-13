using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Threading.Tasks;
using FluentAssertions;
using HungryGame;
using HungryTests.TestInfrastructure;
using Microsoft.AspNetCore.Mvc;
using NUnit.Framework;

namespace HungryTests;

[TestFixture]
public class ApiIntegrationTests
{
    private sealed record GameCreatedResponse(string Id, string Name);
    private sealed record PlayerResponse(string Name, int Id, int Score);

    private static async Task<GameCreatedResponse> CreateGameAsync(
        HttpClient client,
        string creatorToken = "creator-token",
        int rows = 20,
        int cols = 30,
        bool isTimed = false,
        int? timeLimitMinutes = null,
        string? adminToken = null,
        string name = "Arena")
    {
        var response = await client.PostAsJsonAsync("/games", new CreateGameRequest(
            name,
            rows,
            cols,
            creatorToken,
            isTimed,
            timeLimitMinutes,
            adminToken));

        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<GameCreatedResponse>())!;
    }

    private static async Task<string> LoginAsync(HttpClient client, string password = "swordfish")
    {
        var response = await client.PostAsJsonAsync("/admin/login", new AdminLoginRequest(password));
        response.EnsureSuccessStatusCode();
        return (await response.Content.ReadFromJsonAsync<string>())!;
    }

    [Test]
    public async Task PostGames_RejectsInvalidBoardSizesNamesAndTimedPayloads()
    {
        using var factory = new HungryGameWebApplicationFactory();
        using var client = factory.CreateClient();

        var invalidSize = await client.PostAsJsonAsync("/games", new CreateGameRequest(
            "Arena", 0, 30, "creator", false, null, null));
        var blankName = await client.PostAsJsonAsync("/games", new CreateGameRequest(
            "   ", 20, 30, "creator", false, null, null));
        var invalidTimed = await client.PostAsJsonAsync("/games", new CreateGameRequest(
            "Arena", 20, 30, "creator", true, 0, null));

        invalidSize.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalidSize.Content.ReadAsStringAsync()).Should().Contain("at least 1x1");

        blankName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await blankName.Content.ReadAsStringAsync()).Should().Contain("Game name is required");

        invalidTimed.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalidTimed.Content.ReadAsStringAsync()).Should().Contain("Timed games require a positive time limit");
    }

    [Test]
    public async Task PostGames_RequiresAdminTokenForOversizedBoards()
    {
        using var factory = new HungryGameWebApplicationFactory();
        using var client = factory.CreateClient();

        var userResponse = await client.PostAsJsonAsync("/games", new CreateGameRequest(
            "Big Arena", 101, 151, "creator", false, null, null));

        userResponse.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await userResponse.Content.ReadAsStringAsync()).Should().Contain("Board size capped");

        var adminToken = await LoginAsync(client);
        var adminResponse = await client.PostAsJsonAsync("/games", new CreateGameRequest(
            "Big Arena", 101, 151, "creator", false, null, adminToken));

        adminResponse.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task Join_AcceptsEitherSupportedNameParameter_AndMissingNameReturnsProblemDetails()
    {
        using var factory = new HungryGameWebApplicationFactory();
        using var client = factory.CreateClient();
        var game = await CreateGameAsync(client);

        var byUserName = await client.GetAsync($"/game/{game.Id}/join?userName=Alice");
        var byPlayerName = await client.GetAsync($"/game/{game.Id}/join?playerName=Bob");
        var missingName = await client.GetAsync($"/game/{game.Id}/join");

        byUserName.StatusCode.Should().Be(HttpStatusCode.OK);
        (await byUserName.Content.ReadFromJsonAsync<string>()).Should().NotBeNullOrWhiteSpace();

        byPlayerName.StatusCode.Should().Be(HttpStatusCode.OK);
        (await byPlayerName.Content.ReadFromJsonAsync<string>()).Should().NotBeNullOrWhiteSpace();

        missingName.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        var details = await missingName.Content.ReadFromJsonAsync<ProblemDetails>();
        details!.Title.Should().Be("Missing player name");
    }

    [Test]
    public async Task Move_ReturnsExpectedErrorsForInvalidDirectionUnknownGameAndUnknownPlayer()
    {
        using var factory = new HungryGameWebApplicationFactory();
        using var client = factory.CreateClient();
        var game = await CreateGameAsync(client);

        var invalidDirection = await client.GetAsync($"/game/{game.Id}/move/sideways?token=abc");
        var unknownGame = await client.GetAsync("/game/ZZZ/move/left?token=abc");
        var unknownPlayer = await client.GetAsync($"/game/{game.Id}/move/left?token=missing-token");

        invalidDirection.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await invalidDirection.Content.ReadAsStringAsync()).Should().Contain("Unknown direction");

        unknownGame.StatusCode.Should().Be(HttpStatusCode.NotFound);

        unknownPlayer.StatusCode.Should().Be(HttpStatusCode.NotFound);
        var details = await unknownPlayer.Content.ReadFromJsonAsync<ProblemDetails>();
        details!.Title.Should().Be("Player not found");
    }

    [Test]
    public async Task ManagementEndpoints_RequireAuthorization_AndAllowTheCreatorToken()
    {
        using var factory = new HungryGameWebApplicationFactory();
        using var client = factory.CreateClient();
        const string creatorToken = "creator-token";
        var game = await CreateGameAsync(client, creatorToken: creatorToken);

        (await client.GetAsync($"/game/{game.Id}/start")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync($"/game/{game.Id}/start?creatorToken={creatorToken}")).StatusCode.Should().Be(HttpStatusCode.OK);
        (await client.GetAsync($"/game/{game.Id}/reset")).StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.GetAsync($"/game/{game.Id}/reset?creatorToken={creatorToken}")).StatusCode.Should().Be(HttpStatusCode.OK);

        await client.GetAsync($"/game/{game.Id}/join?userName=Alice");
        var players = await client.GetFromJsonAsync<List<PlayerResponse>>($"/game/{game.Id}/players");
        var alice = players!.Single();

        (await client.PostAsJsonAsync($"/game/{game.Id}/admin/boot", new BootRequest(alice.Id, null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync($"/game/{game.Id}/admin/boot", new BootRequest(alice.Id, creatorToken, null)))
            .StatusCode.Should().Be(HttpStatusCode.OK);

        await client.GetAsync($"/game/{game.Id}/join?userName=Bob");
        (await client.PostAsJsonAsync($"/game/{game.Id}/admin/clear-players", new AuthRequest(null, null)))
            .StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        (await client.PostAsJsonAsync($"/game/{game.Id}/admin/clear-players", new AuthRequest(creatorToken, null)))
            .StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Test]
    public async Task AdminLoginAndLogout_ControlAdminOnlyBehavior()
    {
        using var factory = new HungryGameWebApplicationFactory();
        using var client = factory.CreateClient();

        var badLogin = await client.PostAsJsonAsync("/admin/login", new AdminLoginRequest("wrong-password"));
        badLogin.StatusCode.Should().Be(HttpStatusCode.Unauthorized);

        var adminToken = await LoginAsync(client);
        var bigGame = await client.PostAsJsonAsync("/games", new CreateGameRequest(
            "Admin Arena", 200, 200, "creator", false, null, adminToken));

        bigGame.StatusCode.Should().Be(HttpStatusCode.OK);

        var logout = await client.PostAsJsonAsync("/admin/logout", new AdminLogoutRequest(adminToken));
        logout.StatusCode.Should().Be(HttpStatusCode.OK);

        var afterLogout = await client.PostAsJsonAsync("/games", new CreateGameRequest(
            "Admin Arena", 200, 200, "creator", false, null, adminToken));

        afterLogout.StatusCode.Should().Be(HttpStatusCode.BadRequest);
        (await afterLogout.Content.ReadAsStringAsync()).Should().Contain("Board size capped");
    }
}
