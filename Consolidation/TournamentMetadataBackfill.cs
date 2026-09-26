using GameApi.Data;
using Microsoft.EntityFrameworkCore;

namespace GameApi.Consolidation;

/// <summary>
/// One-time-per-tournament backfill: populates Mode/LastGameAt for
/// Tournament rows created before those fields existed. ConsolidationPass
/// only recomputes a tournament whose games just changed, so a finished
/// tournament that gets no new games would otherwise keep both fields null
/// forever.
///
/// Safe to run on every startup, no marker table needed: a tournament
/// already backfilled -- or recomputed since, which sets LastGameAt as a
/// side effect -- no longer matches the query below (LastGameAt is a Max
/// over that tournament's own non-empty game set, so it's never null after
/// a successful recompute). The precondition is its own completion check,
/// same convention as TournamentParentIdBackfill.
/// </summary>
public static class TournamentMetadataBackfill
{
    public static async Task<int> RunOnceAsync(GameApiDbContext db, CancellationToken cancellationToken = default)
    {
        var staleParentIds = await db.Tournaments
            .Where(t => t.LastGameAt == null)
            .Select(t => t.TournamentParentId)
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
