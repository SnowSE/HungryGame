using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Net;
using System.Net.Http.Json;

namespace foolhearty;

public class SmartyPants : BasePlayerLogic
{
    private readonly ILogger<SmartyPants> logger;
    private Dictionary<Location, Cell> map;
    private List<Cell> board;
    private IEnumerable<PlayerInfo> players;
    private string gameState;
    private int rateLimitCount = 0;
    private int requestCount = 0;
    private DateTime lastRateLimitCheck = DateTime.UtcNow;
    private int currentDelayMs = 0;

    public SmartyPants(IConfiguration config, ILogger<SmartyPants> logger) : base(config)
    {
        this.logger = logger;
    }

    private void AdjustThrottling()
    {
        var elapsed = DateTime.UtcNow - lastRateLimitCheck;
        if (elapsed.TotalSeconds >= 10)
        {
            var rateLimitRate = requestCount > 0 ? (double)rateLimitCount / requestCount : 0;
            
            if (rateLimitRate > 0.1) // More than 10% rate limited
            {
                currentDelayMs = Math.Min(currentDelayMs + 50, 500);
                logger.LogWarning("High rate limit rate ({rate:P}). Increasing delay to {delay}ms", rateLimitRate, currentDelayMs);
            }
            else if (rateLimitRate < 0.02 && currentDelayMs > 0) // Less than 2% rate limited
            {
                currentDelayMs = Math.Max(currentDelayMs - 25, 0);
                logger.LogInformation("Low rate limit rate ({rate:P}). Decreasing delay to {delay}ms", rateLimitRate, currentDelayMs);
            }
            
            rateLimitCount = 0;
            requestCount = 0;
            lastRateLimitCheck = DateTime.UtcNow;
        }
    }

    private async Task<T> ExecuteWithRetry<T>(Func<Task<T>> action, int maxRetries = 3)
    {
        AdjustThrottling();
        
        if (currentDelayMs > 0)
        {
            await Task.Delay(currentDelayMs);
        }

        Interlocked.Increment(ref requestCount);
        
        int retryCount = 0;
        while (true)
        {
            try
            {
                return await action();
            }
            catch (HttpRequestException ex) when (ex.StatusCode == HttpStatusCode.TooManyRequests && retryCount < maxRetries)
            {
                Interlocked.Increment(ref rateLimitCount);
                retryCount++;
                var delay = TimeSpan.FromSeconds(Math.Pow(2, retryCount));
                logger.LogWarning("Rate limited. Waiting {delay} seconds before retry {retryCount}/{maxRetries}", delay.TotalSeconds, retryCount, maxRetries);
                await Task.Delay(delay);
            }
        }
    }

    public override string PlayerName => config["PLAYER_NAME"] ?? "SmartyPants";

    public override async Task PlayAsync(CancellationTokenSource cancellationTokenSource)
    {
        logger.LogInformation("SmartyPants starting to play");

        await waitForGameToStart(cancellationTokenSource.Token);

        var timer = new Timer(getBoard, null, 0, 1_000);
        var lastLocation = new Location(0, 0);
        var direction = "right";
        var moveResult = new MoveResult { newLocation = new Location(0, 0) };
        while (true)
        {
            if (cancellationTokenSource.IsCancellationRequested)
            {
                logger.LogInformation("Cancellation request received!");
                break;
            }

            await refreshBoardAndMap();
            var destination = acquireTarget(moveResult?.newLocation, board);

            if (gameState == "GameOver")
            {
                logger.LogInformation("Game over...wait a bit.");
                await Task.Delay(TimeSpan.FromSeconds(1), cancellationTokenSource.Token);
                continue;
            }

            direction = inferDirection(moveResult?.newLocation, destination);
            moveResult = await ExecuteWithRetry(() => httpClient.GetFromJsonAsync<MoveResult>($"{url}/move/{direction}?token={token}"));

            while (moveResult?.newLocation == lastLocation && !cancellationTokenSource.IsCancellationRequested)//didn't move
            {
                logger.LogInformation($"Didn't move when I went {direction}, trying to go {tryNextDirection(direction)}");
                direction = tryNextDirection(direction);
                moveResult = await ExecuteWithRetry(() => httpClient.GetFromJsonAsync<MoveResult>($"{url}/move/{direction}?token={token}"));
            }

            if (moveResult?.ateAPill == false)
            {
                logger.LogInformation("Didn't eat a pill...keep searching.  Move from {from} to {destination}", moveResult.newLocation, destination);
                moveResult = await moveFromTo(moveResult, destination);
                lastLocation = moveResult?.newLocation;
                logger.LogInformation("   moveResult={moveResult}", moveResult);
                continue;
            }
            var nextLocation = advance(moveResult?.newLocation, direction);
            Task<MoveResult> lastRequest = null;
            while (map.ContainsKey(nextLocation) && map[nextLocation].isPillAvailable)
            {
                logger.LogInformation("In a groove!  Keep going!");
                lastRequest = ExecuteWithRetry(() => httpClient.GetFromJsonAsync<MoveResult>($"{url}/move/{direction}?token={token}"));
                nextLocation = advance(nextLocation, direction);
            }
            if (lastRequest != null)
            {
                logger.LogInformation("Wait for response from most recent request");
                moveResult = await lastRequest;
            }
        }
    }

