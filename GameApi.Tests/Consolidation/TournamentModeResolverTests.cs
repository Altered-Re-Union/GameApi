using GameApi.Consolidation;

namespace GameApi.Tests.Consolidation;

public sealed class TournamentModeResolverTests
{
    [Fact]
    public void Resolve_ReturnsTheFirstNonNullFormat()
    {
        var mode = TournamentModeResolver.Resolve([null, "Frontier", "Sealed"]);

        Assert.Equal("Frontier", mode);
    }

    [Fact]
    public void Resolve_SkipsBlankFormats()
    {
        var mode = TournamentModeResolver.Resolve(["", "   ", "All Uniques"]);

        Assert.Equal("All Uniques", mode);
    }

    [Fact]
    public void Resolve_ReturnsNull_WhenNoFormatIsPresent()
    {
        var mode = TournamentModeResolver.Resolve([null, "", "  "]);

        Assert.Null(mode);
    }
}
