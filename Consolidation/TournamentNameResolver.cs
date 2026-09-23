namespace GameApi.Consolidation;

/// <summary>
/// Works out what to call a tournament from the names its stages carry.
/// Ported from altered-bga-api's (now-deleted) TournamentReport.TournamentName
/// -- see that repo's git history -- since consolidation, this logic
/// included, now lives entirely in GameApi.
///
/// BGA names every stage after the tournament plus an extension ("Winter Cup
/// - Round 1", "Winter Cup - Round 2"), so the tournament's own name is the
/// longest prefix they all share, with the separator the extension hangs off
/// trimmed back. A tournament that is its own parent has no extension to
/// strip, and falls out of the same rule for free.
/// </summary>
public static class TournamentNameResolver
{
    /// <summary>
    /// Characters an extension is hung off, trimmed from the end of a common
    /// prefix so "Winter Cup - " comes back as "Winter Cup".
    /// </summary>
    private static readonly char[] TrailingSeparators =
        [' ', '\t', '-', '–', '—', ':', ';', ',', '#', '.', '_', '/', '|', '(', '[', '{'];

    /// <summary>
    /// A shared prefix shorter than this is taken as coincidence -- two
    /// unrelated tournaments both starting with "C" shouldn't collapse into a
    /// tournament named "C" -- and the first candidate is used instead.
    /// </summary>
    private const int MinimumPrefixLength = 3;

    public static string? Resolve(IEnumerable<string?> stageNames)
    {
        var candidates = stageNames
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Select(name => name!.Trim())
            .Distinct(StringComparer.Ordinal)
            .ToList();

        if (candidates.Count == 0)
        {
            return null;
        }

        // One name can't have an extension distinguishing it from anything, so
        // there is nothing to strip: it is the name.
        if (candidates.Count == 1)
        {
            return candidates[0];
        }

        var prefix = candidates.Aggregate(CommonPrefix).TrimEnd(TrailingSeparators);

        return prefix.Length >= MinimumPrefixLength ? prefix : candidates[0];
    }

    private static string CommonPrefix(string left, string right)
    {
        var length = 0;
        var shortest = Math.Min(left.Length, right.Length);

        while (length < shortest && left[length] == right[length])
        {
            length++;
        }

        return left[..length];
    }
}
