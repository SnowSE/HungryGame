using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var boardHeight = builder.AddParameter("boardHeight", "20");
var boardWidth = builder.AddParameter("boardWidth", "30");
var secretCode = builder.AddParameter("secretCode", secret: true);
var massivePlayerCount = builder.AddParameter("massivePlayerCount", "5");

var hungrygame = builder.AddProject<Projects.HungryGame>("hungrygame")
    .WithEnvironment("BOARD_HEIGHT", boardHeight)
    .WithEnvironment("BOARD_WIDTH", boardWidth)
    .WithEnvironment("SECRET_CODE", secretCode);

builder.AddProject<Projects.massive>("massive")
.WithEnvironment("SERVER", hungrygame.GetEndpoint("http"))
.WithEnvironment("CLIENT_COUNT", massivePlayerCount)
.WaitFor(hungrygame);

builder.AddProject<Projects.foolhearty>("foolhearty")
    .WithEnvironment("SERVER", hungrygame.GetEndpoint("http"))
    .WithEnvironment("PLAY_STYLE", "Foolhearty")
    .WaitFor(hungrygame);

builder.AddProject<Projects.foolhearty>("foolhearty2")
    .WithEnvironment("SERVER", hungrygame.GetEndpoint("http"))
    .WithEnvironment("PLAY_STYLE", "Foolhearty")
    .WaitFor(hungrygame);

builder.AddProject<Projects.foolhearty>("smartypants")
    .WithEnvironment("SERVER", hungrygame.GetEndpoint("http"))
    .WithEnvironment("PLAY_STYLE", "SmartyPants")
    .WaitFor(hungrygame);

builder.Build().Run();
