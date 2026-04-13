namespace HungryGame;

public class GameCleanupService : BackgroundService
{
    private readonly GameRegistry _registry;
    private readonly ILogger<GameCleanupService> _log;
    private readonly TimeProvider _timeProvider;
    private static readonly TimeSpan Retention = TimeSpan.FromDays(30);
    private static readonly TimeSpan Interval = TimeSpan.FromHours(1);

    public GameCleanupService(GameRegistry registry, ILogger<GameCleanupService> log, TimeProvider timeProvider)
    {
        _registry = registry;
        _log = log;
        _timeProvider = timeProvider;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                await DelayAsync(Interval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }

            var cutoff = _timeProvider.GetUtcNow() - Retention;
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

    private Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
    {
        if (delay <= TimeSpan.Zero)
        {
            return Task.CompletedTask;
        }

        var completion = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        ITimer? timer = null;
        CancellationTokenRegistration cancellationRegistration = default;

        timer = _timeProvider.CreateTimer(_ =>
        {
            cancellationRegistration.Dispose();
            timer?.Dispose();
            completion.TrySetResult();
        }, null, delay, Timeout.InfiniteTimeSpan);

        if (cancellationToken.CanBeCanceled)
        {
            cancellationRegistration = cancellationToken.Register(() =>
            {
                timer?.Dispose();
                completion.TrySetCanceled(cancellationToken);
            });
        }

        return completion.Task;
    }
}
