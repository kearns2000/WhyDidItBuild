namespace WhyDidItBuild.Core;

public enum ReasonKind
{
    None = 0,
    MissingDiagnostics,
    UpToDate,
    InputNewerThanOutput,
    InputModifiedSinceBuild,
    OutputMissing,
    InputMissing,
    ItemsChanged,
    FirstRun,
    CheckDisabled,
    CriticalTasks,
    CopySourceNewer,
    CopyDestinationMissing,
    CopySourceMissing,
    CopyAlwaysDiffers,
    MarkerNewer,
    Exception,
    Unparsed
}

public static class ReasonText
{
    public const string MissingDiagnostics = "No Fast Up-to-Date Check diagnostics were captured for this project.";

    public const string NotSdkStyle = "No Fast Up-to-Date Check diagnostics were captured. Those diagnostics are written for SDK-style projects.";

    public const string RepeatedInTwoBuilds = "Repeated in 2 builds";

    public const string CompilerRanWithoutCheck =
        "A C# compiler task ran. The Fast Up-to-Date Check did not record why Visual Studio invoked MSBuild.";

    public const string CompilerSkippedWithoutCheck =
        "MSBuild skipped compilation. The Fast Up-to-Date Check did not record why Visual Studio invoked MSBuild.";
}
