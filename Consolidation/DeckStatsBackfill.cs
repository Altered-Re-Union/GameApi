using GameApi.Data;
using Microsoft.EntityFrameworkCore;

namespace GameApi.Consolidation;

/// <summary>
/// One-time-per-tournament backfill: rebuilds tournaments whose
/// PlayerTournament.DecksJson was written before DeckUsage carried per-deck
/// Wins/Losses/LastPlayedAt. ConsolidationPass only recomputes a tournament
/// whose games just changed, so a finished tournament would otherwise expose
/// 0/0/null per deck forever (to GET /api/tournament-decks in particular).
///
/// Safe to run on every startup, no marker table needed: a recompute always
/// serializes every DeckUsage property, so a rebuilt row's non-empty DecksJson
/// contains "Wins" and no longer matches the query below. Same self-completing
/// convention as TournamentMetadataBackfill.
/// </summary>
public static class DeckStatsBackfill
{
    public static async Task<int> RunOnceAsync(GameApiDbContext db, CancellationToken cancellationToken = default)
    {
        var staleParentIds = await db.PlayerTournaments
            .Where(p => p.DecksJson != null && p.DecksJson != "[]" && !p.DecksJson.Contains("\"Wins\""))
            .Select(p => p.TournamentParentId)
            .Distinct()
            .ToListAsync(cancellationToken);

        foreach (var parentId in staleParentIds)
        {
            await ConsolidationPass.RecomputeTournamentAsync(db, parentId, cancellationToken);
        }

        if (staleParentIds.Count > 0)
        {
            await db.SaveChangesAsync(cancellationToken);
        }

        return staleParentIds.Count;
    }
}
