namespace GameApi.Data.Entities;

/// <summary>
/// One distinct decklist a player used in a tournament, and how many games
/// they played it. DeckfmtCodec-compressed. Serialized to/from
/// PlayerTournament.DecksJson, and reused as-is for the API DTOs.
///
/// Deck/Games stay first and unchanged -- existing consumers read only those.
/// Wins/Losses/LastPlayedAt are optional constructor parameters on purpose:
/// DecksJson written before they existed deserializes with 0/0/null rather
/// than failing (see DeckStatsBackfill, which rebuilds those rows).
/// Wins/Losses follow the same rule as PlayerTournament.Wins/Losses: a game
/// with no recognized winner counts toward neither. Admin adjustments are
/// per player, never per deck, so they are not reflected here.
/// </summary>
public sealed record DeckUsage(
    string Deck,
    int Games,
    int Wins = 0,
    int Losses = 0,
    DateTimeOffset? LastPlayedAt = null);
