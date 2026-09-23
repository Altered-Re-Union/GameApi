using System.Reflection;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace AlteredBgaApi.Deckfmt;

/// <summary>
/// Loads the mapping tables (sets, factions, rarities, products) that
/// EncodableCard/CardRefElements need, from the embedded Deckfmt/Config/v*.json
/// files -- one file per wire-format version, discovered by scanning this
/// assembly's embedded resources so a new version needs nothing but a new
/// json file (see README for what still requires touching the structural
/// Encode/Decode code: a genuinely new bit layout, not just new values).
///
/// Adding a set/faction/rarity/product that fits the CURRENT bit-packing
/// scheme is a data-only change: add it to the latest version's json. A
/// structural change (different bit widths, a new "has extra field" rule --
/// see InstanceNumberAppliesToAllRarities) needs a new version file, because
/// a blob already encoded under an older version must keep decoding exactly
/// as it always did.
/// </summary>
public sealed class DeckfmtConfig
{
    public int Version { get; }

    /// <summary>
    /// v1 only ever reads the 16-bit instance number when Rarity == Unique
    /// (RarityId 3) -- any other rarity's card id carrying a trailing number
    /// (e.g. a serialized/numbered non-unique print) silently loses it. v2
    /// makes the instance number an explicit flag independent of rarity, so
    /// any rarity can carry one.
    /// </summary>
    public bool InstanceNumberAppliesToAllRarities { get; }

    public sealed record SetInfo(int Id, string Code, int NumberInFactionBits, bool LegacyRarityBits);

    private readonly Dictionary<int, SetInfo> _setsById;
    private readonly Dictionary<string, SetInfo> _setsByCode;
    private readonly Dictionary<int, string> _factionCodeById;
    private readonly Dictionary<string, int> _factionIdByCode;
    private readonly Dictionary<int, string> _rarityCodeById;
    private readonly Dictionary<string, int> _rarityIdByCode;
    private readonly Dictionary<int, string> _productCodeById;
    private readonly Dictionary<string, int> _productIdByCode;

    private DeckfmtConfig(Dto dto)
    {
        Version = dto.Version;
        InstanceNumberAppliesToAllRarities = dto.InstanceNumberAppliesToAllRarities;

        _setsById = dto.Sets.ToDictionary(
            s => s.Id, s => new SetInfo(s.Id, s.Code, s.NumberInFactionBits, s.LegacyRarityBits));
        _setsByCode = dto.Sets.ToDictionary(
            s => s.Code, s => new SetInfo(s.Id, s.Code, s.NumberInFactionBits, s.LegacyRarityBits));
        _factionCodeById = dto.Factions.ToDictionary(f => f.Id, f => f.Code);
        _factionIdByCode = dto.Factions.ToDictionary(f => f.Code, f => f.Id);
        _rarityCodeById = dto.Rarities.ToDictionary(r => r.Id, r => r.Code);
        _rarityIdByCode = dto.Rarities.ToDictionary(r => r.Code, r => r.Id);
        _productCodeById = dto.Products.ToDictionary(p => p.Id, p => p.Code);
        _productIdByCode = dto.Products.ToDictionary(p => p.Code, p => p.Id);
    }

    public bool HasSetId(int id) => _setsById.ContainsKey(id);

    public bool HasProductId(int id) => _productCodeById.ContainsKey(id);

    public SetInfo SetById(int id) =>
        _setsById.TryGetValue(id, out var set)
            ? set
            : throw new DecodingException($"Invalid SetCode ID ({id})");

    public SetInfo SetByCode(string code) =>
        _setsByCode.TryGetValue(code, out var set)
            ? set
            : throw new ArgumentException($"Unrecognized set code: {code}");

    public string FactionCode(int id) =>
        _factionCodeById.TryGetValue(id, out var code)
            ? code
            : throw new EncodingException($"Invalid faction: {id}");

    public int FactionId(string code) =>
        _factionIdByCode.TryGetValue(code, out var id)
            ? id
            : throw new ArgumentException($"Unrecognized faction: {code}");

    public string RarityCode(int id) =>
        _rarityCodeById.TryGetValue(id, out var code)
            ? code
            : throw new EncodingException($"Invalid rarity: {id}");

    public int RarityId(string code) =>
        _rarityIdByCode.TryGetValue(code, out var id)
            ? id
            : throw new ArgumentException($"Unrecognized rarity: {code}");

    /// <summary>Booster (the default product) is null and isn't in the table -- "B" maps to it.</summary>
    public string? ProductCode(int? id) => id switch
    {
        null => "B",
        _ when _productCodeById.TryGetValue(id.Value, out var code) => code,
        _ => throw new EncodingException($"Invalid product: {id}"),
    };

    public int? ProductId(string code) => code switch
    {
        "B" => null,
        _ when _productIdByCode.TryGetValue(code, out var id) => id,
        _ => throw new ArgumentException($"Unrecognized product: {code}"),
    };

    private static readonly Dictionary<int, DeckfmtConfig> Versions = LoadAll();

    public static int Latest { get; } = Versions.Keys.Max();

    public static bool IsKnownVersion(int version) => Versions.ContainsKey(version);

    public static DeckfmtConfig For(int version) =>
        Versions.TryGetValue(version, out var config)
            ? config
            : throw new DecodingException($"Invalid version ({version})");

    private static Dictionary<int, DeckfmtConfig> LoadAll()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var options = new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

        var configs = new Dictionary<int, DeckfmtConfig>();
        foreach (var resourceName in assembly.GetManifestResourceNames())
        {
            if (!resourceName.Contains(".Deckfmt.Config.", StringComparison.Ordinal) ||
                !resourceName.EndsWith(".json", StringComparison.Ordinal))
            {
                continue;
            }

            using var stream = assembly.GetManifestResourceStream(resourceName)
                ?? throw new InvalidOperationException($"Could not open embedded resource {resourceName}");
            var dto = JsonSerializer.Deserialize<Dto>(stream, options)
                ?? throw new InvalidOperationException($"Empty deckfmt config resource {resourceName}");
            configs[dto.Version] = new DeckfmtConfig(dto);
        }

        if (configs.Count == 0)
        {
            throw new InvalidOperationException("No Deckfmt/Config/v*.json embedded resources were found.");
        }

        return configs;
    }

    private sealed class Dto
    {
        public int Version { get; init; }
        public bool InstanceNumberAppliesToAllRarities { get; init; }
        public List<ProductDto> Products { get; init; } = [];
        public List<FactionDto> Factions { get; init; } = [];
        public List<RarityDto> Rarities { get; init; } = [];
        public List<SetDto> Sets { get; init; } = [];
    }

    private sealed class ProductDto
    {
        public int Id { get; init; }
        public string Code { get; init; } = "";
    }

    private sealed class FactionDto
    {
        public int Id { get; init; }
        public string Code { get; init; } = "";
    }

    private sealed class RarityDto
    {
        public int Id { get; init; }
        public string Code { get; init; } = "";
    }

    private sealed class SetDto
    {
        public int Id { get; init; }
        public string Code { get; init; } = "";
        public int NumberInFactionBits { get; init; }
        public bool LegacyRarityBits { get; init; }
    }
}
