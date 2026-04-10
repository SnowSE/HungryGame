using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net.Http.Json;
using System.Text.Json;

namespace massive;

public class MassiveClient
{
    private readonly ILogger<MassiveClient> logger;
    private readonly IConfiguration config;
    private readonly ILogger<Player> playerLogger;
    private readonly SocketsHttpHandler socketsHandler;
    private readonly string url;
    private readonly string _gameId;
    private CancellationToken cancellationToken;

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

    public async Task Run(int numClients, CancellationToken cancellationToken)
    {
        this.cancellationToken = cancellationToken;

        httpClient = new HttpClient(socketsHandler);

        logger.LogInformation($"Creating {numClients} players");
        var players = (from i in Enumerable.Range(0, numClients)
                       let name = $"Massive_{i:0000}"
                       select new Player(config, name, socketsHandler, url, _gameId, playerLogger)).ToList();

        var gameStartTask = players.First().WaitForGameToStart(cancellationToken);

        logger.LogInformation("Joining players to game");
        //Parallel.ForEach(players, async (p) => await p.JoinGameAsync());
        foreach (var p in players)
        {
            await p.JoinGameAsync();
        }

        logger.LogInformation("Waiting for game to start");
        await gameStartTask;

        var timer = new Timer((a) => getBoard(a), null, 0, 1_000);
        await getBoard(this);

        logger.LogInformation("Making moves");
        //Parallel.ForEach(players, async p => await p.MakeMoves(cancellationToken));
        var playerTasks = players.Select(p => MakeMoves(p, cancellationToken)).ToArray();

        logger.LogInformation("Waiting for players to finish");
        Task.WaitAll(playerTasks);
        logger.LogInformation("massive client all done.");
        timer.Change(Timeout.Infinite, Timeout.Infinite);
    }

    private async Task MakeMoves(Player p, CancellationToken cancellationToken)
    {
        try
        {
            var direction = "right";
            var moveResult = await p.Move(direction);
            var startingLocation = moveResult.newLocation;
            var movesMade = 0;

            while (cancellationToken.IsCancellationRequested is false)
            {
                Task<MoveResult> moveTask = null;

                if ((direction == "right" && moveResult.newLocation.column + movesMade < maxCol) ||
                    (direction == "left" && moveResult.newLocation.column - movesMade > 0))
                {
                    moveTask = p.Move(direction);
                    movesMade++;
                    continue;
                }
                if (moveTask != null)
                {
                    moveResult = await moveTask;
                }

                if (moveResult.newLocation.row > maxRow / 2)
                {
                    moveResult = await p.Move("down");
                }
                else
                {
                    moveResult = await p.Move("up");
                }

                direction = direction switch
                {
                    "right" => "left",
                    _ => "right"
                };
                movesMade = 0;
            }
        }
        catch (Exception ex)
        {

        }
    }

    private Dictionary<Location, Cell> map;
    private List<Cell> board = new();
    private IEnumerable<PlayerInfo> players;
    private HttpClient httpClient;
    private string gameState;
    private int maxCol;
    private int maxRow;

    private async Task getBoard(object _)
    {
        var newBoard = (await getBoardAsync()).ToList();
        var newMap = new Dictionary<Location, Cell>(newBoard.Select(c => new KeyValuePair<Location, Cell>(c.location, c)));
        if (board.Count != newBoard.Count)
        {
            Interlocked.Exchange(ref maxCol, newBoard.Max(c => c.location.column));
            Interlocked.Exchange(ref maxRow, newBoard.Max(c => c.location.row));
        }

        Interlocked.Exchange(ref board, newBoard);
        Interlocked.Exchange(ref map, newMap);

        var newPlayers = await httpClient.GetFromJsonAsync<IEnumerable<PlayerInfo>>($"{url}/game/{_gameId}/players");
        Interlocked.Exchange(ref players, newPlayers);

        var newGameState = await httpClient.GetStringAsync($"{url}/game/{_gameId}/state");
        Interlocked.Exchange(ref gameState, newGameState);
        logger.LogInformation("UPDATED BOARD");
    }
    protected async Task<List<Cell>> getBoardAsync()
    {
        var boardString = await new HttpClient(socketsHandler).GetStringAsync($"{url}/game/{_gameId}/board");
        return JsonSerializer.Deserialize<IEnumerable<Cell>>(boardString)?.ToList() ?? throw new Exception("Unable to get board info");
    }
}

