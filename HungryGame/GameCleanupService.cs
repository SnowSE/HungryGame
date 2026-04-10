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
