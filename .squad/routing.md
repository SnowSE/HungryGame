# Work Routing

How to decide who handles what.

## Routing Table

| Work Type | Route To | Examples |
|-----------|----------|---------|
| Architecture, scope, priorities, code review | Mal | Design decisions, PR review, what to build next, trade-offs |
| Game engine, API endpoints, bot clients | Wash | GameLogic.cs, Program.cs, Records.cs, foolhearty, massive |
| Blazor UI, WASM viewer, admin UI | Kaylee | Pages/, Shared/, wwwroot/, clients/Viewer |
| SpecFlow tests, NUnit, QA, coverage | Zoe | HungryTests/Features/, StepDefinitions/, GameHelper.cs |
| Aspire, Prometheus, Serilog, CI/CD, infra | Shepherd | AppHost.cs, metrics, logging, env vars, GitHub Actions |
| Code review / rejection gate | Mal or Zoe | Mal: architecture/logic; Zoe: test coverage and correctness |
| Session logging | Scribe | Automatic — never needs routing |
| Work queue, backlog, GitHub issue monitoring | Ralph | Automatic when activated |

## Issue Routing

| Label | Action | Who |
|-------|--------|-----|
| `squad` | Triage: analyze issue, assign `squad:{member}` label | Mal |
| `squad:mal` | Architecture, review, scope decisions | Mal |
| `squad:wash` | Backend, game engine, bot client work | Wash |
| `squad:kaylee` | Frontend, UI, Blazor WASM viewer | Kaylee |
| `squad:zoe` | Tests, QA, SpecFlow scenarios | Zoe |
| `squad:shepherd` | DevOps, observability, Aspire, CI | Shepherd |

### How Issue Assignment Works

1. When a GitHub issue gets the `squad` label, **Mal** triages it — analyzing content, assigning the right `squad:{member}` label, and commenting with triage notes.
2. When a `squad:{member}` label is applied, that member picks up the issue in their next session.
3. Members can reassign by removing their label and adding another member's label.
4. The `squad` label is the "inbox" — untriaged issues waiting for Mal's review.

## Rules

1. **Eager by default** — spawn all agents who could usefully start work, including anticipatory downstream work.
2. **Scribe always runs** after substantial work, always as `mode: "background"`. Never blocks.
3. **Quick facts → coordinator answers directly.** Don't spawn an agent for "what port does the server run on?"
4. **When two agents could handle it**, pick the one whose domain is the primary concern.
5. **"Team, ..." → fan-out.** Spawn all relevant agents in parallel as `mode: "background"`.
6. **Anticipate downstream work.** If a feature is being built, spawn Zoe to write test cases from requirements simultaneously.
7. **Issue-labeled work** — when a `squad:{member}` label is applied to an issue, route to that member. Mal handles all `squad` (base label) triage.
8. **Lock conflicts:** Anything touching GameLogic.cs lock semantics or Interlocked state transitions must go through Mal for review before merging.
