using GameApi.Data;
using Microsoft.EntityFrameworkCore;

namespace GameApi.Consolidation;

/// <summary>
/// One-time-per-tournament backfill: corrects Games.TournamentParentId for
/// rows already mirrored from tournaments that predate altered-bga-api's
/// tournament_parent_id field (AlteredBGA#630), then rebuilds the
/// Tournament/PlayerTournament aggregates for every parent touched.
///
/// altered-bga-api's own BackfillTournamentParentIdByNamingPattern migration
/// fixes the source Tournaments.TournamentParentId there, but ConsolidationPass
/// only pulls *new* games from its sync feed -- a finished tournament that's
/// already mirrored keeps whatever TournamentParentId it carried at mirror
/// time, self-parented default included, forever. This backfill re-derives
/// the same naming-pattern rule against the local Games mirror instead of
/// depending on the two services ever running their fix at the same time: a
/// "&lt;prefix&gt; - Stage 2" game plus up to two "&lt;prefix&gt; - Stage 1 - Group
/// 1"/"Group 2" siblings sharing that prefix are re-parented at the Stage 2
/// tournament's own id minus 2, same as the sibling migration.
///
/// Safe to run on every startup, no marker table needed: a tournament
/// already corrected -- or one BGA has since asserted a real parent for --
/// is no longer self-parented (TournamentId == TournamentParentId), so it no
/// longer matches the query below. The precondition is its own completion
/// check.
/// </summary>
public static class TournamentParentIdBackfill
{
    private static readonly string StageTwoSuffix = " - Stage 2";

    private static readonly string[] KnownSuffixes =
    [
        " - Stage 2",
        " - Stage 1 - Group 1",
        " - Stage 1 - Group 2",
    ];

    public static async Task<int> RunOnceAsync(GameApiDbContext db, CancellationToken cancellationToken = default)
    {
        var selfParented = await db.Games
            .Where(g => g.TournamentId != null && g.TournamentParentId == g.TournamentId && g.TournamentName != null)
            .Select(g => new { TournamentId = g.TournamentId!.Value, TournamentName = g.TournamentName! })
            .Distinct()
            .ToListAsync(cancellationToken);

        // Only a prefix with exactly one "Stage 2" candidate is trusted --
        // two unrelated tournaments that happen to share a name are left
        // alone rather than risked on a wrong pairing (same guard the SQL
        // migration in altered-bga-api uses).
        var stage2IdByPrefix = selfParented
            .Where(t => t.TournamentName.EndsWith(StageTwoSuffix, StringComparison.Ordinal))
            .Select(t => (Prefix: t.TournamentName[..^StageTwoSuffix.Length], t.TournamentId))
            .GroupBy(t => t.Prefix, StringComparer.Ordinal)
            .Where(g => g.Count() == 1)
            .ToDictionary(g => g.Key, g => g.Single().TournamentId, StringComparer.Ordinal);

        var newParentByOldTournamentId = new Dictionary<long, long>();
        foreach (var stage in selfParented)
        {
            var prefix = StripKnownSuffix(stage.TournamentName);
            if (prefix is null || !stage2IdByPrefix.TryGetValue(prefix, out var stage2Id))
            {
                continue;
            }

            newParentByOldTournamentId[stage.TournamentId] = stage2Id - 2;
        }

        if (newParentByOldTournamentId.Count == 0)
        {
            return 0;
        }

        var touchedParents = new HashSet<long>();
        foreach (var (oldTournamentId, newParentId) in newParentByOldTournamentId)
        {
            var games = await db.Games
                .Where(g => g.TournamentId == oldTournamentId && g.TournamentParentId == oldTournamentId)
                .ToListAsync(cancellationToken);

            foreach (var game in games)
            {
                game.TournamentParentId = newParentId;
            }

            touchedParents.Add(oldTournamentId);
            touchedParents.Add(newParentId);
        }

        await db.SaveChangesAsync(cancellationToken);

        foreach (var parentId in touchedParents)
        {
            await ConsolidationPass.RecomputeTournamentAsync(db, parentId, cancellationToken);
        }

        await db.SaveChangesAsync(cancellationToken);

        return newParentByOldTournamentId.Count;
    }

    private static string? StripKnownSuffix(string name)
    {
        foreach (var suffix in KnownSuffixes)
        {
            if (name.EndsWith(suffix, StringComparison.Ordinal))
            {
                return name[..^suffix.Length];
            }
        }

        return null;
    }
}
