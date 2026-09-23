namespace AlteredBgaApi.Deckfmt;

public class EncodableCard
{
    public int SetCode { get; set; }
    public int? Product { get; set; }
    public int Faction { get; set; }
    public int NumberInFaction { get; set; }
    public int Rarity { get; set; }
    public int? UniqueId { get; set; }

    /// <summary>
    /// The deckfmt wire-format version this card was decoded from (or, for a
    /// freshly-encoded card, DeckfmtConfig.Latest) -- drives which mapping
    /// tables and bit-packing rules AsCardId()/Encode() use, so a card
    /// decoded from an old blob renders/re-encodes exactly as that version
    /// intended, not however the newest version would.
    /// </summary>
    public int Version { get; set; } = DeckfmtConfig.Latest;

    public static EncodableCard Decode(BitstreamReader reader, DecodingContext context)
    {
        var self = new EncodableCard();
        if (context.SetCode == null)
            throw new DecodingException("Tried to decode card without SetCode in context");

        self.SetCode = context.SetCode.Value;
        self.Version = context.Version;
        var config = DeckfmtConfig.For(context.Version);

        var productBit = reader.ReadSync(1);
        if (productBit == 1)
        {
            self.Product = null;
        }
        else
        {
            self.Product = (int)reader.ReadSync(2);
            if (!config.HasProductId(self.Product.Value))
                throw new DecodingException($"Invalid product ID ({self.Product})");
        }

        self.Faction = (int)reader.ReadSync(3);
        if (self.Faction == 0)
            throw new DecodingException($"Invalid faction ID ({self.Faction})");

        var setInfo = config.SetById(self.SetCode);
        self.NumberInFaction = (int)reader.ReadSync(setInfo.NumberInFactionBits);

        var rarityBitLength = setInfo.LegacyRarityBits ? 2 : 3;
        self.Rarity = (int)reader.ReadSync(rarityBitLength);

        var uniqueRarityId = config.RarityId("U");
        var hasInstanceNumber = config.InstanceNumberAppliesToAllRarities
            ? reader.ReadSync(1) == 1
            : self.Rarity == uniqueRarityId;
        if (hasInstanceNumber)
        {
            self.UniqueId = (int)reader.ReadSync(16);
        }

        return self;
    }

    public void Encode(BitstreamWriter writer)
    {
        var config = DeckfmtConfig.For(Version);

        if (Product == null)
        {
            writer.Write(1, 1);
        }
        else
        {
            writer.Write(1, 0);
            writer.Write(2, (uint)Product.Value);
        }

        writer.Write(3, (uint)Faction);

        var setInfo = config.SetById(SetCode);
        var nifBitLength = setInfo.NumberInFactionBits;
        if (NumberInFaction >= (1 << nifBitLength))
            throw new EncodingException($"Family ID out of range ({NumberInFaction}) for set {SetCode} (max: {(1 << nifBitLength) - 1})");
        writer.Write(nifBitLength, (uint)NumberInFaction);

        var rarityBitLength = setInfo.LegacyRarityBits ? 2 : 3;
        writer.Write(rarityBitLength, (uint)Rarity);

        if (config.InstanceNumberAppliesToAllRarities)
        {
            writer.Write(1, UniqueId.HasValue ? 1u : 0u);
        }

        if (UniqueId.HasValue)
        {
            if (UniqueId.Value > 0xFFFF)
                throw new EncodingException("Cannot encode unique ID greater than 65535");
            writer.Write(16, (uint)UniqueId.Value);
        }
    }

    public string AsCardId()
    {
        var config = DeckfmtConfig.For(Version);
        var setInfo = config.SetById(SetCode);
        var rarityCode = config.RarityCode(Rarity);

        var id = $"ALT_{setInfo.Code}_{config.ProductCode(Product)}_{config.FactionCode(Faction)}_";

        // Special case: CoreKS' NE_1 (Mana Convergence) does not use a 0
        // prefix, unlike every other card on that set -- confirmed against
        // the real card catalog, where this is the only such exception
        // (Core's own NE_1 *does* use the 0 prefix, unlike CoreKS').
        if (NumberInFaction < 10 && !(Faction == config.FactionId("NE") && SetCode == config.SetByCode("COREKS").Id))
        {
            id += "0";
        }
        id += NumberInFaction;

        id += $"_{rarityCode}";
        if (rarityCode == "U")
        {
            id += $"_{UniqueId}";
        }
        else if (UniqueId == 0)
        {
            // 0 is never a genuine serial (DUSTERCB-style numbers start at
            // 1) -- it's the sentinel for the card catalog's "XXX" unnumbered
            // print template, see CardRefElements.
            id += "_XXX";
        }
        else if (UniqueId.HasValue)
        {
            id += $"_{UniqueId.Value:000}";
        }

        return id;
    }

    public static EncodableCard FromId(string id)
    {
        var refElements = new CardRefElements(id);
        return new EncodableCard
        {
            SetCode = refElements.SetCode,
            Product = refElements.Product,
            Faction = refElements.Faction,
            NumberInFaction = refElements.NumInFaction,
            Rarity = refElements.Rarity,
            UniqueId = refElements.UniqNum,
            Version = DeckfmtConfig.Latest,
        };
    }
}

public class EncodableCardQty
{
    public int Quantity { get; set; }
    public EncodableCard Card { get; set; } = new();

