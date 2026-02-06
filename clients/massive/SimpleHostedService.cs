using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace massive;

public class SimpleHostedService : IHostedService
{
    private readonly IConfiguration config;
    private readonly ILogger<SimpleHostedService> logger;
    private readonly IHostApplicationLifetime appLifetime;
    private readonly IServiceProvider serviceProvider;
    private readonly MassiveClient client;
    private CancellationTokenSource? _cancellationTokenSource;
    private string DEFAULT_CLIENT_COUNT = "25";

    private Task? _applicationTask;
    private int? _exitCode;

    public SimpleHostedService(IConfiguration config, ILogger<SimpleHostedService> logger, IHostApplicationLifetime appLifetime,
        IServiceProvider serviceProvider, MassiveClient client)
    {
        this.config = config ?? throw new ArgumentNullException(nameof(config));
        this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
        this.appLifetime = appLifetime ?? throw new ArgumentNullException(nameof(appLifetime));
        this.serviceProvider = serviceProvider ?? throw new ArgumentNullException(nameof(serviceProvider));
        this.client = client;
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogDebug($"Starting with arguments: {string.Join(" ", Environment.GetCommandLineArgs())}");


        appLifetime.ApplicationStarted.Register(() =>
        {
            logger.LogDebug("Application has started");
            _cancellationTokenSource = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _applicationTask = Task.Run(async () =>
            {
                try
                {
                    await client.Run(int.Parse(config["CLIENT_COUNT"] ?? DEFAULT_CLIENT_COUNT), _cancellationTokenSource.Token);

                    _exitCode = 0;
                }
                catch (TaskCanceledException)
                {
                    // This means the application is shutting down, so just swallow this exception
                }
                catch (Exception ex)
                {
                    logger.LogError(ex, "Unhandled exception!");
                    _exitCode = 1;
                }
                finally
                {
                    // Stop the application once the work is done
                    appLifetime.StopApplication();
                }
            });
        });

        appLifetime.ApplicationStopping.Register(() =>
        {
            logger.LogDebug("Application is stopping");
            _cancellationTokenSource?.Cancel();
        });

        return Task.CompletedTask;
    }
    public async Task StopAsync(CancellationToken cancellationToken)
    {
        // Wait for the application logic to fully complete any cleanup tasks.
        // Note that this relies on the cancellation token to be properly used in the application.
        if (_applicationTask != null)
        {
            await _applicationTask;
        }

        logger.LogDebug($"Exiting with return code: {_exitCode}");

        // Exit code may be null if the user cancelled via Ctrl+C/SIGTERM
        Environment.ExitCode = _exitCode.GetValueOrDefault(-1);
    }
}