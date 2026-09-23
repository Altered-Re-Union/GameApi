using System.Text.RegularExpressions;

namespace AlteredBgaApi.Deckfmt;

/// <summary>
/// High-level API for encoding/decoding Altered TCG decklists to/from compact binary format.
/// </summary>
public static class DeckfmtCodec
{
    private static readonly Regex LinePattern = new(@"^(\d+) (\w+)$");

    /// <summary>
    /// Encode a decklist string (one "quantity cardId" per line) to base64url.
    /// </summary>
    public static string EncodeList(string list)
    {
        var lines = list.Split('\n');
        var cards = lines
            .Select(line => line.Trim())
            .Where(line => !string.IsNullOrEmpty(line))
            .Select(line =>
            {
                var match = LinePattern.Match(line);
                if (!match.Success) return null;
                var quantity = int.Parse(match.Groups[1].Value);
                if (quantity <= 0) return null;
                return new CardRefQty(quantity, match.Groups[2].Value);
            })
            .Where(x => x != null)
            .Cast<CardRefQty>()
            .ToList();

        var deck = EncodableDeck.FromList(cards);
        var writer = new BitstreamWriter();
        deck.Encode(writer);
        var bytes = writer.ToArray();
        return Convert.ToBase64String(bytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');
    }

    /// <summary>
    /// Decode a base64url string back to a decklist string.
    /// </summary>
    public static string DecodeList(string encoded)
    {
        var base64 = encoded
            .Replace('-', '+')
            .Replace('_', '/');
        switch (base64.Length % 4)
        {
            case 2: base64 += "=="; break;
            case 3: base64 += "="; break;
        }
        var bytes = Convert.FromBase64String(base64);
        var reader = new BitstreamReader(bytes);
        var deck = EncodableDeck.Decode(reader);
        return string.Join("\n", deck.AsCardRefQty.Select(cq => $"{cq.Quantity} {cq.Id}"));
    }
}
