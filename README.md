# GameApi

Consolidation service for Altered tournament data. Split out of
`altered-bga-api` so that gateway -- which BGA depends on for every deck
selection and end-of-game call -- doesn't also carry the cost of building
tournament aggregates. `altered-bga-api` still owns raw ingestion
(`POST /games/register`) and stays a thin, latency-sensitive pass-through;
this service polls its `GET /api/games` feed and does the expensive part on
its own schedule, in its own database, against its own load.

## What it does

A background worker ([`Consolidation/ConsolidationWorker.cs`](Consolidation/ConsolidationWorker.cs),
logic in [`Consolidation/ConsolidationPass.cs`](Consolidation/ConsolidationPass.cs))
polls `altered-bga-api`'s `GET /api/games` on an interval (`Consolidation:Interval`,
5 minutes by default), pulling **every** game -- tournament or not -- since
the last pass. Each pulled game is mirrored into a local `Game`/`PlayerGame`
copy, then every parent tournament touched by new games is fully rebuilt
from that local mirror's entire history into two computed tables:

- **`Tournament`** -- one row per parent tournament: its resolved name
  (`TournamentParentName`, the longest prefix its stages' names share -- see
  [`Consolidation/TournamentNameResolver.cs`](Consolidation/TournamentNameResolver.cs)),
  total games, total distinct players.
- **`PlayerTournament`** -- one row per player per parent tournament: wins,
  losses, decks played, their main (most-used) decklist, and a faction
  derived by decoding that decklist (see
  [`Consolidation/DeckFactionResolver.cs`](Consolidation/DeckFactionResolver.cs)) --
  faction is not carried on the raw feed at all, it's computed here from
  decoded deck content, and so will `Hero` once a fixed hero-eligible card
  reference list exists to match against (currently an unpopulated column).

Every player row also carries an **admin differential** --
`AdminWinsAdjustment` / `AdminLossesAdjustment` / `AdminAdjustmentNote` --
for manual corrections (an admin override, a BGA bug, a game played
off-platform). These are additive on top of the computed `Wins`/`Losses` and
are never touched by a recompute; a caller wanting the effective total adds
them itself.

## API

Two different auth models, by how privileged the route is:

- `GET /api/tournaments` -- every tournament with a computed aggregate.
- `GET /api/tournaments/{tournamentParentId}/players` -- every player's
  row for that tournament, decklist still `DeckfmtCodec`-compressed.

  Both require `Authorization: Bearer <token>` -- an AlteredAuth (Keycloak)
  access token carrying the `bga-game-history` scope, validated by this
  service itself (standard OIDC/JWKS via `Keycloak:Authority`, see
  `Security/BgaJwtAuth.cs` and the `BgaGameHistory` authorization policy in
  `Program.cs`). `401` with no/invalid token, `403` with a valid token
  missing the scope. This is the surface a website/other reader calls, so it
  goes through the same user-facing identity provider as the rest of the
  ecosystem rather than a service-specific secret.

- `POST /api/tournaments/{tournamentParentId}/players/{bgaUserId}/adjustment`
  -- body `{ winsAdjustment, lossesAdjustment, note }`, `note` required
  non-empty (the audit trail the field exists for).

  Rewriting a recorded result is a privileged action, not just reading one --
  so this route is deliberately **not** gated on the `bga-game-history` scope
  every reader holds. It requires `Authorization: Bearer <key>` matching its
  own `ApiKeys:Adjustment` secret instead; a valid `bga-game-history`-scoped
  token alone does not unlock it. `401` on a missing/wrong key.

## Run locally

Needs a Postgres reachable at `ConnectionStrings:gameapidb`
(`appsettings.Development.json` defaults to `localhost` / db `gameapidb` /
user+password `postgres`), and a running `altered-bga-api` instance to poll
(`AlteredBgaApi:BaseUrl`, `AlteredBgaApi:ApiKey` matching its
`ApiKeys:GameSync`):

```
docker run --rm -e POSTGRES_PASSWORD=postgres -e POSTGRES_DB=gameapidb -p 5433:5432 postgres:17
dotnet ef database update
dotnet run
```

## Tests

`GameApi.Tests` (xUnit) needs no external services -- the consolidation
worker's core logic (`ConsolidationPass`) is tested against EF Core's
InMemory provider with a scripted `IGameSyncClient` standing in for
`altered-bga-api`, so nothing needs to reach a real Postgres or a real
gateway:

```
dotnet test
```

## Deploy

Same shape as `altered-bga-api`: `build/Dockerfile` (`app` and `migrations`
targets) built and pushed by `.github/workflows/publish.yml`. Wiring this
into `AlteredOps` and choosing a `bga-api.altered.re`-equivalent hostname is
a follow-up, not done by this scaffold.
