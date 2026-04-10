# Mal — History

## Core Context

- **Project:** HungryGame — .NET 10 Blazor Server multiplayer grid game
- **Requested by:** Jonathan Allen
- **Stack:** .NET 10, Blazor Server, SpecFlow + NUnit, Aspire, Prometheus, Serilog, OpenAPI/Scalar
- **Solution:** HungryGame.sln — projects: HungryGame (main), HungryTests, clients/foolhearty, clients/massive, clients/Viewer
- **Game states:** Joining → Eating → Battle → GameOver
- **Key file:** GameLogic.cs — singleton, one coarse lock, Interlocked state transitions
- **User:** Jonathan Allen

## Learnings

_Append new learnings here as work progresses._
