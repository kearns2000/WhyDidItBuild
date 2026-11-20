namespace WhyDidItBuild.Core;

public sealed record BuildCapture
{
    public string OutputText { get; init; } = "";

    public string? SolutionPath { get; init; }

    public BuildKind Kind { get; init; } = BuildKind.Unknown;

    public bool Cancelled { get; init; }

    public DateTimeOffset? Started { get; init; }

    public DateTimeOffset? Completed { get; init; }

    public BuildSource Source { get; init; } = BuildSource.VisualStudio;

    public IReadOnlyList<KnownProject> KnownProjects { get; init; } = Array.Empty<KnownProject>();
}
