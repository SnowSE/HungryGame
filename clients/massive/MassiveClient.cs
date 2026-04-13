using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace massive;

public class MassiveClient
{
    private readonly ILogger<MassiveClient> logger;
    private readonly ILogger<Player> playerLogger;
    private readonly SocketsHttpHandler socketsHandler;
    private readonly string url;
    private readonly string gameId;
    private readonly HttpClient httpClient;
    private CancellationToken cancellationToken;
    private List<Cell> board = new();
    private IReadOnlyList<PlayerInfo> players = Array.Empty<PlayerInfo>();
    private string gameState = string.Empty;
    private int maxCol;
    private int maxRow;

    public MassiveClient(ILogger<MassiveClient> logger, IConfiguration config, ILogger<Player> playerLogger)
    {
        this.logger = logger;
        this.playerLogger = playerLogger;
        socketsHandler = new SocketsHttpHandler();
        url = config["SERVER"] ?? "https://hungrygame.azurewebsites.net";
        gameId = config["GAME_ID"] ?? throw new InvalidOperationException("GAME_ID environment variable is required.");
        httpClient = new HttpClient(socketsHandler);
    }

    public async Task Run(int numClients, CancellationToken cancellationToken)
    {
        this.cancellationToken = cancellationToken;

        logger.LogInformation("Creating {numClients} players", numClients);
        var players = Enumerable.Range(0, numClients)
            .Select(i => new Player($"Massive_{i:0000}", socketsHandler, url, gameId, playerLogger))
            .ToList();

        var gameStartTask = players.First().WaitForGameToStart(cancellationToken);

        logger.LogInformation("Joining players to game");
        foreach (var player in players)
        {
            await player.JoinGameAsync();
        }

        logger.LogInformation("Waiting for game to start");
        await gameStartTask;

        using var refreshCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        using var refreshTimer = new PeriodicTimer(TimeSpan.FromSeconds(1));
        var refreshTask = RefreshBoardLoopAsync(refreshTimer, refreshCts.Token);
        await RefreshBoardAsync();

        logger.LogInformation("Making moves");
        var playerTasks = players.Select(player => MakeMoves(player, cancellationToken)).ToArray();

        logger.LogInformation("Waiting for players to finish");
        await Task.WhenAll(playerTasks);
        logger.LogInformation("massive client all done.");

        refreshCts.Cancel();
        await refreshTask;
    }

    private async Task MakeMoves(Player player, CancellationToken cancellationToken)
    {
        try
        {
            var direction = "right";
            var moveResult = await player.Move(direction);
            var movesMade = 0;

            while (!cancellationToken.IsCancellationRequested)
            {
                if ((direction == "right" && moveResult.newLocation.column + movesMade < maxCol) ||
                    (direction == "left" && moveResult.newLocation.column - movesMade > 0))
                {
                    moveResult = await player.Move(direction);
                    movesMade++;
                    continue;
                }

                moveResult = moveResult.newLocation.row > maxRow / 2
                    ? await player.Move("down")
                    : await player.Move("up");

                direction = direction == "right" ? "left" : "right";
                movesMade = 0;
            }
        }
        catch (OperationCanceledException)
        {
        }
        catch (Exception ex)
        {
            logger.LogDebug(ex, "Move loop ended unexpectedly for a massive client player.");
        }
    }

    private async Task RefreshBoardLoopAsync(PeriodicTimer timer, CancellationToken cancellationToken)
    {
        try
        {
            while (await timer.WaitForNextTickAsync(cancellationToken))
            {
                await RefreshBoardAsync();
            }
        }
        catch (OperationCanceledException)
        {
        }
    }

    private async Task RefreshBoardAsync()
    {
        var newBoard = await GetBoardAsync();
        if (board.Count != newBoard.Count && newBoard.Count > 0)
        {
            Interlocked.Exchange(ref maxCol, newBoard.Max(c => c.location.column));
            Interlocked.Exchange(ref maxRow, newBoard.Max(c => c.location.row));
        }

        Interlocked.Exchange(ref board, newBoard);
        var newPlayers = await GetPlayersAsync();
        Interlocked.Exchange(ref players, newPlayers);

        var newGameState = await httpClient.GetStringAsync($"{url}/game/{gameId}/state", cancellationToken);
        Interlocked.Exchange(ref gameState, newGameState);
        logger.LogInformation("UPDATED BOARD");
    }

    private async Task<List<Cell>> GetBoardAsync()
    {
        var boardString = await httpClient.GetStringAsync($"{url}/game/{gameId}/board", cancellationToken);
        return JsonSerializer.Deserialize<IEnumerable<Cell>>(boardString)?.ToList()
            ?? throw new InvalidOperationException("Unable to get board info");
    }

    private async Task<IReadOnlyList<PlayerInfo>> GetPlayersAsync()
    {
        var result = await httpClient.GetFromJsonAsync<List<PlayerInfo>>(
            $"{url}/game/{gameId}/players",
            cancellationToken);
        return result ?? new List<PlayerInfo>();
    }
}

public class Player
{
    private readonly HttpClient httpClient;
    private readonly string url;
    private readonly string gameId;
    private readonly ILogger<Player> logger;
    internal string? token;

    public Player(string name, SocketsHttpHandler socketsHandler, string url, string gameId, ILogger<Player> logger)
    {
        PlayerName = name;
        httpClient = new HttpClient(socketsHandler);
        this.url = url;
        this.gameId = gameId;
        this.logger = logger;
    }

    public string PlayerName { get; }

    public async Task JoinGameAsync()
    {
        token = await httpClient.GetStringAsync($"{url}/game/{gameId}/join?playerName={PlayerName}");
    }

    public async Task WaitForGameToStart(CancellationToken cancellationToken)
    {
        var gameState = await httpClient.GetStringAsync($"{url}/game/{gameId}/state", cancellationToken);
        while (gameState == "Joining" || gameState == "GameOver")
        {
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            gameState = await httpClient.GetStringAsync($"{url}/game/{gameId}/state", cancellationToken);
        }
    }

    public async Task<MoveResult> Move(string direction)
    {
        logger.LogInformation("{playerName} moving {direction}", PlayerName, direction);
        try
        {
            return await httpClient.GetFromJsonAsync<MoveResult>($"{url}/game/{gameId}/move/{direction}?token={token}")
                ?? throw new InvalidOperationException("Move endpoint returned no payload.");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error moving.");
            logger.LogInformation("Re-joining game to get a new token for {playerName}...", PlayerName);
            await JoinGameAsync();
            return await httpClient.GetFromJsonAsync<MoveResult>($"{url}/game/{gameId}/move/{direction}?token={token}")
                ?? throw new InvalidOperationException("Move endpoint returned no payload after rejoin.");
        }
    }
}

public record MoveResult
{
    public Location newLocation { get; set; } = new(0, 0);
    public bool ateAPill { get; set; }
}

public record Location(int row, int column);
public record RedactedPlayer(int id, string name, int score);
public record Cell(Location location, bool isPillAvailable, RedactedPlayer? occupiedBy);

public class PlayerInfo
{
    public string Name { get; set; } = string.Empty;
    public int Id { get; set; }
    public int Score { get; set; }
}
