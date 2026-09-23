namespace GameApi.Data.Entities;

/// <summary>
/// Local mirror of one row pulled from altered-bga-api's GET /api/games --
/// the raw facts table this service's Tournament/PlayerTournament aggregates
/// are built from. Needed because a per-tournament aggregate has to be
/// recomputed from a tournament's *entire* history whenever anything in it
/// changes, not just from the newest page the sync worker pulled -- see
/// Consolidation/ConsolidationWorker.cs.
/// </summary>
public class Game
{
    /// <summary>BGA's table identifier -- same PK convention as upstream (rematches overwrite in place).</summary>
    public long TableId { get; set; }

    public DateTimeOffset ReceivedAt { get; set; }

    public string? Format { get; set; }

    public long? TournamentId { get; set; }

    /// <summary>The stage's own name, as altered-bga-api stored it.</summary>
    public string? TournamentName { get; set; }

    /// <summary>Resolved parent id, null when the game carries no tournament at all.</summary>
    public long? TournamentParentId { get; set; }

    public string? TournamentGroup { get; set; }

    public ICollection<PlayerGame> PlayerGames { get; set; } = [];
}
