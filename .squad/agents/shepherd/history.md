# Shepherd — History

## Core Context

- **Project:** HungryGame — .NET 10 Blazor Server multiplayer grid game
- **Requested by:** Jonathan Allen
- **Stack:** .NET 10, Aspire, Prometheus (prometheus-net), Serilog, OpenAPI/Scalar
- **Solution:** HungryGame.sln — projects: HungryGame (main), HungryTests, clients/foolhearty, clients/massive, clients/Viewer
- **Aspire AppHost:** HungryGame.AppHost/AppHost.cs — parameters: boardHeight, boardWidth, secretCode, massivePlayerCount
- **Loki sink:** Serilog.Sinks.Loki 3.0.0 (stable) — ILogLabelProvider has only GetLabels(); no PropertiesAsLabels, PropertiesToAppend, or FormatterStrategy
- **Prometheus:** /metrics endpoint via prometheus-net — do not break
- **OpenAPI:** /scalar/v1 — do not move
- **Env vars set by AppHost:** BOARD_HEIGHT, BOARD_WIDTH, SECRET_CODE
- **Chaos testing:** THROW_ERRORS=true makes every 4th request return HTTP 500
- **User:** Jonathan Allen

## Learnings

_Append new learnings here as work progresses._
