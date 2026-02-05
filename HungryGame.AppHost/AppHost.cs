using Aspire.Hosting;

var builder = DistributedApplication.CreateBuilder(args);

var hungrygame = builder.AddProject<Projects.HungryGame>("hungrygame");

//builder.AddProject<Projects.massive>("massive")
//    .WithEnvironment("SERVER", hungrygame.GetEndpoint("http"))
//    .WaitFor(hungrygame);

builder.AddProject<Projects.foolhearty>("foolhearty")
    .WithEnvironment("SERVER", hungrygame.GetEndpoint("http"))
    .WithEnvironment("PLAY_STYLE", "Foolhearty")
    .WaitFor(hungrygame);

builder.AddProject<Projects.foolhearty>("smartypants")
    .WithEnvironment("SERVER", hungrygame.GetEndpoint("http"))
    .WithEnvironment("PLAY_STYLE", "SmartyPants")
    .WaitFor(hungrygame);

//builder.AddProject<Projects.Viewer>("viewer");

builder.Build().Run();
