# Squad Team

> hungrygame

## Coordinator

| Name | Role | Notes |
|------|------|-------|
| Squad | Coordinator | Routes work, enforces handoffs and reviewer gates. |

## Members

| Name | Role | Charter | Status |
|------|------|---------|--------|
| Mal | Lead / Architect | .squad/agents/mal/charter.md | Active |
| Wash | Backend Developer | .squad/agents/wash/charter.md | Active |
| Kaylee | Frontend Developer | .squad/agents/kaylee/charter.md | Active |
| Zoe | Tester / QA | .squad/agents/zoe/charter.md | Active |
| Shepherd | DevOps / Observability | .squad/agents/shepherd/charter.md | Active |
| Scribe | Session Logger | .squad/agents/scribe/charter.md | Active |
| Ralph | Work Monitor | — | Active |

## Project Context

- **Project:** HungryGame — .NET 10 Blazor Server multiplayer grid game
- **User:** Jonathan Allen
- **Created:** 2026-04-09
- **Stack:** .NET 10, Blazor Server, Blazor WASM, SpecFlow + NUnit, Aspire, Prometheus, Serilog, OpenAPI/Scalar
- **Solution:** HungryGame.sln
- **Projects:** HungryGame (main), HungryTests, clients/foolhearty, clients/massive, clients/Viewer
- **Game states:** Joining → Eating → Battle → GameOver
- **Build:** `dotnet build HungryGame.sln`
- **Run:** `dotnet run --project HungryGame`
- **Run all:** `dotnet run --project HungryGame.AppHost`
- **Test:** `dotnet test HungryTests`
