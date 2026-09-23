using AlteredBgaApi.Deckfmt;

namespace GameApi.Consolidation;

/// <summary>
/// Derives a player's faction from their main decklist -- per the user's
/// direction, faction is not carried on the raw game feed at all, it's
/// computed here from decoded deck content. Altered decks are effectively
/// mono-faction outside neutral/hero cards, so the modal (most frequent)
/// faction across the deck's cards is a reasonable v1 rule.
/// </summary>
public static class DeckFactionResolver
{
    /// <summary>Compressed deck (DeckfmtCodec form) in, faction code ("BR", "YZ"...) out. Null on anything unparseable or empty -- must never throw into a consolidation pass.</summary>
    public static string? Resolve(string? compressedDeck)
    {
        if (string.IsNullOrEmpty(compressedDeck))
        {
            return null;
        }

        try
        {
            var text = DeckfmtCodec.DecodeList(compressedDeck);
            var tally = new Dictionary<int, long>();

            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var spaceIndex = line.IndexOf(' ');
                if (spaceIndex <= 0 || !long.TryParse(line[..spaceIndex], out var quantity))
                {
                    continue;
                }

                var reference = line[(spaceIndex + 1)..];
                var card = EncodableCard.FromId(reference);
                tally[card.Faction] = tally.GetValueOrDefault(card.Faction) + Math.Max(quantity, 1);
            }

            if (tally.Count == 0)
            {
                return null;
            }

            var modalFaction = tally
                .OrderByDescending(entry => entry.Value)
                .ThenBy(entry => entry.Key)
                .First().Key;

            return DeckfmtConfig.For(DeckfmtConfig.Latest).FactionCode(modalFaction);
        }
        catch
        {
            // Corrupted/unparseable compressed data must not crash consolidation.
            return null;
        }
    }
}
