namespace WhyDidItBuild.Core;

public sealed record ProjectObservation
{
    public required string ProjectName { get; init; }

    public required string FileName { get; init; }

    public string? ProjectPath { get; init; }

    public string Configuration { get; init; } = "";

    public string? TargetFramework { get; init; }

    public ProjectOutcome Outcome { get; init; }

    public ReasonKind ReasonKind { get; init; }

    public string Reason { get; init; } = "";

    public string? RelevantFile { get; init; }

    /// <summary>
    /// Normalized file identity used when the same reason mentions more than one file.
    /// </summary>
    public string RepeatFiles { get; init; } = "";

    public string RepeatedLabel { get; init; } = "";

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();

    public string ProjectLabel
    {
        get
        {
            var framework = string.IsNullOrEmpty(TargetFramework) ? "" : $" ({TargetFramework})";
            var configuration = string.IsNullOrEmpty(Configuration) ? "" : $" — {Configuration}";
            return ProjectName + framework + configuration;
        }
    }

    public string RepeatKey
    {
        get
        {
            if (ReasonKind is ReasonKind.MissingDiagnostics or ReasonKind.None)
            {
                return "";
            }

            var project = TextNormalizer.NormalizePath(ProjectPath ?? FileName);
            var configuration = TextNormalizer.NormalizeConfiguration(Configuration);
            var framework = (TargetFramework ?? "").Trim().ToLowerInvariant();
            var files = RepeatFiles.Length > 0
                ? RepeatFiles
                : TextNormalizer.NormalizePath(RelevantFile);
            var detail = ReasonKind == ReasonKind.Unparsed
                ? TextNormalizer.StripVolatileTokens(Reason)
                : ReasonKind.ToString();
            return string.Join("|", project, configuration, framework, detail, files);
        }
    }
}
