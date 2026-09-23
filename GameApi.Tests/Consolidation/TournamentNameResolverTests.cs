using GameApi.Consolidation;

namespace GameApi.Tests.Consolidation;

public sealed class TournamentNameResolverTests
{
    [Fact]
    public void Resolve_StripsTheExtensionSharedStagesHangOff()
    {
        var name = TournamentNameResolver.Resolve([
            "Winter Cup - Round 1",
            "Winter Cup - Round 2",
            "Winter Cup - Final",
        ]);

        Assert.Equal("Winter Cup", name);
    }

    [Theory]
    // Whatever character BGA hangs the extension off, the tournament's name
    // is what's left once it's trimmed back.
    [InlineData("Winter Cup #1", "Winter Cup #2", "Winter Cup")]
    [InlineData("Winter Cup (A)", "Winter Cup (B)", "Winter Cup")]
    [InlineData("Winter Cup: Swiss", "Winter Cup: Top 8", "Winter Cup")]
    [InlineData("Winter Cup_1", "Winter Cup_2", "Winter Cup")]
    [InlineData("Winter Cup / A", "Winter Cup / B", "Winter Cup")]
    public void Resolve_TrimsEverySeparatorAnExtensionHangsOff(string first, string second, string expected)
    {
        Assert.Equal(expected, TournamentNameResolver.Resolve([first, second]));
    }

    [Fact]
    public void Resolve_KeepsASingleNameWhole()
    {
        // Nothing to compare it against means nothing is an extension -- a
        // tournament that is its own parent lands here.
        Assert.Equal("Winter Cup", TournamentNameResolver.Resolve(["Winter Cup"]));
    }

    [Fact]
    public void Resolve_PrefersTheFirstName_WhenStagesShareNothingMeaningful()
    {
        // Callers pass the parent's own name first for exactly this case.
        var name = TournamentNameResolver.Resolve(["Winter Cup", "Coupe d'hiver"]);

        Assert.Equal("Winter Cup", name);
    }

    [Fact]
    public void Resolve_DoesNotCollapseUnrelatedNamesIntoAStrayFragment()
    {
        // Both start with "C", which is coincidence, not a tournament name.
        var name = TournamentNameResolver.Resolve(["Coupe A", "Challenge B"]);

        Assert.Equal("Coupe A", name);
    }

    [Fact]
    public void Resolve_IgnoresMissingAndBlankNames()
    {
        Assert.Equal("Winter Cup", TournamentNameResolver.Resolve([null, "  ", "Winter Cup", null]));
        Assert.Null(TournamentNameResolver.Resolve([null, "   "]));
        Assert.Null(TournamentNameResolver.Resolve([]));
    }

    [Fact]
    public void Resolve_TreatsRepeatedNamesAsOne()
    {
        // Every stage carrying the identical name must not be mistaken for a
        // prefix that needs trimming.
        Assert.Equal("Winter Cup", TournamentNameResolver.Resolve(["Winter Cup", "Winter Cup", "Winter Cup"]));
    }
}
