# Multi-Game Lobby Design

**Date:** 2026-04-09  
**Status:** Approved

## Overview

Extend HungryGame from a single global game to support multiple concurrent games. Players can create named games from a lobby, manage games they own, and spectate any game in progress. The gameplay within each individual game is unchanged.

---

## 1. Architecture

### GameRegistry (new singleton)

Replaces the single `GameLogic` singleton. Holds a `ConcurrentDictionary<string, GameInstance>` and owns game creation and ID generation.

```
GameRegistry
  ├── ConcurrentDictionary<string, GameInstance>
  ├── IGameIdStrategy (injected)
  └── registered with DI as singleton
```

### GameInstance (new wrapper record)

Wraps an existing `GameLogic` plus game-level metadata. `GameLogic` itself is **unchanged**.

```csharp
class GameInstance
{
    string Id             // e.g. "X7Q"
    string Name           // creator-supplied display name
    string CreatorToken   // browser-local UUID from localStorage
    DateTime CreatedAt
    DateTime? CompletedAt // set when game reaches GameOver
    GameLogic Game        // unchanged from today
}
```

### IGameIdStrategy (new interface)

Encapsulates ID generation so the strategy can be swapped later.

```csharp
interface IGameIdStrategy
{
    string GenerateId(IEnumerable<string> existingIds);
}
```

**ShortRandomIdStrategy** (initial implementation):
- Charset: `ABCDEFGHJKMNPQRTUVWXY Z` + digits `234689 7` — all characters that are visually unambiguous in the Press Start 2P font
- Excluded: `0, 1, 5, I, L, O, S` (ambiguous glyphs)
- Final charset: letters `A B C D E F G H J K M N P Q R T U V W X Y Z` + digits `2 3 4 6 7 8 9` = 29 characters
- Start at 3 characters (29³ = 24,389 possible IDs). Automatically grow to 4 characters if fewer than 100 IDs remain available at the current length.

### GameCleanupService (new BackgroundService)

Runs hourly. Removes any `GameInstance` where `CompletedAt` is older than 30 days.

---

## 2. API Routes

All game-scoped endpoints are prefixed with `/game/{id}`. The global admin endpoints stay at their existing paths.

### Game management (new)

| Method | Route | Auth | Description |
|--------|-------|------|-------------|
| `POST` | `/games` | None | Create a game. Body: `{ name, numRows, numCols, isTimed, timeLimitMinutes, creatorToken }`. Returns `{ id, name }`. |
| `GET` | `/games` | None | List all games for lobby (grouped by state). |

### Game-scoped endpoints (moved from root)

| Method | Route | Auth | Notes |
|--------|-------|------|-------|
| `GET` | `/game/{id}/join` | None | `?userName=` |
| `GET` | `/game/{id}/move/{dir}` | None | `?token=` |
| `GET` | `/game/{id}/board` | None | |
| `GET` | `/game/{id}/players` | None | |
| `GET` | `/game/{id}/state` | None | |
| `GET` | `/game/{id}/start` | Creator or Admin | `?creatorToken=` or `?adminToken=` |
| `GET` | `/game/{id}/reset` | Creator or Admin | `?creatorToken=` or `?adminToken=` |
| `POST` | `/game/{id}/admin/boot` | Creator or Admin | Body includes `creatorToken` or `adminToken` |
| `POST` | `/game/{id}/admin/clear-players` | Creator or Admin | Body includes `creatorToken` or `adminToken` |

### Global admin (unchanged)

`POST /admin/login`, `POST /admin/logout`

### Removed

The old root-level `/join`, `/move/*`, `/board`, `/players`, `/state`, `/start`, `/reset` endpoints are removed.

---

## 3. Authorization Model

Three tiers:

| Principal | Identified by | Can manage |
|-----------|--------------|------------|
| Global admin | Admin token from `POST /admin/login` | All games, no board size cap |
| Game creator | `userToken` in localStorage matching `GameInstance.CreatorToken` | Their own games |
| Anyone else | — | Read-only: watch board, join game |

**Board size cap:** User-created games are capped at 100 rows × 150 columns. Admin-created games are uncapped. Requests exceeding the cap return `400 Bad Request`.

---

## 4. User Identity

- On first visit, the browser generates a UUID v4 stored in `localStorage` as `userToken`.
- This token is sent as `creatorToken` in the `POST /games` request body.
- Stored server-side on `GameInstance.CreatorToken`. No server-side user table.
- Creator powers are browser-local: a different browser/device cannot reclaim ownership. Admins can manage abandoned games.

---

## 5. Blazor Pages & Navigation

| Route | Page | Description |
|-------|------|-------------|
| `/` | `Lobby.razor` (new) | Lists all games in three sections: Games to Join, In Progress, Completed |
| `/game/{id}` | `Game.razor` (replaces `Index.razor`) | Game view — board, player list, controls |
| `/help` | `Help.razor` | Unchanged |
| `/player` | `Player.razor` | Unchanged |

**Nav links** (same on all pages):

```
Lobby · Help / Docs · API Client · Web Player · [admin login]
```

"Lobby" replaces the current page title link and is the always-visible home link.

### Lobby UI

- Three flowing card sections: **Games to Join** (cyan), **In Progress** (green, pulsing dot), **Completed** (muted)
- Cards use `flex-wrap` — width is natural card width (~220px), wrapping freely with browser width
- Each card shows: game name, ID pill (neon cyan, Press Start 2P font), player count, board size, state badge
- Completed cards show winner name and time since completion instead of player count
- **+ Create Game** button in top-right opens a modal

### Create Game Modal

Form fields:
- Game Name (required, text)
- Board Size: Rows × Columns (default 20×20, cap enforced client-side for non-admins)
- Time Limit (optional, minutes)

No emoji picker — pills are plain dots. On submit: navigate to `/game/{id}`.

### Game Page Header

Shows game name + ID pill in the page header alongside the standard nav links.

---

## 6. Cell Emoji Removal

The `CellIcon` / `UseCustomEmoji` fields are removed from:
- `NewGameInfo`
- `SharedStateClass`
- `StartGame.razor`
- Board rendering

Pills render as a simple dot character. This improves rendering performance and simplifies the model.

---

## 7. Bot Clients

Both `foolhearty` and `massive` receive a `GAME_ID` env var.

- If set: target `/game/{GAME_ID}/join` and `/game/{GAME_ID}/move/…`
- If not set: log a clear error and exit (no global game fallback)

`AppHost.cs` adds `GAME_ID` as a configurable parameter alongside `boardHeight`, `boardWidth`, etc.

---

## 8. Viewer Client

`clients/Viewer` is deleted entirely. The main Blazor Server game page at `/game/{id}` serves as the spectator view.

---

## 9. Background Cleanup

`GameCleanupService : BackgroundService`

- Runs on a 1-hour interval
- Removes `GameInstance` entries where `CompletedAt < DateTime.UtcNow - 30 days`
- Purely in-memory; a server restart clears all state regardless

---

## 10. Key Constraints Summary

| Constraint | Value |
|-----------|-------|
| Max board (user games) | 100 rows × 150 columns |
| Max board (admin games) | Uncapped |
| Game ID charset | `ABCDEFGHJKMNPQRTUVWXYZ2346789` (29 chars, no 0/1/5/I/L/O/S) |
| Initial ID length | 3 characters |
| ID growth trigger | < 100 IDs remaining at current length |
| Completed game retention | 30 days |
| Cleanup interval | 1 hour |
| Creator identity | Browser localStorage UUID, browser-local only |
