namespace WhyDidItBuild.Core;

public enum ProjectOutcome
{
    Unknown = 0,
    UpToDate,
    SentToMsBuild,
    CompilationRan,
    CompilationSkipped
}

public static class ProjectOutcomeLabels
{
    public static string ToLabel(this ProjectOutcome outcome) => outcome switch
    {
        ProjectOutcome.UpToDate => "Up to date",
        ProjectOutcome.SentToMsBuild => "Sent to MSBuild",
        ProjectOutcome.CompilationRan => "Compilation ran",
        ProjectOutcome.CompilationSkipped => "Compilation skipped",
        _ => "Unknown"
    };
}
