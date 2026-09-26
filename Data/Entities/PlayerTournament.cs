namespace GameApi.Data.Entities;

/// <summary>
/// One player's consolidated record within one parent tournament -- composite
/// key (TournamentParentId, BgaUserId). Wins/Losses/DecksPlayed/MainDeck/
/// Faction/Hero are computed columns, fully rebuilt on every consolidation
/// pass; the Admin* columns are the opposite -- hand-entered, additive, and
/// never touched by a recompute (see Consolidation/ConsolidationWorker).
/// </summary>
public class PlayerTournament
{
    public long TournamentParentId { get; set; }

    public string BgaUserId { get; set; } = default!;

    public string? BgaName { get; set; }

    /// <summary>Tallied from IsWinner across the player's games in this tournament.</summary>
    public int Wins { get; set; }

    /// <summary>A game with no recognized winner counts toward neither Wins nor Losses.</summary>
    public int Losses { get; set; }

    /// <summary>Count of *distinct* compressed decklists the player used across the tournament.</summary>
    public int DecksPlayed { get; set; }

    /// <summary>The most-frequently-used decklist among the player's games this tournament, DeckfmtCodec-compressed (tie-break: first seen).</summary>
    public string? MainDeck { get; set; }

    /// <summary>Derived from decoding MainDeck -- the modal faction across its cards. See Consolidation/DeckFactionResolver.</summary>
    public string? Faction { get; set; }

    /// <summary>Derived from decoding MainDeck -- the deck's hero card, normalized to one reference per hero character regardless of which set/product print it actually is. See Consolidation/DeckHeroResolver.</summary>
    public string? Hero { get; set; }

    /// <summary>Every distinct decklist the player used this tournament, most-played first (ties: first-seen), JSON-serialized IReadOnlyList&lt;DeckUsage&gt;. MainDeck above is just this list's first entry, kept for existing consumers.</summary>
    public string? DecksJson { get; set; }

    /// <summary>
    /// Manual correction, additive on top of Wins: admin overrides, BGA
    /// bugs, games played off-platform. Never touched by a recompute.
    /// </summary>
    public int AdminWinsAdjustment { get; set; }

    /// <summary>Manual correction, additive on top of Losses. Never touched by a recompute.</summary>
    public int AdminLossesAdjustment { get; set; }

    /// <summary>Required whenever an admin adjustment is set -- the audit trail the adjustment endpoint exists for.</summary>
    public string? AdminAdjustmentNote { get; set; }

    public DateTimeOffset ComputedAt { get; set; }

    public DateTimeOffset RefreshedAt { get; set; }
}
