# Multi-Game Lobby Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Replace the single global game with a registry of named games managed from a lobby page, with per-game creator ownership and a lobby at `/`.

**Architecture:** A new `GameRegistry` singleton replaces the `GameLogic` singleton; it holds a `ConcurrentDictionary<string, GameInstance>` where each `GameInstance` wraps an unchanged `GameLogic` plus metadata (id, name, creatorToken, timestamps). Authorization moves from inside `GameLogic` to the API route layer. Blazor game components switch from `@inject GameLogic` to `[CascadingParameter] GameLogic`, cascaded by the new `Game.razor` page.

**Tech Stack:** .NET 10, Blazor Server, NUnit + SpecFlow + FluentAssertions + Moq, minimal API endpoints, CSS flex/grid

---

## File Map

**New files:**
- `HungryGame/AdminTokenService.cs` — extracted admin token management
- `HungryGame/GameRegistry.cs` — `GameInstance`, `IGameIdStrategy`, `ShortRandomIdStrategy`, `GameRegistry`
- `HungryGame/GameCleanupService.cs` — `BackgroundService` removing 30-day-old completed games
- `HungryGame/Pages/Game.razor` — game page at `/game/{Id}`
- `HungryGame/Shared/CreateGameModal.razor` — modal form for creating a new game
- `HungryGame/Shared/StartGameButton.razor` — simple "Start Game" button (replaces full StartGame.razor on the game page)
- `HungryTests/GameRegistryTests.cs` — unit tests for registry and ID strategy

**Modified files:**
- `HungryGame/GameLogic.cs` — remove `IConfiguration`, admin token fields/methods, auth checks from `StartGame`/`ResetGame`/`BootPlayer`/`ClearAllPlayers`
- `HungryGame/NewGameInfo.cs` — remove `CellIcon`, `UseCustomEmoji`, `SecretCode`, `AdminToken`; add `CreatorToken`
- `HungryGame/Records.cs` — add `UserToken`, `IsCreator`, `CanManage` to `SharedStateClass`; remove emoji fields
- `HungryGame/Program.cs` — register new services, rewrite all routes
- `HungryGame/Pages/Index.razor` — replace contents with lobby UI (rename to Lobby in the page title; route stays `/`)
- `HungryGame/Shared/Board.razor` — switch to `[CascadingParameter] GameLogic`, remove emoji params from JS call
- `HungryGame/Shared/PlayerList.razor` — switch to `[CascadingParameter] GameLogic`, remove `AdminLogin` call
- `HungryGame/Shared/CurrentGameState.razor` — switch to `[CascadingParameter] GameLogic`
- `HungryGame/Shared/AddPlayer.razor` — switch to `[CascadingParameter] GameLogic`
- `HungryGame/Shared/ScoreHistoryChart.razor` — switch to `[CascadingParameter] GameLogic`
- `HungryGame/Shared/ResetGame.razor` — switch to `[CascadingParameter] GameLogic`, use `SharedState.CanManage`
- `HungryGame/Shared/StartGame.razor` — delete (replaced by `CreateGameModal.razor` + `StartGameButton.razor`)
- `HungryGame/wwwroot/css/site.css` — add lobby card styles
- `HungryTests/ScoreHistoryTests.cs` — remove `CellIcon`/`UseCustomEmoji`/`SecretCode` from `NewGameInfo` usage; remove `IConfiguration` mock from `MakeGame()`
- `HungryTests/StepDefinitions/GameInfoTestsStepDefinitions.cs` — same cleanup
- `clients/foolhearty/BasePlayerLogic.cs` — route all URLs through `/game/{GAME_ID}/...`
- `clients/massive/MassiveClient.cs` — route all URLs through `/game/{GAME_ID}/...`
- `HungryGame.AppHost/AppHost.cs` — add `gameId` parameter

**Deleted:**
- `clients/Viewer/` — entire project directory
- `HungryGame/Shared/StartGame.razor` — replaced

---

## Task 1: Delete the Viewer client

**Files:**
- Delete: `clients/Viewer/` (entire directory)
- Modify: `HungryGame.sln`

- [ ] **Step 1: Remove Viewer from solution**

```bash
dotnet sln HungryGame.sln remove clients/Viewer/Viewer.csproj
```

Expected output: `Project 'clients/Viewer/Viewer.csproj' removed from the solution.`

- [ ] **Step 2: Delete the directory**

```bash
rm -rf clients/Viewer
```

- [ ] **Step 3: Verify solution builds**

```bash
dotnet build HungryGame.sln
```

Expected: Build succeeded, 0 errors.

- [ ] **Step 4: Commit**

```bash
git add -A
git commit -m "feat: delete Viewer client - game page serves as spectator view"
```

---

## Task 2: Strip emoji from the model and GameLogic

**Files:**
- Modify: `HungryGame/NewGameInfo.cs`
- Modify: `HungryGame/Records.cs`
- Modify: `HungryGame/GameLogic.cs`
- Modify: `HungryGame/Shared/StartGame.razor` (delete it at end of task)
- Modify: `HungryTests/ScoreHistoryTests.cs`
- Modify: `HungryTests/StepDefinitions/GameInfoTestsStepDefinitions.cs`

- [ ] **Step 1: Remove emoji fields from NewGameInfo.cs**

Replace the entire file content:

```csharp
namespace HungryGame;
public class NewGameInfo
{
    public int NumRows { get; set; }
    public int NumColumns { get; set; }
    public bool IsTimed { get; set; }
    public int? TimeLimitInMinutes { get; set; }
}
```

- [ ] **Step 2: Remove emoji fields from SharedStateClass in Records.cs**

In `Records.cs`, replace `SharedStateClass`:

```csharp
public class SharedStateClass
{
    public bool IsAdmin { get; set; }
    public string? AdminPassword { get; set; }
    public string? UserToken { get; set; }
    public bool IsCreator { get; set; }
    public bool CanManage => IsAdmin || IsCreator;
}
```

- [ ] **Step 3: Remove emoji from GameLogic**

In `GameLogic.cs`, find `StartGame`. Remove the `SecretCode` and `AdminToken` authorization check and the `CellIcon`/`UseCustomEmoji` references. The full updated `StartGame` method:

```csharp
public void StartGame(NewGameInfo gameInfo)
{
    if (Interlocked.Read(ref gameStateValue) != 0)
        return;

    MaxRows = gameInfo.NumRows;
    MaxCols = gameInfo.NumColumns;
    LastGameInfo = gameInfo;

    if (gameInfo.IsTimed && gameInfo.TimeLimitInMinutes.HasValue)
    {
        var minutes = gameInfo.TimeLimitInMinutes.Value;
        TimeLimit = TimeSpan.FromMinutes(minutes);
        GameEndsOn = DateTime.Now.Add(TimeLimit.Value);
        gameTimer = new Timer(gameOverCallback, null, TimeLimit.Value, Timeout.InfiniteTimeSpan);
    }

    _scoreHistory.Clear();
    _battleStartedAt = null;
    _playerEliminationTimes.Clear();
    initializeGame();
}
```

Also update `ResetGame` to remove auth parameters:

```csharp
public void ResetGame()
{
    if (Interlocked.Read(ref gameStateValue) == 0)
        return;
    resetGame();
}
```

Update `BootPlayer` to remove admin token parameter:

```csharp
public void BootPlayer(int playerId)
{
    lock (lockForPlayersCellsPillValuesAndSpecialPontValues)
    {
        var player = players.FirstOrDefault(p => p.Id == playerId);
        if (player == null) return;

        if (player.Token != null && playerLocations.TryGetValue(player.Token, out var location))
        {
            playerLocations.Remove(player.Token);
            var cell = cells[location];
            cells[location] = cell with { OccupiedBy = null, IsPillAvailable = true };
            emptyCells.Add(location);
            remainingPills++;
            activePlayersCount--;
        }

        players.Remove(player);
        playersThatMovedThisGame.Remove(player);
        log.LogInformation("Booted player {playerName} (ID: {playerId})", player.Name, playerId);
    }

    raiseStateChange();
}
```

Update `ClearAllPlayers` to remove admin token parameter:

```csharp
public void ClearAllPlayers()
{
    lock (lockForPlayersCellsPillValuesAndSpecialPontValues)
    {
        foreach (var player in players)
        {
            if (player.Token != null && playerLocations.TryGetValue(player.Token, out var location))
            {
                var cell = cells[location];
                cells[location] = cell with { OccupiedBy = null, IsPillAvailable = true };
                emptyCells.Add(location);
                remainingPills++;
            }
        }

        players.Clear();
        playerLocations.Clear();
        playersThatMovedThisGame.Clear();
        activePlayersCount = 0;
        log.LogInformation("Cleared all players");
    }

    raiseStateChange();
}
```

Remove the admin token fields and `AdminLogin`, `AdminLogout`, `IsValidAdminToken` methods entirely from `GameLogic.cs`. Remove `IConfiguration` from the constructor and all usages:

```csharp
public GameLogic(ILogger<GameLogic> log, IRandomService random)
{
    this.log = log;
    this.random = random;
}
```

