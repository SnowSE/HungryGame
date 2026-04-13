using massive;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
await Host.CreateDefaultBuilder(args)
    .UseContentRoot(AppContext.BaseDirectory)
    .ConfigureLogging(logging =>
    {

    })
    .ConfigureServices(services =>
    {
        services.AddHostedService<SimpleHostedService>();
        services.AddSingleton<MassiveClient>();
    })
    .RunConsoleAsync();
