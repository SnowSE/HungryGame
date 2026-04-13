using HungryGame;
using Microsoft.AspNetCore.RateLimiting;
using Prometheus;
using Scalar.AspNetCore;
using System.Threading.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
var requestErrorCount = 0L;

// Rate Limiting Configuration
var permitLimit = builder.Configuration.GetValue<int>("RateLimit:PermitLimit", 100);
var windowSeconds = builder.Configuration.GetValue<int>("RateLimit:WindowSeconds", 1);

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter(policyName: "fixed", options =>
    {
        options.PermitLimit = permitLimit;
        options.Window = TimeSpan.FromSeconds(windowSeconds);
        options.QueueProcessingOrder = QueueProcessingOrder.OldestFirst;
        options.QueueLimit = 0;
    });
});

// Add services to the container.
builder.Services.AddRazorPages();
builder.Services.AddServerSideBlazor();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddSingleton<IRandomService, SystemRandomService>();
builder.Services.AddSingleton<AdminTokenService>();
builder.Services.AddSingleton<IGameIdStrategy, ShortRandomIdStrategy>();
builder.Services.AddSingleton<GameRegistry>();
builder.Services.AddHostedService<GameCleanupService>();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddCors();
builder.Services.AddOpenApi();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<GameExceptionHandler>();

var app = builder.Build();

app.MapDefaultEndpoints();

//Path base is needed for running behind a reverse proxy, otherwise the app will not be able to find the static files
var pathBase = builder.Configuration["PATH_BASE"];
app.UsePathBase(pathBase);

// Configure the HTTP request pipeline.
app.UseExceptionHandler();

//Prometheus
app.UseMetricServer();
app.UseHttpMetrics();

app.UseCors(builder =>
{
    builder
    .AllowAnyOrigin()
    .AllowAnyMethod()
    .AllowAnyHeader();
});

app.UseStaticFiles();

//THROW_ERRORS middleware
app.Use(async (context, next) =>
{
    var logger = app.Services.GetRequiredService<ILogger<Program>>();
    if (app.Configuration["THROW_ERRORS"] == "true")
    {
        Interlocked.Increment(ref requestErrorCount);
        if (Interlocked.Read(ref requestErrorCount) % 4 == 0)
        {
            logger.LogInformation("THROW_ERRORS enabled...every 4th request dies.");
            context.Response.StatusCode = 500;
            await context.Response.WriteAsync("Every 4th request fails!");
            return;
        }
    }
    await next();
});

app.UseRouting();
app.UseRateLimiter();
app.MapBlazorHub();
app.MapOpenApi();
app.MapScalarApiReference(options =>
{
    options.Title = "Hungry Game API";
    options.WithDefaultHttpClient(ScalarTarget.CSharp, ScalarClient.HttpClient);
});

app.MapFallbackToPage("/_Host");

// Helper local functions
static GameInstance? ResolveGame(string id, GameRegistry registry) =>
    registry.GetGame(id);

static bool IsAuthorized(GameInstance instance, string? creatorToken, AdminTokenService adminTokens, string? adminToken) =>
    adminTokens.IsValid(adminToken) || instance.CreatorToken == creatorToken;

// Lobby endpoints
app.MapGet("games", (GameRegistry registry) =>
{
    return registry.AllGames().Select(i => new
    {
        i.Id,
        i.Name,
        State = i.Game.CurrentGameState.ToString(),
        PlayerCount = i.Game.PlayerCount,
        i.Game.MaxRows,
        i.Game.MaxCols,
        i.CreatedAt,
        i.CompletedAt,
        WinnerName = i.Game.IsGameOver
            ? i.Game.GetPlayersByGameOverRank().FirstOrDefault()?.Name
            : null
    });
}).RequireRateLimiting("fixed");

app.MapPost("games", (CreateGameRequest req, GameRegistry registry, AdminTokenService adminTokens) =>
{
    bool isAdmin = adminTokens.IsValid(req.AdminToken);
    const int MinRows = 1;
    const int MinCols = 1;
    const int MaxUserRows = 100;
    const int MaxUserCols = 150;

    if (req.NumRows < MinRows || req.NumCols < MinCols)
        return Results.BadRequest($"Board size must be at least {MinRows}x{MinCols}.");

    if (!isAdmin && (req.NumRows > MaxUserRows || req.NumCols > MaxUserCols))
        return Results.BadRequest($"Board size capped at {MaxUserRows}x{MaxUserCols} for user-created games.");

    if (string.IsNullOrWhiteSpace(req.Name))
        return Results.BadRequest("Game name is required.");

    if (req.IsTimed && (!req.TimeLimitMinutes.HasValue || req.TimeLimitMinutes.Value < 1))
        return Results.BadRequest("Timed games require a positive time limit in minutes.");

    var instance = registry.CreateGame(
        req.Name.Trim(),
        req.CreatorToken,
        req.NumRows,
        req.NumCols,
        req.IsTimed,
        req.TimeLimitMinutes);

    return Results.Ok(new { instance.Id, instance.Name });
}).RequireRateLimiting("fixed");