Remove `private readonly IConfiguration config;` and `private readonly HashSet<string> adminTokens = new();` fields.

- [ ] **Step 4: Fix ScoreHistoryTests.cs — remove IConfiguration mock and emoji from NewGameInfo**

In `ScoreHistoryTests.cs`, replace `MakeGame()`:

```csharp
private static GameLogic MakeGame()
{
    var logger = new Mock<ILogger<GameLogic>>();
    var random = new Mock<IRandomService>();
    random.Setup(r => r.Next(It.IsAny<int>())).Returns(0);
    return new GameLogic(logger.Object, random.Object);
}
```

In `MakeTwoPlayerGame()`, replace the `game.StartGame(...)` call:

```csharp
game.StartGame(new NewGameInfo { NumRows = 3, NumColumns = 2 });
```

Replace `game.ResetGame("secret")` with `game.ResetGame()`.

- [ ] **Step 5: Fix GameInfoTestsStepDefinitions.cs**

Replace `getGame()` — remove `IConfiguration` mock:

```csharp
private GameLogic getGame()
{
    if (context.TryGetValue(out GameLogic game) is false)
    {
        var loggerMock = new Mock<ILogger<GameLogic>>();
        var randomMock = new Mock<IRandomService>();
        randomMock.Setup(m => m.Next(It.IsAny<int>())).Returns(() =>
        {
            lastRandom++;
            if (lastRandom >= 2)
                lastRandom = 0;
            return lastRandom;
        });
        game = new GameLogic(loggerMock.Object, randomMock.Object);
        context.Set(game);
    }
    return game;
}
```

In `GivenTheGameStarts`, replace `NewGameInfo`:

```csharp
[Given(@"the game starts with (.*) rows, (.*) columns")]
public void GivenTheGameStarts(int numRows, int numColumns)
{
    var game = getGame();
    game.StartGame(new NewGameInfo { NumRows = numRows, NumColumns = numColumns });
}
```

In `ThenStartingAGameWithRowsColumnsGivesATooManyPlayersExeption_`, replace `NewGameInfo`:

```csharp
var newGameInfo = new NewGameInfo { NumRows = rows, NumColumns = cols };
game.StartGame(newGameInfo);
```

- [ ] **Step 6: Delete StartGame.razor (it will be replaced by CreateGameModal + StartGameButton)**

```bash
rm HungryGame/Shared/StartGame.razor
```

- [ ] **Step 7: Run tests**

```bash
dotnet test HungryTests
```

Expected: All tests pass.

- [ ] **Step 8: Commit**

```bash
git add -A
git commit -m "feat: remove emoji support and simplify GameLogic auth"
```

---

## Task 3: Add AdminTokenService

**Files:**
- Create: `HungryGame/AdminTokenService.cs`
- Modify: `HungryGame/Program.cs` (registration only — routes in Task 9)

- [ ] **Step 1: Create AdminTokenService.cs**

```csharp
namespace HungryGame;

public class AdminTokenService
{
    private readonly HashSet<string> _tokens = new();
    private readonly object _lock = new();
    private readonly IConfiguration _config;

    public AdminTokenService(IConfiguration config)
    {
        _config = config;
    }

    public string? Login(string password)
    {
        if (password != _config["SECRET_CODE"])
            return null;

        var token = Guid.NewGuid().ToString();
        lock (_lock) { _tokens.Add(token); }
        return token;
    }

    public void Logout(string token)
    {
        lock (_lock) { _tokens.Remove(token); }
    }

    public bool IsValid(string? token)
    {
        if (string.IsNullOrWhiteSpace(token)) return false;
        lock (_lock) { return _tokens.Contains(token); }
    }
}
```

- [ ] **Step 2: Register in Program.cs**

