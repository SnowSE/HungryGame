# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

```bash
# Build the solution
dotnet build HungryGame.sln

# Run the main game server
dotnet run --project HungryGame

# Run all tests
dotnet test HungryTests

# Run a single SpecFlow feature file (by name filter)
dotnet test HungryTests --filter "FullyQualifiedName~GameScoring"

# Run the Aspire AppHost (starts all services together)
dotnet run --project HungryGame.AppHost
```

## Architecture

**HungryGame** is a .NET 10 Blazor Server app that runs a multiplayer grid game over HTTP. The game has four states: `Joining → Eating → Battle → GameOver`.

### Core game engine (`HungryGame/`)

- `GameLogic.cs` — single singleton holding all game state (players, cells, pill values, score history). All mutation is protected by one coarse lock (`lockForPlayersCellsPillValuesAndSpecialPontValues`). Game state transitions use `Interlocked` on a `long` field.
- `Program.cs` — minimal API endpoints wired directly to `GameLogic`. All game actions are `GET` endpoints (`/join`, `/move/left|right|up|down`, `/board`, `/players`, `/state`, `/start`, `/reset`) plus `POST` admin endpoints.
- `Records.cs` — shared types: `Location`, `Cell`, `RedactedCell`, `MoveResult`, `MoveRequest`, `SharedStateClass`.
- `Player.cs` — `Player` (with token) and `RedactedPlayer` (token-stripped for public board).
- `ScoreSnapshot.cs` — records score history for charting after game ends.
- `GameExceptionHandler.cs` — maps domain exceptions to `ProblemDetails` HTTP responses.
- `LogLabelProvider.cs` — Serilog Loki label provider (uses `ILogLabelProvider.GetLabels()` from Serilog.Sinks.Loki 3.0.0).

### Blazor UI pages (`HungryGame/Shared/`, `HungryGame/Pages/`)

The Blazor Server UI subscribes to `GameLogic.GameStateChanged` events and re-renders. `SharedStateClass` (singleton) carries UI-only state like emoji choice and admin session.

### Observability

- Prometheus metrics via `prometheus-net` (`/metrics`).
- Serilog writing to console. Loki sink is available but not wired by default.
- OpenAPI via `Scalar` at `/scalar/v1`.
- `THROW_ERRORS=true` env var makes every 4th request return HTTP 500 (chaos testing).

### Clients

| Project | Purpose |
|---|---|
| `clients/foolhearty` | Hosted service bot; picks a play style (`Foolhearty` or `SmartyPants`) via `PLAY_STYLE` env var |
| `clients/massive` | Spawns many simultaneous bots (`CLIENT_COUNT` env var) for load testing |

### Aspire orchestration (`HungryGame.AppHost/`)

`AppHost.cs` wires the game server + clients together with Aspire. Parameters `boardHeight`, `boardWidth`, `secretCode`, `massivePlayerCount` are configured here. Run this project to start everything in the Aspire dashboard.

### Tests (`HungryTests/`)

Uses **SpecFlow + NUnit + FluentAssertions + Moq**. Feature files live in `HungryTests/Features/`. Step definitions in `HungryTests/StepDefinitions/`. `GameHelper.cs` provides `DrawBoard()` for ASCII board assertions. Tests instantiate `GameLogic` directly with mocked `IConfiguration`, `ILogger`, and `IRandomService` (deterministic random via sequential 0/1 output).

## Configuration

| Key | Default | Purpose |
|---|---|---|
| `SECRET_CODE` | `"password"` | Required to start/reset game and for admin login |
| `RateLimit:PermitLimit` | `100` | Requests per window |
| `RateLimit:WindowSeconds` | `1` | Rate limit window |
| `PATH_BASE` | _(none)_ | Path prefix when behind a reverse proxy |
| `THROW_ERRORS` | _(none)_ | Set to `"true"` to fail every 4th request |

Environment variables `BOARD_HEIGHT`, `BOARD_WIDTH`, `SECRET_CODE` are set by the AppHost at runtime.
