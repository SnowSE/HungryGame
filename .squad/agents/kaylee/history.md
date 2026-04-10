# Kaylee — History

## Core Context

- **Project:** HungryGame — .NET 10 Blazor Server multiplayer grid game
- **Requested by:** Jonathan Allen
- **Stack:** .NET 10, Blazor Server, Blazor WASM (Viewer client)
- **Solution:** HungryGame.sln — projects: HungryGame (main), HungryTests, clients/foolhearty, clients/massive, clients/Viewer
- **UI pattern:** Blazor Server pages subscribe to GameLogic.GameStateChanged events → call StateHasChanged()
- **SharedStateClass:** singleton for UI-only state (emoji choice, admin session)
- **Viewer:** Blazor WASM in clients/Viewer — read-only spectator polling /board and /players
- **Admin gate:** SECRET_CODE required for start/reset actions
- **User:** Jonathan Allen

## Learnings

_Append new learnings here as work progresses._
