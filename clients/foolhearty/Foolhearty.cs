using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using System.Text.Json;

namespace foolhearty;

public class Foolhearty : BasePlayerLogic
{
    private readonly ILogger<Foolhearty> logger;
    private int errorCount = 0;
    private int sleepTime = 2_000;

    public Foolhearty(ILogger<Foolhearty> logger, IConfiguration config) : base(config)
    {
        this.logger = logger;
    }

    public override string PlayerName => config["NAME"] ?? $"Client {DateTime.Now:HH.mm.ffff}";

    public override async Task JoinGameAsync()
    {
        _gameId = config["GAME_ID"] ?? throw new InvalidOperationException(
            "GAME_ID environment variable is required. Set it to the ID of the game to join.");
        string fileName = $"connectionInfo_{PlayerName}.txt";
        if (File.Exists(fileName))
        {
            var parts = File.ReadAllText(fileName).Split('|');
            url = parts[0];
            token = parts[1];
        }
        else
        {
            await base.JoinGameAsync();
            File.WriteAllText(fileName, $"{url}|{token}");
        }
    }

    public override async Task PlayAsync(CancellationTokenSource cancellationTokenSource)
    {
        await waitForGameToStart(cancellationTokenSource.Token);
        logger.LogInformation("Game started - making moves.");
        Location currentLocation = new Location(0, 0);
        while (!cancellationTokenSource.IsCancellationRequested)
        {
            var board = await getBoardAsync();
            //logger.LogInformation("Got board state; {cellsWithPills} cells with pills remain", board.Count(c => c.isPillAvailable));
            try
            {
                var destination = acquireTarget(currentLocation, board);
                var direction = inferDirection(currentLocation, destination);
            MOVE:
                var moveResultString = await httpClient.GetStringAsync($"{url}/game/{_gameId}/move/{direction}?token={token}");
                var moveResultJson = JsonDocument.Parse(moveResultString).RootElement;
                var currentRow = moveResultJson.GetProperty("newLocation").GetProperty("row").GetInt32();
                var currentCol = moveResultJson.GetProperty("newLocation").GetProperty("column").GetInt32();
                var newLocation = new Location(currentRow, currentCol);
                if (newLocation == currentLocation)//we didn't move
                {
                    var newDirection = tryNextDirection(direction, board, newLocation);
                    logger.LogInformation("Moving {lastDirection} didn't work, trying {newDirection} instead", direction, newDirection);
                    direction = newDirection;
                    if (cancellationTokenSource.IsCancellationRequested)// || await checkIfGameOver())
                    {
                        break;
                    }

                    goto MOVE;
                }
                else
                {
                    currentLocation = new Location(currentRow, currentCol);
                }

                //if (await checkIfGameOver())
                //{
                //    Console.WriteLine("Game over.  Waiting for next game.");
                //    while (true)
                //    {
                //        await Task.Delay(sleepTime, cancellationTokenSource.Token);
                //        errorCount++;
                //        sleepTime += 500;
                //        if (errorCount > 200)
                //        {
                //            errorCount = 0;
                //            return;
                //        }
                //    }
                //}
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Uh oh...");
                await Task.Delay(sleepTime, cancellationTokenSource.Token);
                //errorCount++;
                //sleepTime += 500;
                //if (errorCount > 200)
                //{
                //    return;
                //}
            }
        }
    }
}
