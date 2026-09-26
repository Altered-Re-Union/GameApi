namespace GameApi.Data.Entities;

/// <summary>One distinct decklist a player used in a tournament, and how many games they played it. DeckfmtCodec-compressed. Serialized to/from PlayerTournament.DecksJson, and reused as-is for the API DTOs.</summary>
public sealed record DeckUsage(string Deck, int Games);
