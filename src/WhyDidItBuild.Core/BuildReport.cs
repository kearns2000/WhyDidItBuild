namespace WhyDidItBuild.Core;

public sealed record BuildReport
{
    public Guid Id { get; init; } = Guid.NewGuid();

    public string? SolutionPath { get; init; }

    public BuildKind Kind { get; init; }

    public bool Cancelled { get; init; }

    public BuildSource Source { get; init; }

    public DateTimeOffset? Started { get; init; }

    public DateTimeOffset? Completed { get; init; }

    public bool FastUpToDateDiagnosticsPresent { get; init; }

    public string? OutputSummary { get; init; }

    public int? SummaryUpToDateCount { get; init; }

    public string? Note { get; init; }

    public string? BinlogPath { get; init; }

    public string? BinlogStatus { get; init; }

    public IReadOnlyList<ProjectObservation> Projects { get; init; } = Array.Empty<ProjectObservation>();

    public IReadOnlyList<string> UnattributedEvidence { get; init; } = Array.Empty<string>();

    public bool Comparable =>
        Kind == BuildKind.Build
        && !Cancelled
        && Kind != BuildKind.DesignTime;

    public string KindLabel => Kind switch
    {
        BuildKind.Build => "Build",
        BuildKind.Rebuild => "Rebuild",
        BuildKind.Clean => "Clean",
        BuildKind.Deploy => "Deploy",
        BuildKind.Launch => "Launch",
        BuildKind.DesignTime => "Design-time build",
        _ => "Unrecognized build"
    };
}
