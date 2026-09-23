namespace GameApi.Data.Entities;

/// <summary>
/// One consolidated tournament aggregate -- a computed row, rebuilt by
/// Consolidation/ConsolidationWorker from the local Game mirror whenever one
/// of its games changes. Only ever exists for a TournamentParentId that at
/// least one mirrored game actually carries; a game with no tournament never
/// produces one.
/// </summary>
public class Tournament
{
    /// <summary>BGA's parent tournament id -- not DB-generated.</summary>
    public long TournamentParentId { get; set; }

    /// <summary>
    /// BGA names every stage after the tournament plus an extension ("Winter
    /// Cup - Round 1", "Winter Cup - Round 2"); this is the longest prefix
    /// the tournament's stages share -- see Consolidation/TournamentNameResolver.
    /// </summary>
    public string? TournamentParentName { get; set; }

    public int TotalGames { get; set; }

    /// <summary>Distinct players across every stage -- a participant count, not a seat count.</summary>
    public int TotalPlayers { get; set; }

    /// <summary>When the content last actually changed.</summary>
    public DateTimeOffset ComputedAt { get; set; }

    /// <summary>When a consolidation pass last looked at this tournament, whether or not anything changed.</summary>
    public DateTimeOffset RefreshedAt { get; set; }
}
