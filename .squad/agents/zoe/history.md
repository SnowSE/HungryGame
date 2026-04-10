# Zoe — History

## Core Context

- **Project:** HungryGame — .NET 10 Blazor Server multiplayer grid game
- **Requested by:** Jonathan Allen
- **Stack:** .NET 10, SpecFlow + NUnit + FluentAssertions + Moq
- **Solution:** HungryGame.sln — projects: HungryGame (main), HungryTests, clients/foolhearty, clients/massive, clients/Viewer
- **Test structure:** Feature files in HungryTests/Features/, step defs in HungryTests/StepDefinitions/
- **Mocking pattern:** GameLogic instantiated directly with mocked IConfiguration, ILogger, IRandomService
- **IRandomService mock:** sequential 0/1 for deterministic behavior
- **Run tests:** dotnet test HungryTests | filter: --filter "FullyQualifiedName~FeatureName"
- **GameHelper.cs:** DrawBoard() for ASCII board assertions
- **User:** Jonathan Allen

## Learnings

_Append new learnings here as work progresses._
