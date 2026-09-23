namespace GameApi.Consolidation;

/// <summary>Configured under "Consolidation" (ConsolidationOptions.SectionName); every knob has a working default, so the section is optional.</summary>
public sealed class ConsolidationOptions
{
    public const string SectionName = "Consolidation";

    public bool Enabled { get; set; } = true;

    public TimeSpan Interval { get; set; } = TimeSpan.FromMinutes(5);

    public int PageSize { get; set; } = 200;

    /// <summary>Run a pass immediately at startup, so a deploy picks up whatever landed while the process was down.</summary>
    public bool RunAtStartup { get; set; } = true;
}