    public static EncodableCardQty Decode(BitstreamReader reader, DecodingContext context)
    {
        var self = new EncodableCardQty();
        var simpleQty = (int)reader.ReadSync(2);
        if (simpleQty > 0)
        {
            self.Quantity = simpleQty;
        }
        else
        {
            var extended = (int)reader.ReadSync(6);
            self.Quantity = extended == 0 ? 0 : extended + 3;
        }
        self.Card = EncodableCard.Decode(reader, context);
        return self;
    }

    public void Encode(BitstreamWriter writer)
    {
        if (Quantity > 0 && Quantity <= 3)
        {
            writer.Write(2, (uint)Quantity);
        }
        else if (Quantity > 3)
        {
            if (Quantity > 65)
                throw new EncodingException($"Cannot encode card quantity ({Quantity}) greater than 65");
            writer.Write(2, 0);
            writer.Write(6, (uint)(Quantity - 3));
        }
        else
        {
            // qty=0 is written with 2 zeroes followed by 6 zeroes
            writer.Write(8, 0);
        }

        Card.Encode(writer);
    }

    public CardRefQty AsCardRefQty => new(Quantity, Card.AsCardId());

    public static EncodableCardQty From(int quantity, string card) => new()
    {
        Quantity = quantity,
        Card = EncodableCard.FromId(card)
    };
}

public class EncodableSetGroup
{
    public int SetCode { get; set; }
    public List<EncodableCardQty> CardQty { get; set; } = new();

    public static EncodableSetGroup Decode(BitstreamReader reader, DecodingContext context)
    {
        var self = new EncodableSetGroup();
        self.SetCode = (int)reader.ReadSync(8);

        var config = DeckfmtConfig.For(context.Version);
        if (!config.HasSetId(self.SetCode))
            throw new DecodingException($"Invalid SetCode ID ({self.SetCode}) @offset={reader.Offset}");

        context.SetCode = self.SetCode;

        var cardRefCount = (int)reader.ReadSync(6);
        for (var i = 0; i < cardRefCount; i++)
        {
            self.CardQty.Add(EncodableCardQty.Decode(reader, context));
        }

        context.SetCode = null;
        return self;
    }

    public void Encode(BitstreamWriter writer)
    {
        if (CardQty.Count == 0)
            throw new EncodingException("Cannot encode a SetGroup with 0 cards");

        var setCode = CardQty[0].Card.SetCode;
        writer.Write(8, (uint)setCode);
        writer.Write(6, (uint)CardQty.Count);
        foreach (var cardQty in CardQty)
        {
            cardQty.Encode(writer);
        }
    }

    public static EncodableSetGroup From(IReadOnlyList<CardRefQty> rqs)
    {
        return new EncodableSetGroup
        {
            CardQty = rqs.Select(rq => EncodableCardQty.From(rq.Quantity, rq.Id)).ToList()
        };
    }

}

public class EncodableDeck
{
    public int Version { get; set; } = DeckfmtConfig.Latest;
    public List<EncodableSetGroup> SetGroups { get; set; } = new();

    public static EncodableDeck Decode(BitstreamReader reader)
    {
        var self = new EncodableDeck();

        self.Version = (int)reader.ReadSync(4);
        if (!DeckfmtConfig.IsKnownVersion(self.Version))
            throw new DecodingException($"Invalid version ({self.Version})");

        var context = new DecodingContext { Version = self.Version };

        var groupsCount = (int)reader.ReadSync(8);
        for (var i = 0; i < groupsCount; i++)
        {
            self.SetGroups.Add(EncodableSetGroup.Decode(reader, context));
        }

        return self;
    }

    public void Encode(BitstreamWriter writer)
    {
        writer.Write(4, (uint)Version);
        writer.Write(8, (uint)SetGroups.Count);
        foreach (var group in SetGroups)
        {
            group.Encode(writer);
        }

        if (writer.Offset % 8 > 0)
        {
            var padding = 8 - writer.Offset % 8;
            writer.Write(padding, 0);
        }
    }

    public IReadOnlyList<CardRefQty> AsCardRefQty =>
        SetGroups.SelectMany(g => g.CardQty.Select(cq => cq.AsCardRefQty)).ToList();

    public static EncodableDeck FromList(IReadOnlyList<CardRefQty> refQtyList)
    {
        var groups = GroupedBySet(refQtyList)
            .Select(g => SplitIntoGroupsOf(g, 63)
                .Select(block => EncodableSetGroup.From(block)).ToList())
            .SelectMany(g => g)
            .ToList();

        return new EncodableDeck
        {
            Version = DeckfmtConfig.Latest,
            SetGroups = groups
        };
    }

    private static IEnumerable<IGrouping<string, CardRefQty>> GroupedBySet(IReadOnlyList<CardRefQty> refQtyList)
    {
        return refQtyList
            .GroupBy(rq => new CardRefElements(rq.Id).SetCode.ToString());
    }

    private static IEnumerable<List<CardRefQty>> SplitIntoGroupsOf(IEnumerable<CardRefQty> array, int maxGroupSize)
    {
        var list = array.ToList();
        for (var i = 0; i < list.Count; i += maxGroupSize)
        {
            yield return list.Skip(i).Take(maxGroupSize).ToList();
        }
    }
}

public class DecodingContext
{
    public int Version { get; set; }
    public int? SetCode { get; set; }
}

public class DecodingException : Exception
{
    public DecodingException(string message) : base(message) { }
}

public class EncodingException : Exception
{
    public EncodingException(string message) : base(message) { }
}
