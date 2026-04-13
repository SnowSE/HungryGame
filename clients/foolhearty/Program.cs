using foolhearty;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
await Host.CreateDefaultBuilder(args)
    .UseContentRoot(AppContext.BaseDirectory)
    .ConfigureLogging(logging =>
    {

    })
    .ConfigureServices(services =>
    {
        services.AddHostedService<ClientLogic>();
        services.AddTransient<Foolhearty>();
        services.AddTransient<SmartyPants>();
    })
    .RunConsoleAsync();
