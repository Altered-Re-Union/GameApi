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

    public Game Game { get; set; } = null!;
}
