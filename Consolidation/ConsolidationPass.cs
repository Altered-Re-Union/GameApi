using GameApi.Data;
using GameApi.Data.Entities;
using GameApi.GameSync;
using Microsoft.EntityFrameworkCore;

namespace GameApi.Consolidation;

/// <summary>
/// One consolidation pass: pulls every new game from altered-bga-api's
/// GET /api/games, mirrors it locally, and rebuilds Tournament/
/// PlayerTournament for every parent tournament touched -- always from that
/// tournament's *entire* history in the local mirror, never just the new
/// page, since a per-player aggregate can't be built incrementally without
/// re-deriving it whenever something upstream (a corrected tournamentId, a
/// replayed body) changes what "the whole history" contains.
///
/// Static + a plain BgaDbContext-shaped dependency (IGameSyncClient) so this
/// is unit-testable against EF Core InMemory without a real HTTP call or
/// hosted-service timer -- see GameApi.Tests. Consolidation/ConsolidationWorker
/// is the thin IHostedService wrapper that calls this on a schedule.
/// </summary>
public static class ConsolidationPass
{
    public static async Task<ConsolidationResult> RunAsync(
        GameApiDbContext db, IGameSyncClient client, int pageSize, CancellationToken cancellationToken = default)
    {
        var cursor = await db.SyncCursors.FindAsync([SyncCursor.Key], cancellationToken);
        var since = cursor?.Since;
        var touchedParents = new HashSet<long>();
        var pulled = 0;

        while (true)
        {
            var page = await client.FetchPageAsync(since, pageSize, cancellationToken);

            foreach (var game in page.Games)
            {
                var previousParentId = await MirrorGameAsync(db, game, cancellationToken);
                pulled++;
                if (game.TournamentParentId is { } parentId)
                {
                    touchedParents.Add(parentId);
                }

                // A game already mirrored under one parent can be re-pulled
                // pointing at a different one (BGA names a real parent after
                // the fact -- see altered-bga-api's EndGameHandler doc
                // comment on self-parenting). The tournament it used to
                // belong to needs recomputing too, or its aggregate goes
                // stale/orphaned.
                if (previousParentId is { } oldParentId && oldParentId != game.TournamentParentId)
                {
                    touchedParents.Add(oldParentId);
                }
            }

            if (page.Games.Count == 0)
            {
                break;
            }

            // Prefer the server's own cursor value; fall back to the last
            // pulled row only if it's somehow missing.
            since = page.NextSince is { } next
                ? DateTimeOffset.Parse(next)
                : page.Games[^1].ReceivedAt;

            if (!page.HasMore)
            {
                break;
            }
        }

        // Mirrored rows must actually be in the store before
        // RecomputeTournamentAsync queries for them below -- an Added
        // entity isn't visible to a fresh query against the DbSet until
        // SaveChanges flushes it, InMemory provider included.
        await db.SaveChangesAsync(cancellationToken);

        foreach (var parentId in touchedParents)
        {
            await RecomputeTournamentAsync(db, parentId, cancellationToken);
        }

        if (cursor is null)
        {
            cursor = new SyncCursor();
            db.SyncCursors.Add(cursor);
        }

        cursor.Since = since;
        cursor.UpdatedAt = DateTimeOffset.UtcNow;

        await db.SaveChangesAsync(cancellationToken);

        return new ConsolidationResult(pulled, touchedParents.Count);
    }

    /// <summary>Mirrors one pulled game, returning the TournamentParentId it carried locally before this update (null for a first sight).</summary>
    private static async Task<long?> MirrorGameAsync(GameApiDbContext db, GameSyncGame game, CancellationToken cancellationToken)
    {
        var existing = await db.Games
            .Include(g => g.PlayerGames)
            .FirstOrDefaultAsync(g => g.TableId == game.TableId, cancellationToken);

        long? previousParentId = null;

        if (existing is null)
        {
            existing = new Game { TableId = game.TableId };
            db.Games.Add(existing);
        }
        else
        {
            previousParentId = existing.TournamentParentId;
            // Rematches of the same table overwrite in place upstream too --
            // mirror that by replacing the player rows wholesale rather than
            // trying to diff them.
            db.PlayerGames.RemoveRange(existing.PlayerGames);
        }

        existing.ReceivedAt = game.ReceivedAt;
        existing.Format = game.Format;
        existing.TournamentId = game.TournamentId;
        existing.TournamentName = game.TournamentName;
        existing.TournamentParentId = game.TournamentParentId;
        existing.TournamentGroup = game.TournamentGroup;

        foreach (var player in game.Players)
        {
            db.PlayerGames.Add(new PlayerGame
            {
                TableId = game.TableId,
                BgaUserId = player.BgaUserId,
                BgaName = player.BgaName,
                IsWinner = player.IsWinner,
                Deck = player.Deck,
                PlayedCards = player.PlayedCards,
            });
        }

        return previousParentId;
    }

