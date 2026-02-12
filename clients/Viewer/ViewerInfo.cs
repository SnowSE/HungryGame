using System.Net.Http.Json;

namespace Viewer;

public class ViewerInfo
{
    private readonly HttpClient httpClient;
    private readonly IConfiguration config;
    private readonly ILogger<ViewerInfo> logger;
    private readonly Timer timer;
    private readonly string server;
    private List<PlayerInfo> players = new();
    private string gameState = String.Empty;
    public event EventHandler? UpdateTick;

    public string Server => server;

    public ViewerInfo(IConfiguration config, ILogger<ViewerInfo> logger)
    {
        logger.LogInformation("Instantiating ViewerInfo");
        this.httpClient = new HttpClient();
        this.config = config;
        this.logger = logger;
        timer = new Timer(timerTick, null, 0, 1_000);
        server = config["SERVER"] ?? "https://hungrygame.azurewebsites.net";
    }

    private async void timerTick(object? state)
    {
        logger.LogInformation("timerTick() start");

        var newPlayers = (await httpClient.GetFromJsonAsync<IEnumerable<PlayerInfo>>($"{server}/players")).ToList();
        Interlocked.Exchange(ref players, newPlayers);

        var newGameState = await httpClient.GetStringAsync($"{server}/state");
        Interlocked.Exchange(ref gameState, newGameState);

        logger.LogInformation("timerTick() end");
        UpdateTick?.Invoke(this, EventArgs.Empty);
    }

    public bool IsGameStarted => gameState != "Joining" && gameState != "GameOver";
    public string CurrentGameState => gameState;
    public DateTime? GameEndsOn { get; private set; }
    public TimeSpan TimeRemaining => (GameEndsOn ?? DateTime.Now) - DateTime.Now;
    public List<PlayerInfo> GetPlayersByScoreDescending() => players.OrderByDescending(p => p.Score).ToList();
}

public class PlayerInfo
{
    public string Name { get; set; }
    public int Id { get; set; }
    public int Score { get; set; }
}