// Game-scoped endpoints
app.MapGet("game/{id}/join", (string id, string? userName, string? playerName, GameRegistry registry) =>
{
    var instance = ResolveGame(id, registry);
    if (instance == null) return Results.NotFound();
    var name = userName ?? playerName ?? throw new ArgumentNullException("userName");
    return Results.Ok(instance.Game.JoinPlayer(name));
}).RequireRateLimiting("fixed");

app.MapGet("game/{id}/move/{dir}", (string id, string dir, string token, GameRegistry registry) =>
{
    var instance = ResolveGame(id, registry);
    if (instance == null) return Results.NotFound();
    if (!Enum.TryParse<Direction>(dir, ignoreCase: true, out var direction))
        return Results.BadRequest("Unknown direction");
    return Results.Ok(instance.Game.Move(token, direction));
}).RequireRateLimiting("fixed");

app.MapGet("game/{id}/board", (string id, GameRegistry registry) =>
{
    var instance = ResolveGame(id, registry);
    if (instance == null) return Results.NotFound();
    return Results.Ok(instance.Game.GetBoardState());
}).RequireRateLimiting("fixed");

app.MapGet("game/{id}/players", (string id, GameRegistry registry) =>
{
    var instance = ResolveGame(id, registry);
    if (instance == null) return Results.NotFound();
    return Results.Ok(instance.Game.GetPlayersByScoreDescending()
        .Select(p => new { p.Name, p.Id, p.Score }));
}).RequireRateLimiting("fixed");

app.MapGet("game/{id}/state", (string id, GameRegistry registry) =>
{
    var instance = ResolveGame(id, registry);
    if (instance == null) return Results.NotFound();
    return Results.Ok(instance.Game.CurrentGameState.ToString());
}).RequireRateLimiting("fixed");

app.MapGet("game/{id}/start", (string id, string? creatorToken, string? adminToken,
    GameRegistry registry, AdminTokenService adminTokens) =>
{
    var instance = ResolveGame(id, registry);
    if (instance == null) return Results.NotFound();
    if (!IsAuthorized(instance, creatorToken, adminTokens, adminToken))
        return Results.Unauthorized();
    instance.Game.StartGame();
    return Results.Ok();
}).RequireRateLimiting("fixed");

app.MapGet("game/{id}/reset", (string id, string? creatorToken, string? adminToken,
    GameRegistry registry, AdminTokenService adminTokens) =>
{
    var instance = ResolveGame(id, registry);
    if (instance == null) return Results.NotFound();
    if (!IsAuthorized(instance, creatorToken, adminTokens, adminToken))
        return Results.Unauthorized();
    instance.Game.ResetGame();
    return Results.Ok();
}).RequireRateLimiting("fixed");

app.MapPost("game/{id}/admin/boot", (string id, BootRequest req,
    GameRegistry registry, AdminTokenService adminTokens) =>
{
    var instance = ResolveGame(id, registry);
    if (instance == null) return Results.NotFound();
    if (!IsAuthorized(instance, req.CreatorToken, adminTokens, req.AdminToken))
        return Results.Unauthorized();
    instance.Game.BootPlayer(req.PlayerId);
    return Results.Ok();
}).RequireRateLimiting("fixed");

app.MapPost("game/{id}/admin/clear-players", (string id, AuthRequest req,
    GameRegistry registry, AdminTokenService adminTokens) =>
{
    var instance = ResolveGame(id, registry);
    if (instance == null) return Results.NotFound();
    if (!IsAuthorized(instance, req.CreatorToken, adminTokens, req.AdminToken))
        return Results.Unauthorized();
    instance.Game.ClearAllPlayers();
    return Results.Ok();
}).RequireRateLimiting("fixed");

// Global admin auth
app.MapPost("admin/login", (AdminLoginRequest req, AdminTokenService adminTokens) =>
{
    var token = adminTokens.Login(req.Password);
    if (token == null) return Results.Unauthorized();
    return Results.Ok(token);
}).RequireRateLimiting("fixed");

app.MapPost("admin/logout", (AdminLogoutRequest req, AdminTokenService adminTokens) =>
{
    adminTokens.Logout(req.AdminToken);
    return Results.Ok();
}).RequireRateLimiting("fixed");

app.Run();

public partial class Program
{
}