    private static async Task RecomputeTournamentAsync(GameApiDbContext db, long parentId, CancellationToken cancellationToken)
    {
        var games = await db.Games
            .Include(g => g.PlayerGames)
            .Where(g => g.TournamentParentId == parentId)
            .ToListAsync(cancellationToken);

        // Every game that used to point at this parent has since moved
        // elsewhere (see the "previousParentId" handling above) -- nothing
        // left to aggregate, so the row itself (and any player rows under
        // it) is garbage, not just stale. Same situation altered-bga-api's
        // now-deleted snapshot refresher called "a demoted parent".
        if (games.Count == 0)
        {
            var orphanedTournament = await db.Tournaments.FindAsync([parentId], cancellationToken);
            if (orphanedTournament is not null)
            {
                db.Tournaments.Remove(orphanedTournament);
            }

            var orphanedPlayers = await db.PlayerTournaments
                .Where(p => p.TournamentParentId == parentId)
                .ToListAsync(cancellationToken);
            db.PlayerTournaments.RemoveRange(orphanedPlayers);

            return;
        }

        var now = DateTimeOffset.UtcNow;

        var tournament = await db.Tournaments.FindAsync([parentId], cancellationToken);
        if (tournament is null)
        {
            tournament = new Tournament { TournamentParentId = parentId };
            db.Tournaments.Add(tournament);
        }

        tournament.TournamentParentName = TournamentNameResolver.Resolve(games.Select(g => g.TournamentName));
        tournament.TotalGames = games.Count;
        tournament.TotalPlayers = games
            .SelectMany(g => g.PlayerGames)
            .Select(p => p.BgaUserId)
            .Where(id => !string.IsNullOrEmpty(id))
            .Distinct(StringComparer.Ordinal)
            .Count();
        tournament.ComputedAt = now;
        tournament.RefreshedAt = now;

        var entriesByPlayer = games
            .SelectMany(g => g.PlayerGames.Select(p => (Game: g, Player: p)))
            .Where(x => !string.IsNullOrEmpty(x.Player.BgaUserId))
            .GroupBy(x => x.Player.BgaUserId!, StringComparer.Ordinal);

        foreach (var group in entriesByPlayer)
        {
            var entries = group.ToList();

            var wins = entries.Count(x => x.Player.IsWinner);
            // A game with no recognized winner counts toward neither Wins
            // nor Losses -- only count a loss when that specific game did
            // name one.
            var losses = entries.Count(x => !x.Player.IsWinner && x.Game.PlayerGames.Any(p => p.IsWinner));

            var deckGroups = entries
                .Where(x => !string.IsNullOrEmpty(x.Player.Deck))
                .GroupBy(x => x.Player.Deck!, StringComparer.Ordinal)
                .ToList();

            // Most-frequently-used deck; ties broken by first-seen order
            // (the order `entries` is already in, since Games was queried
            // ordered by ReceivedAt/TableId upstream).
            var mainDeck = deckGroups
                .OrderByDescending(deckGroup => deckGroup.Count())
                .ThenBy(deckGroup => entries.FindIndex(x => x.Player.Deck == deckGroup.Key))
                .FirstOrDefault()?.Key;

            var playerTournament = await db.PlayerTournaments.FindAsync([parentId, group.Key], cancellationToken);
            if (playerTournament is null)
            {
                playerTournament = new PlayerTournament { TournamentParentId = parentId, BgaUserId = group.Key };
                db.PlayerTournaments.Add(playerTournament);
            }

            playerTournament.BgaName = entries[^1].Player.BgaName ?? playerTournament.BgaName;
            playerTournament.Wins = wins;
            playerTournament.Losses = losses;
            playerTournament.DecksPlayed = deckGroups.Count;
            playerTournament.MainDeck = mainDeck;
            playerTournament.Faction = DeckFactionResolver.Resolve(mainDeck);
            playerTournament.ComputedAt = now;
            playerTournament.RefreshedAt = now;
            // AdminWinsAdjustment / AdminLossesAdjustment / AdminAdjustmentNote
            // are deliberately untouched: a recompute must never erase a
            // hand-entered correction.
        }
    }
}

public sealed record ConsolidationResult(int GamesPulled, int TournamentsTouched);
