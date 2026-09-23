using System.Text.RegularExpressions;

namespace AlteredBgaApi.Deckfmt;

/// <summary>
/// Parses a card id string (e.g. "ALT_CORE_B_BR_03_C", "ALT_CORE_B_LY_07_U_12345"
/// or "ALT_DUSTERCB_P_LY_65_C_015") into the ints EncodableCard's wire format
/// uses, resolving set/faction/rarity/product codes through
/// DeckfmtConfig.Latest -- this class is only ever used when encoding fresh
/// text into a new blob, never when decoding an existing one (decoding uses
/// the version embedded in that blob instead, see EncodableCard.Decode).
/// </summary>
public class CardRefElements
{
    // The trailing number is a genuine print/instance number (Unique's own
    // number, or a DUSTERCB-style serialized common's) -- or, only for
    // non-Unique rarities, the literal "XXX" the card catalog uses for a
    // unnumbered/unassigned print template. XXX has no real serial yet, so
    // it's parsed as instance number 0 (never a genuine serial -- those
    // start at 1) and rendered back as "XXX", not "000" -- see
    // EncodableCard.AsCardId().
    //
    // Bare "R" (as opposed to "R1"/"R2") shows up on real CORE game data --
    // a data bug in the BGA game module itself (AlteredBGA), not a genuinely
    // ambiguous format: 162 CORE-set "R1" rare cards have their uid
    // hardcoded as "..._R" (missing the trailing "1") in the game's card
    // class files, while their paired R2 print is always correct. Since
    // that mislabeling is deterministic and set-wide (every bare "R" seen
    // in practice is one of those 162 cards), it's normalized to R1 here
    // rather than kept as a separate rarity -- see CardRefElements' ctor.
    private static readonly Regex CardIdPattern = new(@"^ALT_(\w+)_(A|B|P)_(\w{2})_(\d+)_(C|R1|R2|R|U|E)(?:_(\d+|XXX))?$");

    public int SetCode { get; }
    public int? Product { get; }
    public int Faction { get; }
    public int NumInFaction { get; }
    public int Rarity { get; }
    public int? UniqNum { get; }

    public CardRefElements(string id)
    {
        var match = CardIdPattern.Match(id);
        if (!match.Success)
            throw new ArgumentException($"Unrecognized card id '{id}'");

        var config = DeckfmtConfig.For(DeckfmtConfig.Latest);

        SetCode = config.SetByCode(match.Groups[1].Value).Id;
        Product = config.ProductId(match.Groups[2].Value);
        Faction = config.FactionId(match.Groups[3].Value);
        NumInFaction = int.Parse(match.Groups[4].Value);
        var rarityCode = match.Groups[5].Value == "R" ? "R1" : match.Groups[5].Value;
        Rarity = config.RarityId(rarityCode);
        UniqNum = match.Groups[6].Success
            ? (match.Groups[6].Value == "XXX" ? 0 : int.Parse(match.Groups[6].Value))
            : null;

        if (config.RarityCode(Rarity) == "U" && UniqNum == null)
            throw new ArgumentException("Unique card is missing a unique_number");
    }
}

public record CardRefQty(int Quantity, string Id);