Find `builder.Services.AddSingleton<GameLogic>();` and replace it (temporarily keep it if other things depend on it — you'll remove it in Task 9). Add:

```csharp
builder.Services.AddSingleton<AdminTokenService>();
```

- [ ] **Step 3: Build to verify**

```bash
dotnet build HungryGame
```

Expected: Build succeeded.

- [ ] **Step 4: Commit**

```bash
git add HungryGame/AdminTokenService.cs HungryGame/Program.cs
git commit -m "feat: add AdminTokenService extracted from GameLogic"
```

---

## Task 4: Add GameInstance, IGameIdStrategy, ShortRandomIdStrategy, and GameRegistry

**Files:**
- Create: `HungryGame/GameRegistry.cs`
- Create: `HungryTests/GameRegistryTests.cs`

- [ ] **Step 1: Write failing tests**

Create `HungryTests/GameRegistryTests.cs`:

```csharp
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
            random.Object);
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
        // Fill up all but 99 of the 3-char slots
        const string allowed = "ABCDEFGHJKMNPQRTUVWXYZ2346789"; // 29 chars
        // 29^3 = 24389 total; need 24389 - 99 = 24290 existing
        // For test speed, just seed with a count that triggers the growth
        var existing = Enumerable.Range(0, 24_290).Select(i => i.ToString("X6")).ToList();
        var id = strategy.GenerateId(existing);
        id.Length.Should().BeGreaterThan(3);
    }
}
```

- [ ] **Step 2: Run tests to confirm they fail**

```bash
dotnet test HungryTests --filter "FullyQualifiedName~GameRegistryTests"
```

Expected: Compile error — `GameRegistry`, `ShortRandomIdStrategy` not found.

- [ ] **Step 3: Create GameRegistry.cs**

```csharp
namespace HungryGame;

public class GameInstance
{
    public string Id { get; init; } = "";
    public string Name { get; init; } = "";
    public string CreatorToken { get; init; } = "";
    public DateTime CreatedAt { get; init; }
    public DateTime? CompletedAt { get; set; }
    public GameLogic Game { get; init; } = null!;
}

public interface IGameIdStrategy
{
    string GenerateId(IEnumerable<string> existingIds);
}

public class ShortRandomIdStrategy : IGameIdStrategy
{
    // Visually unambiguous in Press Start 2P font: no 0,1,5,I,L,O,S
    private const string Charset = "ABCDEFGHJKMNPQRTUVWXYZ2346789";
    private const int GrowthThreshold = 100;
    private readonly Random _rng = new();

    public string GenerateId(IEnumerable<string> existingIds)
    {
        var existing = new HashSet<string>(existingIds);
        int length = 3;
        while (true)
        {
            long capacity = (long)Math.Pow(Charset.Length, length);
            if (capacity - existing.Count(id => id.Length == length) < GrowthThreshold)
            {
                length++;
                continue;
            }

            string id;
            do
            {
                id = new string(Enumerable.Range(0, length)
                    .Select(_ => Charset[_rng.Next(Charset.Length)])
                    .ToArray());
            } while (existing.Contains(id));

            return id;
        }
    }
}

public class GameRegistry
{
    private readonly ConcurrentDictionary<string, GameInstance> _games = new();
    private readonly IGameIdStrategy _idStrategy;
    private readonly ILogger<GameLogic> _gameLogger;
    private readonly IRandomService _random;

    public GameRegistry(IGameIdStrategy idStrategy, ILogger<GameLogic> gameLogger, IRandomService random)
    {
        _idStrategy = idStrategy;
        _gameLogger = gameLogger;
        _random = random;
    }

    public GameInstance CreateGame(string name, string creatorToken, int numRows, int numCols)
    {
        var id = _idStrategy.GenerateId(_games.Keys);
        var game = new GameLogic(_gameLogger, _random);
        var instance = new GameInstance
        {
            Id = id,
            Name = name,
            CreatorToken = creatorToken,
            CreatedAt = DateTime.UtcNow,
            Game = game,
        };
        _games[id] = instance;
        return instance;
    }

    public GameInstance? GetGame(string id) =>
        _games.TryGetValue(id, out var instance) ? instance : null;

    public IEnumerable<GameInstance> AllGames() => _games.Values;

    public void RemoveGame(string id) => _games.TryRemove(id, out _);
}
```

Add `using System.Collections.Concurrent;` at the top.

- [ ] **Step 4: Run tests**

```bash
dotnet test HungryTests --filter "FullyQualifiedName~GameRegistryTests"
```

Expected: All pass. (The `GrowsToFourChars` test checks length > 3; passing a huge fake existing list triggers it.)

- [ ] **Step 5: Commit**

```bash
git add HungryGame/GameRegistry.cs HungryTests/GameRegistryTests.cs
git commit -m "feat: add GameRegistry, GameInstance, IGameIdStrategy, ShortRandomIdStrategy"
```

---

## Task 5: Add GameCleanupService

**Files:**
- Create: `HungryGame/GameCleanupService.cs`

- [ ] **Step 1: Create GameCleanupService.cs**

```csharp
namespace HungryGame;

public class GameCleanupService : BackgroundService
{
    private readonly GameRegistry _registry;
    private readonly ILogger<GameCleanupService> _log;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public GameCleanupService(GameRegistry registry, ILogger<GameCleanupService> log)
    {
        _registry = registry;
        _log = log;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(Interval, stoppingToken);
            var cutoff = DateTime.UtcNow - Retention;
            foreach (var instance in _registry.AllGames().ToList())
            {
                if (instance.CompletedAt.HasValue && instance.CompletedAt.Value < cutoff)
                {
                    _registry.RemoveGame(instance.Id);
                    _log.LogInformation("Cleaned up completed game {Id} ({Name})", instance.Id, instance.Name);
                }
            }
        }
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build HungryGame
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add HungryGame/GameCleanupService.cs
git commit -m "feat: add GameCleanupService for 30-day completed game retention"
```

---

## Task 6: Rewrite Program.cs — DI registration and routes

**Files:**
- Modify: `HungryGame/Program.cs`

This is the largest single-file change. Replace the relevant sections completely.

- [ ] **Step 1: Update DI registrations**

Remove:
```csharp
builder.Services.AddSingleton<GameLogic>();
```

Add (after `builder.Services.AddSingleton<IRandomService, SystemRandomService>();`):
```csharp
builder.Services.AddSingleton<IGameIdStrategy, ShortRandomIdStrategy>();
builder.Services.AddSingleton<GameRegistry>();
builder.Services.AddHostedService<GameCleanupService>();
```

`AdminTokenService` was added in Task 3. Keep it.

Also remove `builder.Services.AddMemoryCache();` if present — the per-game caching approach is replaced by the registry. (The `IMemoryCache` injection in old route handlers disappears with the route rewrite.)

- [ ] **Step 2: Replace all API route handlers**

Delete every `app.MapGet` and `app.MapPost` below the middleware setup. Replace with:

```csharp
// Lobby endpoints
app.MapGet("games", (GameRegistry registry) =>
{
    return registry.AllGames().Select(i => new
    {
        i.Id,
        i.Name,
        State = i.Game.CurrentGameState.ToString(),
        PlayerCount = i.Game.GetPlayersByScoreDescending().Count(),
        i.Game.MaxRows,
        i.Game.MaxCols,
        i.CreatedAt,
        i.CompletedAt,
        WinnerName = i.Game.IsGameOver
            ? i.Game.GetPlayersByGameOverRank().FirstOrDefault()?.Name
            : null
    });
}).RequireRateLimiting("fixed");

app.MapPost("games", (CreateGameRequest req, GameRegistry registry, AdminTokenService adminTokens) =>
{
    bool isAdmin = adminTokens.IsValid(req.AdminToken);
    const int MaxUserRows = 100;
    const int MaxUserCols = 150;

    if (!isAdmin && (req.NumRows > MaxUserRows || req.NumCols > MaxUserCols))
        return Results.BadRequest($"Board size capped at {MaxUserRows}x{MaxUserCols} for user-created games.");

    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest("Game name is required.");

    var instance = registry.CreateGame(
        req.Name.Trim(),
        req.CreatorToken,
        req.NumRows,
        req.NumCols);

    // Pre-configure the game info so MaxRows/MaxCols are set
    instance.Game.StartGame(new NewGameInfo
    {
        NumRows = req.NumRows,
        NumColumns = req.NumCols,
        IsTimed = req.IsTimed,
        TimeLimitInMinutes = req.TimeLimitMinutes
    });

    // Wait — StartGame also starts the game (state → Eating). We only want to configure.
    // Instead, store the config on the instance and start separately.
    // See note below.
    return Results.Ok(new { instance.Id, instance.Name });
}).RequireRateLimiting("fixed");
```

**Important note on game creation vs game start:** `GameLogic.StartGame` currently both configures AND transitions state. We need to separate "configure" from "start". The simplest fix: add a `ConfigureGame(NewGameInfo)` method to `GameLogic` that sets `MaxRows`, `MaxCols`, `LastGameInfo` without transitioning state. Then `StartGame` (no-arg or just the method call by creator) transitions to Eating.

Update `GameLogic`:

```csharp
// Add this method:
public void ConfigureGame(NewGameInfo gameInfo)
{
    MaxRows = gameInfo.NumRows;
    MaxCols = gameInfo.NumColumns;
    LastGameInfo = gameInfo;

    if (gameInfo.IsTimed && gameInfo.TimeLimitInMinutes.HasValue)
    {
        var minutes = gameInfo.TimeLimitInMinutes.Value;
        TimeLimit = TimeSpan.FromMinutes(minutes);
        GameEndsOn = DateTime.Now.Add(TimeLimit.Value);
    }
}

// Update StartGame to just transition state (called after ConfigureGame):
public void StartGame()
{
    if (Interlocked.Read(ref gameStateValue) != 0 || MaxRows == 0)
        return;

    if (LastGameInfo?.IsTimed == true && LastGameInfo.TimeLimitInMinutes.HasValue)
    {
        gameTimer = new Timer(gameOverCallback, null, TimeLimit!.Value, Timeout.InfiniteTimeSpan);
    }

    _scoreHistory.Clear();
    _battleStartedAt = null;
    _playerEliminationTimes.Clear();
    initializeGame();
}
```

Update `GameRegistry.CreateGame` to call `ConfigureGame` with the size:

```csharp
public GameInstance CreateGame(string name, string creatorToken, int numRows, int numCols,
    bool isTimed = false, int? timeLimitMinutes = null)
{
    var id = _idStrategy.GenerateId(_games.Keys);
    var game = new GameLogic(_gameLogger, _random);
    game.ConfigureGame(new NewGameInfo
    {
        NumRows = numRows,
        NumColumns = numCols,
        IsTimed = isTimed,
        TimeLimitInMinutes = timeLimitMinutes
    });
    var instance = new GameInstance
    {
        Id = id,
        Name = name,
        CreatorToken = creatorToken,
        CreatedAt = DateTime.UtcNow,
        Game = game,
    };
    _games[id] = instance;
    return instance;
}
```

Update the `POST /games` handler to use this and not call `StartGame`:

```csharp
app.MapPost("games", (CreateGameRequest req, GameRegistry registry, AdminTokenService adminTokens) =>
{
    bool isAdmin = adminTokens.IsValid(req.AdminToken);
    const int MaxUserRows = 100;
    const int MaxUserCols = 150;

    if (!isAdmin && (req.NumRows > MaxUserRows || req.NumCols > MaxUserCols))
        return Results.BadRequest($"Board size capped at {MaxUserRows}x{MaxUserCols} for user-created games.");

    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest("Game name is required.");

    var instance = registry.CreateGame(
        req.Name.Trim(),
        req.CreatorToken,
        req.NumRows,
        req.NumCols,
        req.IsTimed,
        req.TimeLimitMinutes);

    return Results.Ok(new { instance.Id, instance.Name });
}).RequireRateLimiting("fixed");
```

Add `CreateGameRequest` record to `Records.cs`:

```csharp
public record CreateGameRequest(
    string Name,
    int NumRows,
    int NumCols,
    string CreatorToken,
    bool IsTimed,
    int? TimeLimitMinutes,
    string? AdminToken);
```

- [ ] **Step 3: Add all game-scoped route handlers**

```csharp
// Helper: resolve game or return 404
static GameInstance? ResolveGame(string id, GameRegistry registry) =>
    registry.GetGame(id);

static bool IsAuthorized(GameInstance instance, string? creatorToken, AdminTokenService adminTokens, string? adminToken) =>
    adminTokens.IsValid(adminToken) || instance.CreatorToken == creatorToken;

app.MapGet("game/{id}/join", (string id, string? userName, string? playerName, GameRegistry registry) =>
{
    var instance = ResolveGame(id, registry);
    if (instance == null) return Results.NotFound();
    var name = userName ?? playerName ?? throw new ArgumentNullException("userName");
    return Results.Ok(instance.Game.JoinPlayer(name));
}).RequireRateLimiting("fixed");

app.MapGet("game/{id}/move/{dir}", (string id, string dir, string token, GameRegistry registry) =>
{
    var instance = ResolveGame(id, registry);
    if (instance == null) return Results.NotFound();
    if (!Enum.TryParse<Direction>(dir, ignoreCase: true, out var direction))
        return Results.BadRequest("Unknown direction");
    return Results.Ok(instance.Game.Move(token, direction));
}).RequireRateLimiting("fixed");

app.MapGet("game/{id}/board", (string id, GameRegistry registry) =>
{
    var instance = ResolveGame(id, registry);
    if (instance == null) return Results.NotFound();
    return Results.Ok(instance.Game.GetBoardState());
}).RequireRateLimiting("fixed");

app.MapGet("game/{id}/players", (string id, GameRegistry registry) =>
{
    var instance = ResolveGame(id, registry);
    if (instance == null) return Results.NotFound();
    return Results.Ok(instance.Game.GetPlayersByScoreDescending()
        .Select(p => new { p.Name, p.Id, p.Score }));
}).RequireRateLimiting("fixed");

app.MapGet("game/{id}/state", (string id, GameRegistry registry) =>
{
    var instance = ResolveGame(id, registry);
    if (instance == null) return Results.NotFound();
    return Results.Ok(instance.Game.CurrentGameState.ToString());
}).RequireRateLimiting("fixed");

app.MapGet("game/{id}/start", (string id, string? creatorToken, string? adminToken,
    GameRegistry registry, AdminTokenService adminTokens) =>
{
    var instance = ResolveGame(id, registry);
    if (instance == null) return Results.NotFound();
    if (!IsAuthorized(instance, creatorToken, adminTokens, adminToken))
        return Results.Unauthorized();
    instance.Game.StartGame();
    return Results.Ok();
}).RequireRateLimiting("fixed");

app.MapGet("game/{id}/reset", (string id, string? creatorToken, string? adminToken,
    GameRegistry registry, AdminTokenService adminTokens) =>
{
    var instance = ResolveGame(id, registry);
    if (instance == null) return Results.NotFound();
    if (!IsAuthorized(instance, creatorToken, adminTokens, adminToken))
        return Results.Unauthorized();
    instance.Game.ResetGame();
    return Results.Ok();
}).RequireRateLimiting("fixed");

app.MapPost("game/{id}/admin/boot", (string id, BootRequest req,
    GameRegistry registry, AdminTokenService adminTokens) =>
{
    var instance = ResolveGame(id, registry);
    if (instance == null) return Results.NotFound();
    if (!IsAuthorized(instance, req.CreatorToken, adminTokens, req.AdminToken))
        return Results.Unauthorized();
    instance.Game.BootPlayer(req.PlayerId);
    return Results.Ok();
}).RequireRateLimiting("fixed");

app.MapPost("game/{id}/admin/clear-players", (string id, AuthRequest req,
    GameRegistry registry, AdminTokenService adminTokens) =>
{
    var instance = ResolveGame(id, registry);
    if (instance == null) return Results.NotFound();
    if (!IsAuthorized(instance, req.CreatorToken, adminTokens, req.AdminToken))
        return Results.Unauthorized();
    instance.Game.ClearAllPlayers();
    return Results.Ok();
}).RequireRateLimiting("fixed");

// Global admin auth
app.MapPost("admin/login", (AdminLoginRequest req, AdminTokenService adminTokens) =>
{
    var token = adminTokens.Login(req.Password);
    if (token == null) return Results.Unauthorized();
    return Results.Ok(token);
}).RequireRateLimiting("fixed");

app.MapPost("admin/logout", (AdminLogoutRequest req, AdminTokenService adminTokens) =>
{
    adminTokens.Logout(req.AdminToken);
    return Results.Ok();
}).RequireRateLimiting("fixed");
```

Add these request records to `Records.cs`:

```csharp
public record AuthRequest(string? CreatorToken, string? AdminToken);
public record BootRequest(int PlayerId, string? CreatorToken, string? AdminToken);
public record AdminLoginRequest(string Password);
public record AdminLogoutRequest(string AdminToken);
```

- [ ] **Step 4: Remove IMemoryCache from DI and usages (no longer needed)**

Remove `builder.Services.AddMemoryCache();` from Program.cs if present. The new route handlers don't use it.

- [ ] **Step 5: Build**

```bash
dotnet build HungryGame
```

Fix any remaining compile errors (typically missing usings or type mismatches).

- [ ] **Step 6: Run all tests**

```bash
dotnet test HungryTests
```

Expected: All pass.

- [ ] **Step 7: Commit**

```bash
git add -A
git commit -m "feat: rewrite Program.cs with multi-game routes and GameRegistry DI"
```

---

## Task 7: Add lobby styles to site.css

**Files:**
- Modify: `HungryGame/wwwroot/css/site.css`

- [ ] **Step 1: Append lobby card styles to site.css**

Add at the end of `site.css`:

```css
/* ---- Lobby ---- */
.lobby-topbar {
    display: flex;
    align-items: center;
    justify-content: space-between;
    flex-wrap: wrap;
    gap: 12px;
    margin-bottom: 28px;
}

.lobby-tagline {
    color: var(--text-secondary);
    font-size: 0.9rem;
}

.lobby-section {
    margin-bottom: 36px;
}

.lobby-section-heading {
    display: flex;
    align-items: center;
    gap: 10px;
    margin-bottom: 14px;
}

.lobby-section-heading h2 {
    font-family: var(--font-display);
    font-size: 0.8rem;
    font-weight: 700;
    letter-spacing: 2px;
    text-transform: uppercase;
    margin: 0;
}

.lobby-section-count {
    font-family: var(--font-display);
    font-size: 0.7rem;
    font-weight: 700;
    padding: 2px 8px;
    border-radius: 100px;
    letter-spacing: 1px;
}

.lobby-dot {
    width: 8px;
    height: 8px;
    border-radius: 50%;
    flex-shrink: 0;
}

/* Section color variants */
.lobby-join .lobby-section-heading h2 { color: var(--neon-cyan); }
.lobby-join .lobby-dot { background: var(--neon-cyan); box-shadow: 0 0 6px var(--neon-cyan); }
.lobby-join .lobby-section-count { background: rgba(0,180,216,0.12); color: var(--neon-cyan); border: 1px solid rgba(0,180,216,0.25); }

.lobby-progress .lobby-section-heading h2 { color: var(--neon-green); }
.lobby-progress .lobby-dot { background: var(--neon-green); box-shadow: 0 0 6px var(--neon-green); animation: pulse-green 2s ease-in-out infinite; }
.lobby-progress .lobby-section-count { background: rgba(0,255,136,0.1); color: var(--neon-green); border: 1px solid rgba(0,255,136,0.2); }

.lobby-completed .lobby-section-heading h2 { color: var(--text-muted); }
.lobby-completed .lobby-dot { background: var(--text-muted); }
.lobby-completed .lobby-section-count { background: rgba(108,108,128,0.15); color: var(--text-secondary); border: 1px solid rgba(108,108,128,0.2); }

/* Card grid */
.lobby-card-grid {
    display: flex;
    flex-wrap: wrap;
    gap: 14px;
}

/* Game card */
.lobby-game-card {
    background: var(--bg-card);
    border: 1px solid var(--border-subtle);
    border-radius: var(--radius-md);
    box-shadow: var(--shadow-card);
    padding: 16px 18px;
    position: relative;
    overflow: hidden;
    width: 220px;
    flex-shrink: 0;
    display: flex;
    flex-direction: column;
    gap: 8px;
    cursor: pointer;
    transition: background 0.2s, border-color 0.2s, transform 0.15s, box-shadow 0.2s;
    text-decoration: none;
    color: inherit;
}

.lobby-game-card::before {
    content: '';
    position: absolute;
    top: 0;
    left: 0;
    right: 0;
    height: 2px;
}

.lobby-join .lobby-game-card::before { background: linear-gradient(90deg, var(--neon-cyan), var(--neon-green)); }
.lobby-progress .lobby-game-card::before { background: linear-gradient(90deg, var(--neon-green), var(--neon-cyan), var(--neon-pink)); }
.lobby-completed .lobby-game-card::before { background: linear-gradient(90deg, var(--text-muted), transparent); }

.lobby-join .lobby-game-card:hover,
.lobby-progress .lobby-game-card:hover {
    background: var(--bg-card-hover);
    transform: translateY(-2px);
    box-shadow: 0 0 20px rgba(0,180,216,0.12), 0 4px 24px rgba(0,0,0,0.4);
}

.lobby-completed .lobby-game-card { opacity: 0.85; }
.lobby-completed .lobby-game-card:hover { opacity: 1; background: var(--bg-card-hover); }

.lobby-card-name {
    font-family: var(--font-display);
    font-size: 0.8rem;
    font-weight: 700;
    color: var(--text-primary);
    white-space: nowrap;
    overflow: hidden;
    text-overflow: ellipsis;
}

.lobby-completed .lobby-card-name { color: var(--text-secondary); }

.lobby-card-id {
    font-family: 'Press Start 2P', monospace;
    font-size: 0.6rem;
    color: var(--neon-cyan);
    letter-spacing: 2px;
    background: rgba(0,180,216,0.1);
    border: 1px solid rgba(0,180,216,0.25);
    border-radius: 4px;
    padding: 3px 8px;
    display: inline-block;
    width: fit-content;
}

.lobby-completed .lobby-card-id {
    color: var(--text-muted);
    background: rgba(108,108,128,0.1);
    border-color: rgba(108,108,128,0.2);
}

.lobby-card-divider {
    height: 1px;
    background: var(--border-subtle);
    margin: 2px 0;
}

.lobby-card-meta { display: flex; flex-direction: column; gap: 4px; }

.lobby-card-meta-row {
    display: flex;
    align-items: center;
    justify-content: space-between;
    gap: 8px;
}

.lobby-card-label {
    font-family: var(--font-display);
    font-size: 0.6rem;
    font-weight: 600;
    letter-spacing: 1px;
    text-transform: uppercase;
    color: var(--text-muted);
}

.lobby-card-value {
    font-family: var(--font-display);
    font-size: 0.7rem;
    font-weight: 700;
    color: var(--text-secondary);
}

.lobby-card-winner {
    font-family: var(--font-display);
    font-size: 0.65rem;
    font-weight: 700;
    color: var(--neon-yellow);
    margin-top: 2px;
}

.lobby-card-winner .label { color: var(--text-muted); font-weight: 600; }

/* Create game modal */
.modal-overlay {
    position: fixed;
    inset: 0;
    background: rgba(0,0,0,0.7);
    display: flex;
    align-items: center;
    justify-content: center;
    z-index: 1000;
    backdrop-filter: blur(4px);
}

.modal-box {
    background: var(--bg-card);
    border: 1px solid var(--border-subtle);
    border-radius: var(--radius-lg);
    padding: 32px;
    width: 100%;
    max-width: 440px;
    position: relative;
    box-shadow: 0 0 60px rgba(0,0,0,0.8);
    animation: slideUp 0.25s ease-out;
}

.modal-box::before {
    content: '';
    position: absolute;
    top: 0; left: 0; right: 0;
    height: 2px;
    background: linear-gradient(90deg, var(--neon-green), var(--neon-cyan));
    border-radius: var(--radius-lg) var(--radius-lg) 0 0;
}

.modal-title {
    font-family: var(--font-display);
    font-size: 1rem;
    font-weight: 700;
    color: var(--neon-green);
    letter-spacing: 2px;
    text-transform: uppercase;
    margin-bottom: 24px;
}

.modal-close {
    position: absolute;
    top: 16px; right: 16px;
    background: none; border: none;
    color: var(--text-muted);
    font-size: 1.2rem;
    cursor: pointer;
    line-height: 1;
    padding: 4px;
    transition: color 0.2s;
}

.modal-close:hover { color: var(--text-primary); }

.modal-field {
    display: flex;
    flex-direction: column;
    gap: 6px;
    margin-bottom: 16px;
}

.modal-field label {
    font-family: var(--font-display);
    font-size: 0.7rem;
    font-weight: 600;
    letter-spacing: 1px;
    text-transform: uppercase;
    color: var(--text-secondary);
}

.modal-row {
    display: grid;
    grid-template-columns: 1fr 1fr;
    gap: 12px;
}

.modal-actions {
    display: flex;
    gap: 12px;
    margin-top: 24px;
}

.modal-actions .btn-primary { flex: 1; }
```

- [ ] **Step 2: Build**

```bash
dotnet build HungryGame
```

Expected: Build succeeded.

- [ ] **Step 3: Commit**

```bash
git add HungryGame/wwwroot/css/site.css
git commit -m "feat: add lobby card and modal CSS styles"
```

---

## Task 8: Convert game components to CascadingParameter

**Files:**
- Modify: `HungryGame/Shared/Board.razor`
- Modify: `HungryGame/Shared/PlayerList.razor`
- Modify: `HungryGame/Shared/CurrentGameState.razor`
- Modify: `HungryGame/Shared/AddPlayer.razor`
- Modify: `HungryGame/Shared/ScoreHistoryChart.razor`
- Modify: `HungryGame/Shared/ResetGame.razor`

In each component: replace `@inject GameLogic gameInfo` with a `[CascadingParameter]`. The cascade type is `GameLogic` — the Game page cascades the specific instance. All existing code using `gameInfo` keeps working unchanged.

- [ ] **Step 1: Update Board.razor**

Remove line:
```razor
@inject GameLogic gameInfo
```

Add to `@code` block:
```csharp
[CascadingParameter]
public GameLogic gameInfo { get; set; } = default!;
```

Also remove emoji from the `PushBoardToCanvas` JS call. Replace:
```csharp
var cellIcon = gameInfo.LastGameInfo?.CellIcon ?? "";
var useCustomEmoji = gameInfo.LastGameInfo?.UseCustomEmoji ?? false;
await JS.InvokeVoidAsync("boardRenderer.render", rows, cols, gridData, playerPositions, cellIcon, useCustomEmoji);
```

With:
```csharp
await JS.InvokeVoidAsync("boardRenderer.render", rows, cols, gridData, playerPositions);
```

(The JS boardRenderer.render signature will need to be updated in Task 10 to not use emoji params.)

- [ ] **Step 2: Update PlayerList.razor**

Remove:
```razor
@inject GameLogic gameInfo
```

Add to `@code`:
```csharp
[CascadingParameter]
public GameLogic gameInfo { get; set; } = default!;
```

Remove the `adminToken` field and `EnsureAdminToken()` method. Replace `BootPlayer` and `ClearAllPlayers` methods:

```csharp
private void BootPlayer(int playerId)
{
    if (SharedState.CanManage)
        gameInfo.BootPlayer(playerId);
}

private void ClearAllPlayers()
{
    if (SharedState.CanManage)
        gameInfo.ClearAllPlayers();
}
```

Update the boot button condition from `SharedState?.IsAdmin == true` to `SharedState?.CanManage == true`:

```razor
@if (SharedState?.CanManage == true)
{
    <button class="boot-btn" @onclick="() => BootPlayer(p.Id)" title="Boot player">X</button>
}
```

And the Clear All button:
```razor
@if (SharedState?.CanManage == true)
{
    <button class="clear-all-btn" @onclick="ClearAllPlayers">Clear All Players</button>
}
```

- [ ] **Step 3: Update CurrentGameState.razor**

Remove `@inject GameLogic gameInfo`. Add to `@code`:
```csharp
[CascadingParameter]
public GameLogic gameInfo { get; set; } = default!;
```

- [ ] **Step 4: Update AddPlayer.razor**

Remove `@inject GameLogic gameInfo`. Add to `@code`:
```csharp
[CascadingParameter]
public GameLogic gameInfo { get; set; } = default!;
```

- [ ] **Step 5: Update ScoreHistoryChart.razor**

Remove `@inject GameLogic gameInfo`. Add to `@code`:
```csharp
[CascadingParameter]
public GameLogic gameInfo { get; set; } = default!;
```

- [ ] **Step 6: Update ResetGame.razor**

Remove:
```razor
@inject GameLogic gameInfo
@inject IConfiguration config
```

Add to `@code`:
```csharp
[CascadingParameter]
public GameLogic gameInfo { get; set; } = default!;
```

Replace the reset logic to use `SharedState.CanManage` instead of password:

```razor
<div class="reset-panel">
    @if (SharedState.CanManage)
    {
        <button class="btn btn-secondary w-100" @onclick="Reset">Reset Game</button>
    }
</div>

@code {
    [CascadingParameter]
    public GameLogic gameInfo { get; set; } = default!;

    [CascadingParameter]
    public SharedStateClass SharedState { get; set; } = default!;

    private void Reset()
    {
        if (SharedState.CanManage)
            gameInfo.ResetGame();
    }
}
```

- [ ] **Step 7: Build**

```bash
dotnet build HungryGame
```

Fix any compile errors. Common issue: `GameLogic` no longer has `AdminLogin` — those calls were removed in Step 2 of this task.

- [ ] **Step 8: Commit**

```bash
git add HungryGame/Shared/
git commit -m "feat: convert game components to CascadingParameter GameLogic"
```

---

## Task 9: Create StartGameButton.razor

**Files:**
- Create: `HungryGame/Shared/StartGameButton.razor`

This replaces `StartGame.razor`. It's a simple button — all configuration happened at game creation.

- [ ] **Step 1: Create StartGameButton.razor**

```razor
@code {
    [CascadingParameter]
    public GameLogic gameInfo { get; set; } = default!;

    [CascadingParameter]
    public SharedStateClass SharedState { get; set; } = default!;

    private void StartGame()
    {
        if (SharedState.CanManage)
            gameInfo.StartGame();
    }
}

@if (SharedState.CanManage && !gameInfo.IsGameStarted)
{
    <button class="btn btn-primary w-100" @onclick="StartGame">Launch Game</button>
}
```

- [ ] **Step 2: Commit**

```bash
git add HungryGame/Shared/StartGameButton.razor
git commit -m "feat: add StartGameButton component (replaces StartGame form)"
```

---

## Task 10: Create Game.razor — the per-game page

**Files:**
- Create: `HungryGame/Pages/Game.razor`

This is the main game view, parameterized by game ID. It replaces `Index.razor` for per-game content.

- [ ] **Step 1: Create Game.razor**

```razor
@page "/game/{Id}"
@inject GameRegistry Registry
@inject IConfiguration Config
@inject IJSRuntime JS
@inject NavigationManager Nav

<PageTitle>@(instance?.Name ?? "Game Not Found") — Hungry Game</PageTitle>

@if (instance == null)
{
    <div class="page-header">
        <h1 class="game-title">Hungry Game</h1>
    </div>
    <div class="error-message">Game not found. <a href="/">Back to Lobby</a></div>
}
else
{
    <div class="page-header">
        <div style="display:flex;align-items:center;gap:14px;flex-wrap:wrap">
            <h1 class="game-title">@instance.Name</h1>
            <span class="lobby-card-id">@instance.Id</span>
        </div>
        <div class="nav-links">
            <a href="/">Lobby</a>
            <span style="color:var(--text-muted)">·</span>
            <a href="help">Help / Docs</a>
            <span style="color:var(--text-muted)">·</span>
            <a href="scalar/v1">API Client</a>
            <span style="color:var(--text-muted)">·</span>
            <a href="player">Web Player</a>
            <span style="color:var(--text-muted)">·</span>
            @if (SharedState.IsAdmin)
            {
                <a href="javascript:void(0)" @onclick="AdminLogout">Logout</a>
            }
            else
            {
                <form style="display:inline-flex;align-items:center;gap:4px;" @onsubmit="AdminLogin">
                    <input type="password" @bind="adminPasswordInput" placeholder="admin pw"
                           style="width:90px;padding:2px 6px;font-size:0.8rem;background:#222;border:1px solid #555;color:#eee;border-radius:4px;" />
                    <button type="submit" style="padding:2px 8px;font-size:0.8rem;background:#333;border:1px solid #555;color:#eee;border-radius:4px;cursor:pointer;">Login</button>
                </form>
            }
        </div>
    </div>

    <CascadingValue Value="instance.Game">
        <CascadingValue Value="SharedState">
            <CurrentGameState />

            <div class="game-layout mt-3">
                @if (instance.Game.IsGameStarted)
                {
                    if (instance.Game.IsGameOver)
                    {
                        <div class="game-main">
                            <ScoreHistoryChart />
                        </div>
                        <div class="game-sidebar">
                            <ResetGame />
                            <PlayerList />
                        </div>
                    }
                    else
                    {
                        <div class="game-main">
                            <Board />
                        </div>
                        <div class="game-sidebar">
                            <ResetGame />
                            <PlayerList />
                        </div>
                    }
                }
                else
                {
                    <div class="game-main">
                        <AddPlayer />
                    </div>
                    <div class="game-sidebar">
                        <StartGameButton />
                        <PlayerList />
                    </div>
                }
            </div>
        </CascadingValue>
    </CascadingValue>
}

@code {
    [Parameter] public string Id { get; set; } = "";

    private GameInstance? instance;
    private SharedStateClass SharedState { get; set; } = new();
    private string? adminPasswordInput;

    protected override void OnInitialized()
    {
        instance = Registry.GetGame(Id);
        instance?.Game.GameStateChanged += OnGameStateChanged;
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        var saved = await JS.InvokeAsync<string?>("localStorage.getItem", "adminPassword");
        if (saved != null && saved == Config["SECRET_CODE"])
        {
            SharedState.IsAdmin = true;
            SharedState.AdminPassword = saved;
        }
        var userToken = await JS.InvokeAsync<string?>("localStorage.getItem", "userToken");
        if (instance != null && userToken == instance.CreatorToken)
        {
            SharedState.UserToken = userToken;
            SharedState.IsCreator = true;
        }
        StateHasChanged();
    }

    private void OnGameStateChanged(object? sender, EventArgs e) =>
        InvokeAsync(StateHasChanged);

    private async Task AdminLogin()
    {
        if (adminPasswordInput == Config["SECRET_CODE"])
        {
            SharedState.IsAdmin = true;
            SharedState.AdminPassword = adminPasswordInput;
            await JS.InvokeVoidAsync("localStorage.setItem", "adminPassword", adminPasswordInput);
            StateHasChanged();
        }
        adminPasswordInput = null;
    }

    private async Task AdminLogout()
    {
        SharedState.IsAdmin = false;
        SharedState.AdminPassword = null;
        await JS.InvokeVoidAsync("localStorage.removeItem", "adminPassword");
        StateHasChanged();
    }
}
```

- [ ] **Step 2: Build**

```bash
dotnet build HungryGame
```

- [ ] **Step 3: Commit**

```bash
git add HungryGame/Pages/Game.razor
git commit -m "feat: add Game.razor page at /game/{id}"
```

---

## Task 11: Create CreateGameModal.razor

**Files:**
- Create: `HungryGame/Shared/CreateGameModal.razor`

- [ ] **Step 1: Create CreateGameModal.razor**

```razor
@inject NavigationManager Nav
@inject IJSRuntime JS

@if (IsVisible)
{
    <div class="modal-overlay" @onclick="Close">
        <div class="modal-box" @onclick:stopPropagation="true">
            <button class="modal-close" @onclick="Close">&times;</button>
            <div class="modal-title">Create Game</div>

            <EditForm Model="@model" OnValidSubmit="HandleSubmit">
                <div class="modal-field">
                    <label>Game Name</label>
                    <InputText @bind-Value="model.Name" placeholder="e.g. Friday Night Showdown"
                               class="form-control" maxlength="60" />
                </div>

                <div class="modal-row">
                    <div class="modal-field">
                        <label>Rows (max @MaxRows)</label>
                        <InputNumber @bind-Value="model.NumRows" class="form-control"
                                     min="2" max="@MaxRows" />
                    </div>
                    <div class="modal-field">
                        <label>Cols (max @MaxCols)</label>
                        <InputNumber @bind-Value="model.NumCols" class="form-control"
                                     min="2" max="@MaxCols" />
                    </div>
                </div>

                <div style="display:flex;align-items:center;gap:10px;margin-bottom:12px;">
                    <InputCheckbox @bind-Value="model.IsTimed" id="isTimed" class="form-check-input" />
                    <label class="form-label" for="isTimed" style="text-transform:none;font-size:0.9rem;margin:0;">Timed Game</label>
                </div>

                @if (model.IsTimed)
                {
                    <div class="modal-field">
                        <label>Time Limit (minutes)</label>
                        <InputNumber @bind-Value="model.TimeLimitMinutes" class="form-control" min="1" max="60" />
                    </div>
                }

                @if (errorMessage != null)
                {
                    <div class="error-message" style="margin-bottom:12px;">@errorMessage</div>
                }

                <div class="modal-actions">
                    <button type="submit" class="btn btn-primary">Create &amp; Go</button>
                    <button type="button" class="btn btn-secondary" @onclick="Close">Cancel</button>
                </div>
            </EditForm>
        </div>
    </div>
}

@code {
    [Parameter] public bool IsVisible { get; set; }
    [Parameter] public EventCallback OnClose { get; set; }
    [Parameter] public bool IsAdmin { get; set; }

    private int MaxRows => IsAdmin ? 9999 : 100;
    private int MaxCols => IsAdmin ? 9999 : 150;

    private CreateGameModel model = new();
    private string? errorMessage;

    private class CreateGameModel
    {
        public string Name { get; set; } = "";
        public int NumRows { get; set; } = 20;
        public int NumCols { get; set; } = 30;
        public bool IsTimed { get; set; }
        public int? TimeLimitMinutes { get; set; }
    }

    private async Task HandleSubmit()
    {
        errorMessage = null;
        var userToken = await JS.InvokeAsync<string?>("localStorage.getItem", "userToken");
        if (string.IsNullOrWhiteSpace(userToken))
        {
            // Generate and persist a new user token
            userToken = Guid.NewGuid().ToString();
            await JS.InvokeVoidAsync("localStorage.setItem", "userToken", userToken);
        }

        var req = new CreateGameRequest(
            model.Name.Trim(),
            model.NumRows,
            model.NumCols,
            userToken,
            model.IsTimed,
            model.IsTimed ? model.TimeLimitMinutes : null,
            null // AdminToken not used here; admin cap bypass handled server-side
        );

        using var http = new HttpClient();
        var baseUri = Nav.BaseUri.TrimEnd('/');
        var response = await http.PostAsJsonAsync($"{baseUri}/games", req);
        if (!response.IsSuccessStatusCode)
        {
            errorMessage = await response.Content.ReadAsStringAsync();
            return;
        }

        var result = await response.Content.ReadFromJsonAsync<GameCreatedResponse>();
        if (result != null)
        {
            Nav.NavigateTo($"/game/{result.Id}");
        }
    }

    private async Task Close()
    {
        model = new();
        errorMessage = null;
        await OnClose.InvokeAsync();
    }

    private record GameCreatedResponse(string Id, string Name);
}
```

Add `using System.Net.Http.Json;` to `_Imports.razor` if not present.

- [ ] **Step 2: Build**

```bash
dotnet build HungryGame
```

- [ ] **Step 3: Commit**

```bash
git add HungryGame/Shared/CreateGameModal.razor
git commit -m "feat: add CreateGameModal component"
```

---

## Task 12: Rewrite Index.razor as the Lobby

**Files:**
- Modify: `HungryGame/Pages/Index.razor`

Replace the entire file content:

- [ ] **Step 1: Rewrite Index.razor**

```razor
@page "/"
@inject GameRegistry Registry
@inject IConfiguration Config
@inject IJSRuntime JS
@implements IDisposable

<PageTitle>Hungry Game — Lobby</PageTitle>

<div class="page-header">
    <h1 class="game-title">Hungry Game</h1>
    <div class="nav-links">
        <a href="help">Help / Docs</a>
        <span style="color:var(--text-muted)">·</span>
        <a href="scalar/v1">API Client</a>
        <span style="color:var(--text-muted)">·</span>
        <a href="player">Web Player</a>
        <span style="color:var(--text-muted)">·</span>
        @if (isAdmin)
        {
            <a href="javascript:void(0)" @onclick="AdminLogout">Logout</a>
        }
        else
        {
            <form style="display:inline-flex;align-items:center;gap:4px;" @onsubmit="AdminLogin">
                <input type="password" @bind="adminPasswordInput" placeholder="admin pw"
                       style="width:90px;padding:2px 6px;font-size:0.8rem;background:#222;border:1px solid #555;color:#eee;border-radius:4px;" />
                <button type="submit" style="padding:2px 8px;font-size:0.8rem;background:#333;border:1px solid #555;color:#eee;border-radius:4px;cursor:pointer;">Login</button>
            </form>
        }
    </div>
</div>

<div class="lobby-topbar">
    <span class="lobby-tagline">Pick a game to watch or join, or create your own.</span>
    <button class="btn btn-primary" @onclick="() => showModal = true">+ Create Game</button>
</div>

@{
    var all = Registry.AllGames().ToList();
    var toJoin    = all.Where(g => g.Game.CurrentGameState == GameState.Joining).ToList();
    var inProgress = all.Where(g => g.Game.CurrentGameState is GameState.Eating or GameState.Battle).ToList();
    var completed  = all.Where(g => g.Game.IsGameOver).OrderByDescending(g => g.CompletedAt).ToList();
}

<!-- Games to Join -->
<div class="lobby-section lobby-join">
    <div class="lobby-section-heading">
        <div class="lobby-dot"></div>
        <h2>Games to Join</h2>
        <span class="lobby-section-count">@toJoin.Count</span>
    </div>
    <div class="lobby-card-grid">
        @foreach (var g in toJoin)
        {
            <a href="/game/@g.Id" class="lobby-game-card">
                <div class="lobby-card-name">@g.Name</div>
                <div class="lobby-card-id">@g.Id</div>
                <div class="lobby-card-divider"></div>
                <div class="lobby-card-meta">
                    <div class="lobby-card-meta-row">
                        <span class="lobby-card-label">Players</span>
                        <span class="lobby-card-value">@g.Game.GetPlayersByScoreDescending().Count() waiting</span>
                    </div>
                    <div class="lobby-card-meta-row">
                        <span class="lobby-card-label">Board</span>
                        <span class="lobby-card-value">@g.Game.MaxRows × @g.Game.MaxCols</span>
                    </div>
                </div>
                <div class="game-state-badge state-joining" style="font-size:0.6rem;padding:3px 8px;">
                    <span class="state-dot"></span> Joining
                </div>
            </a>
        }
    </div>
</div>

<!-- In Progress -->
<div class="lobby-section lobby-progress">
    <div class="lobby-section-heading">
        <div class="lobby-dot"></div>
        <h2>In Progress</h2>
        <span class="lobby-section-count">@inProgress.Count</span>
    </div>
    <div class="lobby-card-grid">
        @foreach (var g in inProgress)
        {
            var stateClass = g.Game.CurrentGameState == GameState.Battle ? "state-battle" : "state-eating";
            <a href="/game/@g.Id" class="lobby-game-card">
                <div class="lobby-card-name">@g.Name</div>
                <div class="lobby-card-id">@g.Id</div>
                <div class="lobby-card-divider"></div>
                <div class="lobby-card-meta">
                    <div class="lobby-card-meta-row">
                        <span class="lobby-card-label">Players</span>
                        <span class="lobby-card-value">@g.Game.GetPlayersByScoreDescending().Count() alive</span>
                    </div>
                    <div class="lobby-card-meta-row">
                        <span class="lobby-card-label">Board</span>
                        <span class="lobby-card-value">@g.Game.MaxRows × @g.Game.MaxCols</span>
                    </div>
                </div>
                <div class="game-state-badge @stateClass" style="font-size:0.6rem;padding:3px 8px;">
                    <span class="state-dot"></span> @g.Game.CurrentGameState
                </div>
            </a>
        }
    </div>
</div>

<!-- Completed -->
<div class="lobby-section lobby-completed">
    <div class="lobby-section-heading">
        <div class="lobby-dot"></div>
        <h2>Completed</h2>
        <span class="lobby-section-count">@completed.Count</span>
    </div>
    <div class="lobby-card-grid">
        @foreach (var g in completed)
        {
            var winner = g.Game.GetPlayersByGameOverRank().FirstOrDefault();
            var age = g.CompletedAt.HasValue
                ? FormatAge(DateTime.UtcNow - g.CompletedAt.Value)
                : "recently";
            <a href="/game/@g.Id" class="lobby-game-card">
                <div class="lobby-card-name">@g.Name</div>
                <div class="lobby-card-id">@g.Id</div>
                <div class="lobby-card-divider"></div>
                @if (winner != null)
                {
                    <div class="lobby-card-winner"><span class="label">Winner: </span>@winner.Name</div>
                }
                <div class="lobby-card-meta">
                    <div class="lobby-card-meta-row">
                        <span class="lobby-card-label">Players</span>
                        <span class="lobby-card-value">@g.Game.GetPlayersByScoreDescending().Count()</span>
                    </div>
                    <div class="lobby-card-meta-row">
                        <span class="lobby-card-label">Ended</span>
                        <span class="lobby-card-value">@age</span>
                    </div>
                </div>
                <div class="game-state-badge state-gameover" style="font-size:0.6rem;padding:3px 8px;">
                    <span class="state-dot"></span> Game Over
                </div>
            </a>
        }
    </div>
</div>

<CreateGameModal IsVisible="showModal" IsAdmin="isAdmin" OnClose="() => showModal = false" />

@code {
    private bool showModal;
    private bool isAdmin;
    private string? adminPasswordInput;
    private System.Timers.Timer? _refreshTimer;

    protected override void OnInitialized()
    {
        _refreshTimer = new System.Timers.Timer(5_000);
        _refreshTimer.Elapsed += (_, _) => InvokeAsync(StateHasChanged);
        _refreshTimer.Start();
    }

    protected override async Task OnAfterRenderAsync(bool firstRender)
    {
        if (!firstRender) return;
        var saved = await JS.InvokeAsync<string?>("localStorage.getItem", "adminPassword");
        if (saved != null && saved == Config["SECRET_CODE"])
        {
            isAdmin = true;
            StateHasChanged();
        }
    }

    private async Task AdminLogin()
    {
        if (adminPasswordInput == Config["SECRET_CODE"])
        {
            isAdmin = true;
            await JS.InvokeVoidAsync("localStorage.setItem", "adminPassword", adminPasswordInput);
            StateHasChanged();
        }
        adminPasswordInput = null;
    }

    private async Task AdminLogout()
    {
        isAdmin = false;
        await JS.InvokeVoidAsync("localStorage.removeItem", "adminPassword");
        StateHasChanged();
    }

    private static string FormatAge(TimeSpan age)
    {
        if (age.TotalDays >= 1) return $"{(int)age.TotalDays}d ago";
        if (age.TotalHours >= 1) return $"{(int)age.TotalHours}h ago";
        return $"{(int)age.TotalMinutes}m ago";
    }

    public void Dispose() => _refreshTimer?.Dispose();
}
```

- [ ] **Step 2: Build**

```bash
dotnet build HungryGame
```

- [ ] **Step 3: Commit**

```bash
git add HungryGame/Pages/Index.razor HungryGame/Shared/CreateGameModal.razor
git commit -m "feat: rewrite Index.razor as lobby page"
```

---

## Task 13: Update boardRenderer JS to remove emoji params

**Files:**
- Modify: `HungryGame/wwwroot/js/boardRenderer.js` (or wherever the JS canvas renderer lives)

- [ ] **Step 1: Find the JS file**

```bash
find HungryGame/wwwroot -name "*.js" | grep -i board
```

- [ ] **Step 2: Update render function signature**

Find the `render` function in the JS file. Remove `cellIcon` and `useCustomEmoji` parameters and any code that uses them. Pills always render as dots. The function signature should become:

```js
render: function(rows, cols, gridData, playerPositions) {
    // ... existing render logic, but pills always draw as a dot '·'
}
```

Remove any `if (useCustomEmoji)` branches. Replace custom emoji rendering with a consistent dot character (e.g. `'·'` U+00B7) or a small filled circle drawn on canvas.

- [ ] **Step 3: Build and smoke-test**

```bash
dotnet run --project HungryGame
```

Open the app, create a game via the lobby, navigate to it, verify the board renders without errors.

- [ ] **Step 4: Commit**

```bash
git add HungryGame/wwwroot/
git commit -m "feat: remove emoji from board renderer, pills always render as dots"
```

---

## Task 14: Mark games as completed when GameOver is reached

**Files:**
- Modify: `HungryGame/GameLogic.cs`
- Modify: `HungryGame/GameRegistry.cs`

The lobby needs `GameInstance.CompletedAt` to be set when a game ends.

- [ ] **Step 1: Add CompletedAt callback to GameLogic**

Add an `OnGameOver` event to `GameLogic`:

```csharp
public event EventHandler? GameOver;
```

In `checkForWinner()`, after `Interlocked.Increment(ref gameStateValue)`:
```csharp
if (activePlayersCount <= 1)
{
    Interlocked.Increment(ref gameStateValue);
    GameOver?.Invoke(this, EventArgs.Empty);  // add this line
}
```

In `changeToBattleModeIfNoMorePillsAvailable()`, after the game-over exchange:
```csharp
Interlocked.Exchange(ref gameStateValue, 3);
GameOver?.Invoke(this, EventArgs.Empty);  // add this line
```

In `gameOverCallback` (timer-triggered), after the state exchange:
```csharp
Interlocked.Exchange(ref gameStateValue, 3);
GameOver?.Invoke(this, EventArgs.Empty);  // add this line
```

- [ ] **Step 2: Wire up in GameRegistry.CreateGame**

In `GameRegistry.CreateGame`, after creating the `GameInstance`, subscribe to the event:

```csharp
instance.Game.GameOver += (_, _) =>
{
    instance.CompletedAt = DateTime.UtcNow;
};
```

- [ ] **Step 3: Build and run tests**

```bash
dotnet test HungryTests
dotnet build HungryGame
```

- [ ] **Step 4: Commit**

```bash
git add HungryGame/GameLogic.cs HungryGame/GameRegistry.cs
git commit -m "feat: set GameInstance.CompletedAt when game ends"
```

---

## Task 15: Update foolhearty bot client

**Files:**
- Modify: `clients/foolhearty/BasePlayerLogic.cs`

- [ ] **Step 1: Update BasePlayerLogic to use GAME_ID**

In `BasePlayerLogic.cs`, update `JoinGameAsync`:

```csharp
public virtual async Task JoinGameAsync()
{
    url = config["SERVER"] ?? "https://hungrygame.azurewebsites.net";
    var gameId = config["GAME_ID"] ?? throw new InvalidOperationException(
        "GAME_ID environment variable is required. Set it to the ID of the game to join.");
    token = await httpClient.GetStringAsync($"{url}/game/{gameId}/join?playerName={PlayerName}");
    _gameId = gameId;
}
```

Add a private field `private string _gameId = "";` to the class.

Update `checkIfGameOver`:
```csharp
protected async Task<bool> checkIfGameOver()
{
    return (await httpClient.GetStringAsync($"{url}/game/{_gameId}/state")) == "GameOver";
}
```

Update `waitForGameToStart`:
```csharp
protected async Task waitForGameToStart(CancellationToken cancellationToken)
{
    var gameState = await httpClient.GetStringAsync($"{url}/game/{_gameId}/state");
    while (gameState == "Joining" || gameState == "GameOver")
    {
        await Task.Delay(2_000, cancellationToken);
        gameState = await httpClient.GetStringAsync($"{url}/game/{_gameId}/state", cancellationToken);
    }
}
```

Update `getBoardAsync`:
```csharp
protected async Task<List<Cell>> getBoardAsync()
{
    var boardString = await httpClient.GetStringAsync($"{url}/game/{_gameId}/board");
    return JsonSerializer.Deserialize<IEnumerable<Cell>>(boardString)?.ToList() ?? throw new MissingBoardException();
}
```

Also update the move calls in `Foolhearty.cs` and `SmartyPants.cs` — find any direct calls to `$"{url}/move/{dir}?token={token}"` and update to `$"{url}/game/{_gameId}/move/{dir}?token={token}"`. Make `_gameId` `protected` so subclasses can access it.

- [ ] **Step 2: Build foolhearty**

```bash
dotnet build clients/foolhearty
```

- [ ] **Step 3: Commit**

```bash
git add clients/foolhearty/
git commit -m "feat: update foolhearty to use GAME_ID env var for multi-game routing"
```

---

## Task 16: Update massive bot client

**Files:**
- Modify: `clients/massive/MassiveClient.cs`

- [ ] **Step 1: Update MassiveClient to use GAME_ID**

In `MassiveClient.cs` constructor, read GAME_ID:

```csharp
private readonly string _gameId;

public MassiveClient(ILogger<MassiveClient> logger, IConfiguration config, ILogger<Player> playerLogger)
{
    this.logger = logger;
    this.config = config;
    this.playerLogger = playerLogger;
    socketsHandler = new SocketsHttpHandler();
    url = config["SERVER"] ?? "https://hungrygame.azurewebsites.net";
    _gameId = config["GAME_ID"] ?? throw new InvalidOperationException(
        "GAME_ID environment variable is required.");
}
```

Update all URL references in `MassiveClient` from `$"{url}/board"` → `$"{url}/game/{_gameId}/board"`, `$"{url}/players"` → `$"{url}/game/{_gameId}/players"`, `$"{url}/state"` → `$"{url}/game/{_gameId}/state"`.

In the `Player` class, update `JoinGameAsync`:
```csharp
public async Task JoinGameAsync()
{
    token = await httpClient.GetStringAsync($"{url}/game/{gameId}/join?playerName={PlayerName}");
    // Remove the file-based token caching — it's per-game now and the file approach is error-prone
}
```

Update `Player` constructor to accept `gameId`:
```csharp
public Player(IConfiguration config, string name, SocketsHttpHandler socketsHandler, string url, string gameId, ILogger<Player> logger)
{
    // ...
    this.gameId = gameId;
}
```

Add `private readonly string gameId;` field to `Player`.

Update `WaitForGameToStart` and `getBoardAsync` in `Player` to use `$"{url}/game/{gameId}/..."`.

Update `Move`:
```csharp
public async Task<MoveResult> Move(string direction)
{
    return await httpClient.GetFromJsonAsync<MoveResult>($"{url}/game/{gameId}/move/{direction}?token={token}");
}
```

Update the player instantiation in `MassiveClient.Run`:
```csharp
var players = (from i in Enumerable.Range(0, numClients)
               let name = $"Massive_{i:0000}"
               select new Player(config, name, socketsHandler, url, _gameId, playerLogger)).ToList();
```

- [ ] **Step 2: Build massive**

```bash
dotnet build clients/massive
```

- [ ] **Step 3: Commit**

```bash
git add clients/massive/
git commit -m "feat: update massive client to use GAME_ID env var"
```

---

## Task 17: Update AppHost

**Files:**
- Modify: `HungryGame.AppHost/AppHost.cs`

- [ ] **Step 1: Add gameId parameter and wire to bot clients**

```csharp
var gameId = builder.AddParameter("gameId", "");  // empty = must be set at runtime

builder.AddProject<Projects.massive>("massive")
    .WithEnvironment("SERVER", hungrygame.GetEndpoint("http"))
    .WithEnvironment("CLIENT_COUNT", massivePlayerCount)
    .WithEnvironment("GAME_ID", gameId)
    .WaitFor(hungrygame);

builder.AddProject<Projects.foolhearty>("foolhearty")
    .WithEnvironment("SERVER", hungrygame.GetEndpoint("http"))
    .WithEnvironment("PLAY_STYLE", "Foolhearty")
    .WithEnvironment("GAME_ID", gameId)
    .WaitFor(hungrygame);

builder.AddProject<Projects.foolhearty>("foolhearty2")
    .WithEnvironment("SERVER", hungrygame.GetEndpoint("http"))
    .WithEnvironment("PLAY_STYLE", "Foolhearty")
    .WithEnvironment("GAME_ID", gameId)
    .WaitFor(hungrygame);

builder.AddProject<Projects.foolhearty>("smartypants")
    .WithEnvironment("SERVER", hungrygame.GetEndpoint("http"))
    .WithEnvironment("PLAY_STYLE", "SmartyPants")
    .WithEnvironment("GAME_ID", gameId)
    .WaitFor(hungrygame);
```

Remove `BOARD_HEIGHT`, `BOARD_WIDTH` environment variables from the `hungrygame` project — board size is now set per-game at creation time via the lobby.

- [ ] **Step 2: Build AppHost**

```bash
dotnet build HungryGame.AppHost
```

- [ ] **Step 3: Commit**

```bash
git add HungryGame.AppHost/AppHost.cs
git commit -m "feat: add GAME_ID parameter to AppHost for bot clients"
```

---

## Task 18: Full integration smoke-test and final commit

- [ ] **Step 1: Build entire solution**

```bash
dotnet build HungryGame.sln
```

Expected: 0 errors.

- [ ] **Step 2: Run all tests**

```bash
dotnet test HungryTests
```

Expected: All pass.

- [ ] **Step 3: Smoke-test manually**

```bash
dotnet run --project HungryGame
```

1. Open `http://localhost:5000` — see the lobby (empty)
2. Click **+ Create Game** — fill in name + board size — click **Create & Go**
3. Verify you land on `/game/{id}` and see the game in Joining state
4. Open the lobby in another tab — verify the game card appears under "Games to Join"
5. Click **Launch Game** — verify board appears and game transitions to Eating
6. Verify the lobby card moves to "In Progress"
7. Open `/scalar/v1` — verify the new `POST /games` and `GET /game/{id}/...` routes appear

- [ ] **Step 4: Final commit**

```bash
git add -A
git commit -m "feat: multi-game lobby - complete implementation"
```
