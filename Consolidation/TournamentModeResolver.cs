namespace GameApi.Consolidation;

/// <summary>
/// Works out a tournament's mode (Frontier, Sealed, All Uniques, ...) from
/// the Format its games carry. A tournament only ever runs one mode, so
/// unlike TournamentNameResolver/mainDeck this needs no majority vote -- the
/// first non-null Format seen is the answer.
/// </summary>
public static class TournamentModeResolver
{
    public static string? Resolve(IEnumerable<string?> formats)
    {
        foreach (var format in formats)
        {
            if (!string.IsNullOrWhiteSpace(format))
            {
                return format;
            }
        }

        return null;
    }
}