    protected override Location findNearestPlayerToAttack(Location curLocation, List<Cell> board, Location max, Location closest)
    {
        var myId = map[curLocation].occupiedBy?.id;
        if (myId != null)
        {
            var otherPlayerSum = players.Where(p => p.id != myId.Value).Sum(p => p.score);
            var myScore = players.Single(p => p.id == myId.Value).score;
            if (myScore > otherPlayerSum) //do I have enough points to beat everyone?
            {
                return base.findNearestPlayerToAttack(curLocation, board, max, closest);
            }
        }

        //if I don't have enough points to beat everyone, run away. :)
        var nearestPlayer = base.findNearestPlayerToAttack(curLocation, board, max, closest);
        var columnDelta = nearestPlayer.column - curLocation.column;
        var rowDelta = nearestPlayer.row - curLocation.row;
        return new Location(curLocation.row + (rowDelta * -1), curLocation.column + (columnDelta * -1));
    }

    private async Task refreshBoardAndMap()
    {
        board = await getBoardAsync();
        map = new Dictionary<Location, Cell>(board.Select(c => new KeyValuePair<Location, Cell>(c.location, c)));
    }

    private Location advance(Location? lastLocation, string direction)
    {
        return direction switch
        {
            "left" => lastLocation with { column = lastLocation.column - 1 },
            "right" => lastLocation with { column = lastLocation.column + 1 },
            "up" => lastLocation with { row = lastLocation.row - 1 },
            "down" => lastLocation with { row = lastLocation.row + 1 },
            _ => lastLocation
        };
    }

    private async void getBoard(object _)
    {
        try
        {
            var newBoard = await ExecuteWithRetry(() => getBoardAsync());
            var newMap = new Dictionary<Location, Cell>(newBoard.Select(c => new KeyValuePair<Location, Cell>(c.location, c)));
            Interlocked.Exchange(ref board, newBoard);
            Interlocked.Exchange(ref map, newMap);

            var newPlayers = await ExecuteWithRetry(() => httpClient.GetFromJsonAsync<IEnumerable<PlayerInfo>>($"{url}/players"));
            Interlocked.Exchange(ref players, newPlayers);

            var newGameState = await ExecuteWithRetry(() => httpClient.GetStringAsync($"{url}/state"));
            Interlocked.Exchange(ref gameState, newGameState);
            logger.LogInformation("UPDATED BOARD");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error updating board");
        }
    }

    private async Task<MoveResult> moveFromTo(MoveResult current, Location destination)
    {
        var rowDelta = destination.row - current.newLocation.row;
        var colDelta = destination.column - current.newLocation.column;

        var direction = rowDelta < 0 ? "up" : "down";
        Task<MoveResult> result = null;
        for (int i = 0; i < Math.Abs(rowDelta); i++)
        {
            result = ExecuteWithRetry(() => httpClient.GetFromJsonAsync<MoveResult>($"{url}/move/{direction}?token={token}"));
        }

        direction = colDelta < 0 ? "left" : "right";
        for (int i = 0; i < Math.Abs(colDelta); i++)
        {
            result = ExecuteWithRetry(() => httpClient.GetFromJsonAsync<MoveResult>($"{url}/move/{direction}?token={token}"));
        }

        if (result != null)
        {
            try
            {
                return await result;
            }
            catch { }
        }

        return new MoveResult { newLocation = destination };
    }
}

public record MoveResult
{
    public Location newLocation { get; set; }
    public bool ateAPill { get; set; }
}

public static class Extensions
{
}

public class PlayerInfo
{
    public string name { get; set; }
    public int id { get; set; }
    public int score { get; set; }
}
