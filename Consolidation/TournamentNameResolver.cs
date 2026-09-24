namespace GameApi.Consolidation;

/// <summary>
/// Works out what to call a tournament from the names its stages carry.
///
/// BGA hangs a "- &lt;stage/round/group/...&gt;" extension off the parent's
/// own name for every stage that has a real parent -- a Game whose
/// TournamentId differs from its TournamentParentId -- whatever that
/// extension actually says (language included: "Round 2", "Étape 2", "Group
/// A" all cut the same way), so the parent's name is recovered by cutting at
/// the *last* " -" in the stage's own name. Cutting per-stage rather than
/// hunting for a prefix shared across every stage is what makes this hold up
/// when stages only share a short, generic tail ("... - Stage 1" / "... -
/// Stage 2" no longer collapses to the stray fragment "... - Stage").
///
/// A stage whose TournamentId *is* its TournamentParentId is the parent
/// itself, not a guess -- BGA never hangs an extension off a tournament's own
/// name -- so its name is preferred verbatim over any cut stage name whenever
/// one is present in the group.
/// </summary>
public static class TournamentNameResolver
{
    public static string? Resolve(IEnumerable<(long? TournamentId, long? TournamentParentId, string? TournamentName)> stages)
    {
        var named = stages
            .Where(stage => !string.IsNullOrWhiteSpace(stage.TournamentName))
            .Select(stage => (stage.TournamentId, stage.TournamentParentId, Name: stage.TournamentName!.Trim()))
            .ToList();

        if (named.Count == 0)
        {
            return null;
        }

        var selfParented = named.FirstOrDefault(stage => stage.TournamentId is not null && stage.TournamentId == stage.TournamentParentId);
        if (selfParented.Name is not null)
        {
            return selfParented.Name;
        }

        var guesses = named.Select(stage => StripStageSuffix(stage.Name)).ToList();

        // Ties broken by first-seen order (the order `named` is already in,
        // since Games is queried ordered by ReceivedAt/TableId upstream) --
        // same convention ConsolidationPass uses for a tied main deck.
        return guesses
            .GroupBy(guess => guess, StringComparer.Ordinal)
            .OrderByDescending(group => group.Count())
            .ThenBy(group => guesses.IndexOf(group.Key))
            .First()
            .Key;
    }

    /// <summary>Cuts a stage name at its last " -"; a name with no such separator has nothing to strip and is kept whole.</summary>
    private static string StripStageSuffix(string name)
    {
        var cutIndex = name.LastIndexOf(" -", StringComparison.Ordinal);
        return cutIndex < 0 ? name : name[..cutIndex].TrimEnd();
    }
}
