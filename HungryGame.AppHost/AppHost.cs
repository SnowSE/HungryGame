using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var secretCode = builder.AddParameter("secretCode", secret: true);
var massivePlayerCount = builder.AddParameter("massivePlayerCount", "5");
var gameId = builder.AddParameter("gameId", secret: false);

var appServiceEnvironment = builder.AddAzureAppServiceEnvironment("HungryGame-ASE");

var hungrygame = builder.AddProject<Projects.HungryGame>("hungrygame")
    .WithEnvironment("SECRET_CODE", secretCode)
    .WithExternalHttpEndpoints()
    .WithComputeEnvironment(appServiceEnvironment);

if (!builder.ExecutionContext.IsPublishMode)
{
    builder.AddProject<Projects.massive>("massive")
        .WithEnvironment("SERVER", hungrygame.GetEndpoint("http"))
        .WithEnvironment("CLIENT_COUNT", massivePlayerCount)
        .WithEnvironment("GAME_ID", gameId)
        .WaitFor(hungrygame);

    builder.AddProject<Projects.foolhearty>("foolhearty")
        .WithEnvironment("SERVER", hungrygame.GetEndpoint("http"))
        .WithEnvironment("PLAY_STYLE", "Foolhearty")
        .WithEnvironment("GAME_ID", gameId)
        .WaitFor(hungrygame);

    builder.AddProject<Projects.foolhearty>("foolhearty2")
        .WithEnvironment("SERVER", hungrygame.GetEndpoint("http"))
        .WithEnvironment("PLAY_STYLE", "Foolhearty")
        .WithEnvironment("GAME_ID", gameId)
        .WaitFor(hungrygame);

    builder.AddProject<Projects.foolhearty>("smartypants")
        .WithEnvironment("SERVER", hungrygame.GetEndpoint("http"))
        .WithEnvironment("PLAY_STYLE", "SmartyPants")
        .WithEnvironment("GAME_ID", gameId)
        .WaitFor(hungrygame);
}

builder.Build().Run();
