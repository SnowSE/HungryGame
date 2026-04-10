# Wash — History

## Core Context

- **Project:** HungryGame — .NET 10 Blazor Server multiplayer grid game
- **Requested by:** Jonathan Allen
- **Stack:** .NET 10, Blazor Server, minimal API, Aspire, Prometheus, Serilog
- **Solution:** HungryGame.sln — projects: HungryGame (main), HungryTests, clients/foolhearty, clients/massive, clients/Viewer
- **Game states:** Joining → Eating → Battle → GameOver
- **Key file:** GameLogic.cs — singleton with lock `lockForPlayersCellsPillValuesAndSpecialPontValues`
- **State field:** long using Interlocked (0=Joining, 1=Eating, 2=Battle, 3=GameOver)
- **IRandomService:** inject for deterministic behavior in tests — never use System.Random directly
- **User:** Jonathan Allen

## Learnings

_Append new learnings here as work progresses._
