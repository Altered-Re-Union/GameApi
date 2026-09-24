using AlteredBgaApi.Deckfmt;

namespace GameApi.Consolidation;

/// <summary>
/// Derives a player's hero from their main decklist -- the hero field is
/// meant for grouping stats by hero character, not by exact print, so this
/// resolves a hero card to one normalized reference per character rather
/// than the specific reference the deck actually contains.
///
/// A hero character is reprinted at the same (faction, number-in-faction)
/// across every set/product that carries it (CORE/COREKS/ALIZE/BISE/WCS25/
/// WCF25 use 01-03, CYCLONE/DUSTER/EOLE use one of 65/85/105, FUGUE uses
/// 130) -- see the reference breakdown this table was built from. So
/// (faction, number) alone both identifies a card as a hero and identifies
/// *which* hero it is, independent of the set/product the deck's copy
/// actually came from. HeroRefsByFactionAndNumber maps each of the 36
/// (faction, number) pairs to that hero's normalized reference, chosen as
/// its first/lowest print (CORE's "B" product for the 01-03 tier, etc).
///
/// Deliberately reads faction/number straight off the raw reference string
/// (its 4th/5th '_'-separated elements) instead of going through
/// EncodableCard/CardRefElements' strict parsing -- a future alt-art print
/// of one of these heroes only needs to keep that same faction/number
/// pair to still be recognized here, even if it uses a rarity/product/suffix
/// combination CardRefElements doesn't know yet.
/// </summary>
public static class DeckHeroResolver
{
    private static readonly IReadOnlyDictionary<(string Faction, int Number), string> HeroRefsByFactionAndNumber =
        BuildTable();

    /// <summary>Compressed deck (DeckfmtCodec form) in, normalized hero reference out. Null on anything unparseable, empty, or with no recognized hero card -- must never throw into a consolidation pass.</summary>
    public static string? Resolve(string? compressedDeck)
    {
        if (string.IsNullOrEmpty(compressedDeck))
        {
            return null;
        }

        try
        {
            var text = DeckfmtCodec.DecodeList(compressedDeck);

            foreach (var line in text.Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
            {
                var spaceIndex = line.IndexOf(' ');
                if (spaceIndex <= 0)
                {
                    continue;
                }

                var reference = line[(spaceIndex + 1)..];
                var parts = reference.Split('_');
                if (parts.Length < 5 || !int.TryParse(parts[4], out var number))
                {
                    continue;
                }

                var faction = parts[3];

                if (HeroRefsByFactionAndNumber.TryGetValue((faction, number), out var normalized))
                {
                    return normalized;
                }
            }

            return null;
        }
        catch
        {
            // Corrupted/unparseable compressed data must not crash consolidation.
            return null;
        }
    }

    private static IReadOnlyDictionary<(string, int), string> BuildTable()
    {
        // Every faction shares the CORE 01-03 tier and the CYCLONE 65 tier;
        // the remaining DUSTER/EOLE tier's number and home set differ by
        // faction -- see the "CYCLONE / DUSTER / EOLE" breakdown: AX/MU/OR
        // carry that third hero in DUSTER at number 85, BR/LY/YZ carry it in
        // EOLE at number 105.
        var secondTierBySet = new (string Set, int Number, string[] Factions)[]
        {
            ("DUSTER", 85, ["AX", "MU", "OR"]),
            ("EOLE", 105, ["BR", "LY", "YZ"]),
        };

        string[] factions = ["AX", "BR", "LY", "MU", "OR", "YZ"];
        var table = new Dictionary<(string, int), string>();

        foreach (var faction in factions)
        {
            foreach (var number in new[] { 1, 2, 3 })
            {
                table[(faction, number)] = $"ALT_CORE_B_{faction}_{number:00}_C";
            }

            table[(faction, 65)] = $"ALT_CYCLONE_B_{faction}_65_C";
            table[(faction, 130)] = $"ALT_FUGUE_B_{faction}_130_C";
        }

        foreach (var (set, number, setFactions) in secondTierBySet)
        {
            foreach (var faction in setFactions)
            {
                table[(faction, number)] = $"ALT_{set}_B_{faction}_{number}_C";
            }
        }

        return table;
    }
}
