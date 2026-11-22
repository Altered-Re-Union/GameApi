namespace GameApi.Data.Entities;

/// <summary>
/// One player of one mirrored Game -- deck/playedCards kept exactly as
/// altered-bga-api stored them (DeckfmtCodec-compressed); this service
/// decompresses on demand when consolidating, never on the way in.
/// </summary>
public class PlayerGame
{
    public long Id { get; set; }

    /// <summary>FK to the mirrored Game this player participated in.</summary>
    public long TableId { get; set; }

    public string? BgaUserId { get; set; }

    public string? BgaName { get; set; }

    public bool IsWinner { get; set; }

    public string? Deck { get; set; }

    public string? PlayedCards { get; set; }

    /// <summary>The player's ReUnion (AlteredAuth/Keycloak) account id -- the token's `sub` -- as altered-bga-api's feed carries it. Null when the player has no linked ReUnion account, or for rows pulled before the feed carried it.</summary>
    public string? ReunionUserId { get; set; }

    /// <summary>The player's ReUnion pseudo as altered-bga-api's feed carries it. Null under the same conditions as ReunionUserId.</summary>
    public string? ReunionPseudo { get; set; }

    public Game Game { get; set; } = null!;
}
