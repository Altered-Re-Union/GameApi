using GameApi.Consolidation;

namespace GameApi.Tests.Consolidation;

public sealed class TournamentNameResolverTests
{
    [Fact]
    public void Resolve_StripsTheLastDashSuffix_OffAChildStage()
    {
        var name = Resolve((601, 500, "Winter Cup - Round 1"));

        Assert.Equal("Winter Cup", name);
    }

    [Fact]
    public void Resolve_OnlyCutsAtTheLastDash_WhenTheParentsOwnNameAlsoContainsOne()
    {
        // The real case that broke the old shared-prefix resolver: the
        // suffix is just "Stage 2", but the parent's own name legitimately
        // has dashes in it too.
        var name = Resolve((602988, 602986, "Monday Night Topcut - Season 1 - Week 3 - Stage 2"));

        Assert.Equal("Monday Night Topcut - Season 1 - Week 3", name);
    }

    [Fact]
    public void Resolve_AgreesAcrossStagesWithDifferentGenericSuffixes()
    {
        // The old bug: a shared prefix across "... - Stage 1" / "... - Stage
        // 2" left the stray fragment "... - Stage". Cutting per-stage instead
        // of hunting for a shared prefix means both agree on the same name.
        var name = Resolve(
            (601, 500, "Winter Cup - Stage 1"),
            (602, 500, "Winter Cup - Stage 2"));

        Assert.Equal("Winter Cup", name);
    }

    [Fact]
    public void Resolve_IsAgnosticToWhatTheSuffixSays_LanguageIncluded()
    {
        var name = Resolve(
            (601, 500, "Winter Cup - Group A"),
            (602, 500, "Winter Cup - Étape 2"));

        Assert.Equal("Winter Cup", name);
    }

    [Fact]
    public void Resolve_PrefersTheSelfParentedStagesNameVerbatim_OverAnyGuess()
    {
        // TournamentId == TournamentParentId means this stage IS the parent
        // -- not a guess to be cut, even if a sibling's cut guess disagrees.
        var name = Resolve(
            (500, 500, "Winter Cup"),
            (601, 500, "Winter Cup - Round 1"));

        Assert.Equal("Winter Cup", name);
    }

    [Fact]
    public void Resolve_KeepsAChildStagesNameWhole_WhenItHasNoDashSuffix()
    {
        var name = Resolve((601, 500, "RoundRobin1"));

        Assert.Equal("RoundRobin1", name);
    }

    [Fact]
    public void Resolve_PicksTheMostCommonGuess_WhenChildStagesDisagree()
    {
        var name = Resolve(
            (601, 500, "Winter Cup - Round 1"),
            (602, 500, "Winter Cup - Round 2"),
            (603, 500, "Winter Classic - Final")); // a mislabeled outlier

        Assert.Equal("Winter Cup", name);
    }

    [Fact]
    public void Resolve_IgnoresMissingAndBlankNames()
    {
        Assert.Equal("Winter Cup", Resolve((601, 500, null), (602, 500, "  "), (603, 500, "Winter Cup - Final"), (604, 500, null)));
        Assert.Null(Resolve((601, 500, null), (602, 500, "   ")));
        Assert.Null(TournamentNameResolver.Resolve([]));
    }

    private static string? Resolve(params (long? TournamentId, long? TournamentParentId, string? TournamentName)[] stages) =>
        TournamentNameResolver.Resolve(stages);
}
