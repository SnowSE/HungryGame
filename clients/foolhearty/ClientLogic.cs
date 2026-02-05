using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using System.Reflection;

namespace foolhearty
{
    public class ClientLogic : IHostedService
    {
        private readonly IConfiguration config;
        private readonly ILogger<ClientLogic> logger;
        private readonly IHostApplicationLifetime appLifetime;
        private readonly IServiceProvider serviceProvider;
        private CancellationTokenSource? _cancellationTokenSource;

        private Task? _applicationTask;
        private int? _exitCode;

        public ClientLogic(IConfiguration config, ILogger<ClientLogic> logger, IHostApplicationLifetime appLifetime, IServiceProvider serviceProvider)
        {
            this.config = config ?? throw new ArgumentNullException(nameof(config));
            this.logger = logger ?? throw new ArgumentNullException(nameof(logger));
            this.appLifetime = appLifetime ?? throw new ArgumentNullException(nameof(appLifetime));
            this.serviceProvider = serviceProvider;
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
                        var playerName = "foolhearty." + (config["PLAY_STYLE"] ?? "Foolhearty");
                        logger.LogInformation("playerName {playerName}", playerName);
                        Type playerType = Assembly.GetExecutingAssembly().GetType(playerName) ?? throw new PlayerNotFoundException(playerName);
                        var playerLogic = (serviceProvider.GetService(playerType) as IPlayerLogic) ?? throw new Exception("Unable to resolve player logic");

                        await playerLogic.JoinGameAsync();
                        await playerLogic.PlayAsync(_cancellationTokenSource);

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

    public record Location(int row, int column);
    public record RedactedPlayer(int id, string name, int score);
    public record Cell(Location location, bool isPillAvailable, RedactedPlayer occupiedBy);
}