public class Player
{
    protected readonly HttpClient httpClient;
    protected readonly string url;
    private readonly string gameId;
    internal string? token;
    private readonly IConfiguration config;
    private ILogger<Player> logger;

    public Player(IConfiguration config, string name, SocketsHttpHandler socketsHandler, string url, string gameId, ILogger<Player> logger)
    {
        this.config = config;
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

    protected async Task<bool> checkIfGameOver()
    {
        return (await httpClient.GetStringAsync($"{url}/game/{gameId}/state")) == "GameOver";
    }

    protected virtual private string tryNextDirection(string direction) => direction switch
    {
        "down" => "left",
        "left" => "up",
        "up" => "right",
        "right" => "down",
        _ => throw new NotImplementedException()
    };

    protected virtual Location acquireTarget(Location curLocation, List<Cell> board)
    {
        var max = new Location(int.MaxValue, int.MaxValue);

        Location closest = findClosestPillToEat(curLocation, board, max);

        if (closest == max)//e.g. didn't find a pill to eat...look for another player
        {
            closest = findNearestPlayerToAttack(curLocation, board, max, closest);
        }

        return closest;
    }

    protected virtual Location findClosestPillToEat(Location curLocation, List<Cell> board, Location max)
    {
        var closest = max;
        var minDistance = double.MaxValue;

        foreach (var cell in board)
        {
            if (!cell.isPillAvailable)
            {
                continue;
            }
            var a = curLocation.row - cell.location.row;
            var b = curLocation.column - cell.location.column;
            var newDistance = Math.Sqrt((a * a) + (b * b));
            if (newDistance < minDistance)
            {
                minDistance = newDistance;
                closest = cell.location;
            }
        }

        return closest;
    }

    protected virtual Location findNearestPlayerToAttack(Location curLocation, List<Cell> board, Location max, Location closest)
    {
        var minScore = int.MaxValue;
        foreach (var cell in board)
        {
            if (cell.occupiedBy == null || cell.location == curLocation)
            {
                continue;
            }
            if (cell.occupiedBy.score < minScore)
            {
                minScore = cell.occupiedBy.score;
                closest = cell.location;
            }
        }

        return closest;
    }

    protected static string inferDirection(Location currentLocation, Location destination)
    {
        if (currentLocation.row < destination.row)
        {
            return "down";
        }
        else if (currentLocation.row > destination.row)
        {
            return "up";
        }

        if (currentLocation.column < destination.column)
        {
            return "right";
        }
        return "left";
    }
    public async Task WaitForGameToStart(CancellationToken cancellationToken)
    {
        var gameState = await httpClient.GetStringAsync($"{url}/game/{gameId}/state");
        while (gameState == "Joining" || gameState == "GameOver")
        {
            await Task.Delay(2_000, cancellationToken);
            gameState = await httpClient.GetStringAsync($"{url}/game/{gameId}/state", cancellationToken);
        }
    }

    protected async Task<List<Cell>> getBoardAsync()
    {
        var boardString = await httpClient.GetStringAsync($"{url}/game/{gameId}/board");
        return JsonSerializer.Deserialize<IEnumerable<Cell>>(boardString)?.ToList() ?? throw new Exception("Unable to deserialize board");
    }

    public async Task<MoveResult> Move(string direction)
    {
        logger.LogInformation($"{PlayerName} moving {direction}");
        try
        {
            return await httpClient.GetFromJsonAsync<MoveResult>($"{url}/game/{gameId}/move/{direction}?token={token}");
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Error moving.");
            logger.LogInformation($"Re-joining game to get a new token for {PlayerName}...");
            await JoinGameAsync();
            return await httpClient.GetFromJsonAsync<MoveResult>($"{url}/game/{gameId}/move/{direction}?token={token}");
        }
    }
}


public record MoveResult
{
    public Location newLocation { get; set; }
    public bool ateAPill { get; set; }
}
public record Location(int row, int column);
public record RedactedPlayer(int id, string name, int score);
public record Cell(Location location, bool isPillAvailable, RedactedPlayer occupiedBy);

public class PlayerInfo
{
    public string Name { get; set; }
    public int Id { get; set; }
    public int Score { get; set; }
}