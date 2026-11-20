using WhyDidItBuild.Core;
using WhyDidItBuild.Core.History;
using Xunit;

namespace WhyDidItBuild.Tests;

public class BuildOutputParserTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 19, 19, 0, 0, TimeSpan.Zero);
    private static readonly DateTimeOffset End = Start.AddMinutes(1);

    [Fact]
    public void FirstBuild_CapturesProjectsThatDidWork()
    {
        var report = Parse("""
            1>FastUpToDate: Output 'C:\src\Library\bin\Debug\net10.0\Library.dll' does not exist, not up-to-date. (Library)
            1>------ Build started: Project: Library, Configuration: Debug Any CPU ------
            2>FastUpToDate: Output 'C:\src\App\bin\Debug\net10.0\App.dll' does not exist, not up-to-date. (App)
            2>------ Build started: Project: App, Configuration: Debug Any CPU ------
            ========== Build: 2 succeeded, 0 failed, 0 up-to-date, 0 skipped ==========
            """);

        Assert.Equal(2, report.Projects.Count);
        Assert.All(report.Projects, project => Assert.Equal(ProjectOutcome.SentToMsBuild, project.Outcome));
        Assert.Contains(report.Projects, project => project.FileName == "Library" && project.Reason.Contains("Library.dll"));
        Assert.Contains(report.Projects, project => project.FileName == "App" && project.Reason.Contains("App.dll"));
        Assert.DoesNotContain(report.Projects, project => project.Outcome == ProjectOutcome.UpToDate);
    }

    [Fact]
    public void SecondBuild_ShowsUpToDateOnlyWhenTheCheckSaysSo()
    {
        var report = Parse("""
            1>FastUpToDate: Project is up-to-date. (Library)
            1>FastUpToDate: Up-to-date check completed in 4.2 ms (Library)
            2>FastUpToDate: Input 'C:\src\App\GeneratedRoutes.cs' is newer (2026-11-19 19:02:01.000) than earliest output 'C:\src\App\bin\Debug\net10.0\App.dll' (2026-11-19 19:01:00.000), not up-to-date. (App)
            2>------ Build started: Project: App, Configuration: Debug Any CPU ------
            ========== Build: 1 succeeded, 0 failed, 1 up-to-date, 0 skipped ==========
            """);

        var library = Assert.Single(report.Projects, project => project.FileName == "Library");
        var app = Assert.Single(report.Projects, project => project.FileName == "App");
        Assert.Equal(ProjectOutcome.UpToDate, library.Outcome);
        Assert.Equal("Project is up-to-date.", library.Reason);
        Assert.DoesNotContain("no source changes", library.Reason, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(ProjectOutcome.SentToMsBuild, app.Outcome);
        Assert.Equal("GeneratedRoutes.cs was newer than the last build output.", app.Reason);
        Assert.Equal(@"C:\src\App\GeneratedRoutes.cs", app.RelevantFile);
    }

    [Fact]
    public void UpToDateLineWinsOverBuildStartedAndTheSummaryCount()
    {
        var report = Parse("""
            1>FastUpToDate: Project is up-to-date. (Library)
            1>FastUpToDate: Up-to-date check completed in 110.5 ms (Library)
            1>------ Build started: Project: Library, Configuration: Debug Any CPU ------
            ========== Build: 1 succeeded, 0 failed, 0 up-to-date, 0 skipped ==========
            """);

        var library = Assert.Single(report.Projects);
        Assert.Equal(ProjectOutcome.UpToDate, library.Outcome);
        Assert.Equal(0, report.SummaryUpToDateCount);
    }

    [Fact]
    public void MissingDiagnosticsAreNotUpToDate()
    {
        var report = Parse(
            """
            1>------ Build started: Project: Library, Configuration: Debug Any CPU ------
            Library -> C:\src\Library\bin\Debug\net10.0\Library.dll
            ========== Build: 0 succeeded, 0 failed, 1 up-to-date, 0 skipped ==========
            """,
            new KnownProject
            {
                Name = "Library",
                FileName = "Library",
                FullPath = @"C:\src\Library\Library.csproj",
                Configuration = "Debug|Any CPU",
                SdkStyleCSharp = true
            });

        Assert.False(report.FastUpToDateDiagnosticsPresent);
        Assert.Equal(1, report.SummaryUpToDateCount);
        var library = Assert.Single(report.Projects);
        Assert.Equal(ProjectOutcome.Unknown, library.Outcome);
        Assert.Equal(ReasonText.MissingDiagnostics, library.Reason);
        Assert.Equal(ReasonKind.MissingDiagnostics, library.ReasonKind);
    }

    [Fact]
    public void SummaryAloneDoesNotInventAnUpToDateProject()
    {
        var report = Parse("""
            ========== Build: 0 succeeded, 0 failed, 2 up-to-date, 0 skipped ==========
            """);

        Assert.Empty(report.Projects);
        Assert.Equal(2, report.SummaryUpToDateCount);
    }

    [Fact]
    public void InformationalInputComparisonIsNotAnUpToDateDecision()
    {
        var report = Parse("""
            1>FastUpToDate: No inputs are newer than earliest output 'C:\src\Library\bin\Debug\net10.0\Library.dll' (2026-11-19 19:00:00.000). Newest input is 'C:\src\Library\Class1.cs' (2026-11-19 18:00:00.000). (Library)
            ========== Build: 0 succeeded, 0 failed, 1 up-to-date, 0 skipped ==========
            """);

        var library = Assert.Single(report.Projects);
        Assert.Equal(ProjectOutcome.Unknown, library.Outcome);
        Assert.Contains("No inputs are newer", Assert.Single(library.Evidence));
        Assert.DoesNotContain("no source changes", library.Reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void RepeatedReasonIgnoresTimestampsAndRequiresTheSameFile()
    {
        var first = Parse("""
            1>FastUpToDate: Project is up-to-date. (Library)
            2>FastUpToDate: Input 'C:\src\App\GeneratedRoutes.cs' is newer (2026-11-19 19:01:00.000) than earliest output 'C:\src\App\bin\Debug\net10.0\App.dll' (2026-11-19 19:00:00.000), not up-to-date. (App)
            ========== Build: 1 succeeded, 0 failed, 1 up-to-date, 0 skipped ==========
            """);
        var second = Parse("""
            1>FastUpToDate: Project is up-to-date. (Library)
            2>FastUpToDate: Input 'C:\src\App\GeneratedRoutes.cs' is newer (2026-11-19 19:05:00.000) than earliest output 'C:\src\App\bin\Debug\net10.0\App.dll' (2026-11-19 19:04:00.000), not up-to-date. (App)
            ========== Build: 1 succeeded, 0 failed, 1 up-to-date, 0 skipped ==========
            """);

        var history = new BuildHistory();
        history.Add(first);
        history.Add(second);
        var snapshot = history.Snapshot();
        var latest = snapshot.Latest!;
        Assert.Equal(ReasonText.RepeatedInTwoBuilds, Assert.Single(latest.Projects, project => project.FileName == "App").RepeatedLabel);
        Assert.Equal(ReasonText.RepeatedInTwoBuilds, Assert.Single(latest.Projects, project => project.FileName == "Library").RepeatedLabel);
        Assert.NotNull(snapshot.PreviousComparable);
    }

    [Fact]
    public void DifferentFilesAreNotRepeated()
    {
        var history = new BuildHistory();
        history.Add(Parse("""
            1>FastUpToDate: Input 'C:\src\App\GeneratedRoutes.cs' is newer (2026-11-19 19:01:00.000) than earliest output 'C:\src\App\bin\Debug\net10.0\App.dll' (2026-11-19 19:00:00.000), not up-to-date. (App)
            ========== Build: 1 succeeded, 0 failed, 0 up-to-date, 0 skipped ==========
            """));
        history.Add(Parse("""
            1>FastUpToDate: Input 'C:\src\App\Program.cs' is newer (2026-11-19 19:06:00.000) than earliest output 'C:\src\App\bin\Debug\net10.0\App.dll' (2026-11-19 19:05:00.000), not up-to-date. (App)
            ========== Build: 1 succeeded, 0 failed, 0 up-to-date, 0 skipped ==========
            """));

        Assert.Equal("", Assert.Single(history.Snapshot().Latest!.Projects).RepeatedLabel);
    }

    [Fact]
    public void CleanRebuildAndDesignTimeAreExcludedFromComparison()
    {
        var history = new BuildHistory();
        history.Add(Parse("""
            1>FastUpToDate: Input 'C:\src\App\GeneratedRoutes.cs' is newer (2026-11-19 19:01:00.000) than earliest output 'C:\src\App\bin\Debug\net10.0\App.dll' (2026-11-19 19:00:00.000), not up-to-date. (App)
            ========== Build: 1 succeeded, 0 failed, 0 up-to-date, 0 skipped ==========
            """, BuildKind.Build));
        history.Add(Parse("""
            1>------ Rebuild All started: Project: App, Configuration: Debug Any CPU ------
            ========== Rebuild All: 1 succeeded, 0 failed, 0 skipped ==========
            """, BuildKind.Rebuild));
        history.Add(Parse("""
            1>FastUpToDate: Input 'C:\src\App\GeneratedRoutes.cs' is newer (2026-11-19 19:09:00.000) than earliest output 'C:\src\App\bin\Debug\net10.0\App.dll' (2026-11-19 19:08:00.000), not up-to-date. (App)
            ========== Build: 1 succeeded, 0 failed, 0 up-to-date, 0 skipped ==========
            """, BuildKind.Build));

        var snapshot = history.Snapshot();
        Assert.Equal(BuildKind.Build, snapshot.Latest!.Kind);
        Assert.Equal(ReasonText.RepeatedInTwoBuilds, Assert.Single(snapshot.Latest.Projects).RepeatedLabel);

        history.Add(new BuildReport
        {
            Kind = BuildKind.DesignTime,
            Projects = Array.Empty<ProjectObservation>()
        });
        history.Add(Parse("""
            ========== Clean: 1 succeeded, 0 failed, 0 skipped ==========
            """, BuildKind.Clean));
        Assert.Equal(BuildKind.Clean, history.Snapshot().Latest!.Kind);
        Assert.False(history.Snapshot().Latest!.Comparable);
        Assert.Equal(BuildKind.Build, history.Snapshot().PreviousComparable!.Kind);
    }

    [Fact]
    public void CancelledBuildIsNotComparable()
    {
        var report = BuildOutputParser.Parse(new BuildCapture
        {
            OutputText = """
                1>FastUpToDate: Project is up-to-date. (Library)
                ========== Build: 0 succeeded, 0 failed, 1 up-to-date, 0 skipped ==========
                """,
            Kind = BuildKind.Build,
            Cancelled = true,
            Started = Start,
            Completed = End
        });

        Assert.False(report.Comparable);
        var history = new BuildHistory();
        history.Add(report);
        history.Add(Parse("""
            1>FastUpToDate: Project is up-to-date. (Library)
            ========== Build: 0 succeeded, 0 failed, 1 up-to-date, 0 skipped ==========
            """));
        Assert.Equal("", Assert.Single(history.Snapshot().Latest!.Projects).RepeatedLabel);
    }

    [Fact]
    public void InterleavedOutputStaysWithTheNamedProject()
    {
        var report = Parse("""
            1>FastUpToDate: Input 'C:\src\App\GeneratedRoutes.cs' is newer (2026-11-19 19:02:00.000) than earliest output 'C:\src\App\bin\Debug\net10.0\App.dll' (2026-11-19 19:01:00.000), not up-to-date. (App)
            2>FastUpToDate: Project is up-to-date. (Library)
            1>FastUpToDate: Up-to-date check completed in 3.2 ms (App)
            2>FastUpToDate: Up-to-date check completed in 1.1 ms (Library)
            1>------ Build started: Project: App, Configuration: Debug Any CPU ------
            ========== Build: 1 succeeded, 0 failed, 1 up-to-date, 0 skipped ==========
            """);

        Assert.Equal(ProjectOutcome.SentToMsBuild, Assert.Single(report.Projects, project => project.FileName == "App").Outcome);
        Assert.Equal(ProjectOutcome.UpToDate, Assert.Single(report.Projects, project => project.FileName == "Library").Outcome);
        Assert.Equal(2, Assert.Single(report.Projects, project => project.FileName == "App").Evidence.Count);
    }

    [Fact]
    public void DuplicateProjectNamesStayUnattributed()
    {
        var report = Parse(
            """
            1>FastUpToDate: Project is up-to-date. (App)
            ========== Build: 0 succeeded, 0 failed, 1 up-to-date, 0 skipped ==========
            """,
            known: new[]
            {
                new KnownProject { Name = "App", FileName = "App", FullPath = @"C:\one\App\App.csproj", SdkStyleCSharp = true },
                new KnownProject { Name = "App", FileName = "App", FullPath = @"C:\two\App\App.csproj", SdkStyleCSharp = true }
            });

        Assert.Contains(report.UnattributedEvidence, line => line.Contains("Project is up-to-date", StringComparison.Ordinal));
        Assert.All(report.Projects, project => Assert.Equal(ProjectOutcome.Unknown, project.Outcome));
    }

    [Fact]
    public void TargetFrameworksStayOnSeparateRows()
    {
        var report = Parse("""
            1>FastUpToDate: Checking configuration Debug|AnyCPU|net8.0: (App)
            1>FastUpToDate: Input 'C:\src\App\GeneratedRoutes.cs' is newer (2026-11-19 19:02:00.000) than earliest output 'C:\src\App\bin\Debug\net8.0\App.dll' (2026-11-19 19:01:00.000), not up-to-date. (App)
            1>FastUpToDate: Checking configuration Debug|AnyCPU|net10.0: (App)
            1>FastUpToDate: Project is up-to-date. (App)
            ========== Build: 1 succeeded, 0 failed, 1 up-to-date, 0 skipped ==========
            """);

        Assert.Equal(2, report.Projects.Count);
        var net8 = Assert.Single(report.Projects, project => project.TargetFramework == "net8.0");
        var net10 = Assert.Single(report.Projects, project => project.TargetFramework == "net10.0");
        Assert.Equal(ProjectOutcome.SentToMsBuild, net8.Outcome);
        Assert.Equal(ProjectOutcome.UpToDate, net10.Outcome);
    }

    [Fact]
    public void UnparsedFailureKeepsTheOriginalLine()
    {
        const string Line = "1>FastUpToDate: Something unexpected happened, not up-to-date. (App)";
        var report = Parse(Line + "\n========== Build: 1 succeeded, 0 failed, 0 up-to-date, 0 skipped ==========");
        var app = Assert.Single(report.Projects);
        Assert.Equal(ProjectOutcome.SentToMsBuild, app.Outcome);
        Assert.Equal(ReasonKind.Unparsed, app.ReasonKind);
        Assert.Contains("Something unexpected happened", app.Reason);
        Assert.Contains(Line, app.Evidence);
    }

    [Fact]
    public void BuildOutputCanConfirmCompilerExecutionOrSkip()
    {
        var ran = Parse("""
            1>FastUpToDate: Output 'C:\src\Library\bin\Debug\net10.0\Library.dll' does not exist, not up-to-date. (Library)
            1>------ Build started: Project: Library, Configuration: Debug Any CPU ------
            1>Task "Csc"
            ========== Build: 1 succeeded, 0 failed, 0 up-to-date, 0 skipped ==========
            """);
        Assert.Equal(ProjectOutcome.CompilationRan, Assert.Single(ran.Projects).Outcome);
        Assert.Contains("does not exist", Assert.Single(ran.Projects).Reason);

        var skipped = Parse("""
            1>FastUpToDate: Input 'C:\src\App\GeneratedRoutes.cs' is newer (2026-11-19 19:02:00.000) than earliest output 'C:\src\App\bin\Debug\net10.0\App.dll' (2026-11-19 19:01:00.000), not up-to-date. (App)
            1>------ Build started: Project: App, Configuration: Debug Any CPU ------
            1>Skipping target "CoreCompile" because all output files are up-to-date with respect to the input files.
            ========== Build: 1 succeeded, 0 failed, 0 up-to-date, 0 skipped ==========
            """);
        var app = Assert.Single(skipped.Projects);
        Assert.Equal(ProjectOutcome.CompilationSkipped, app.Outcome);
        Assert.Equal("GeneratedRoutes.cs was newer than the last build output.", app.Reason);
    }

    [Fact]
    public void CompilerEvidenceWithoutAProjectStaysUnattributed()
    {
        var report = Parse("""
            1>Task "Csc"
            ========== Build: 1 succeeded, 0 failed, 0 up-to-date, 0 skipped ==========
            """);

        Assert.Contains(report.UnattributedEvidence, line => line.Contains("Task \"Csc\"", StringComparison.Ordinal));
        Assert.Empty(report.Projects);
    }

    [Fact]
    public void KnownProjectPathIsKeptWhenTheLogUsesTheFileName()
    {
        var report = Parse(
            """
            1>FastUpToDate: Project is up-to-date. (Library)
            ========== Build: 0 succeeded, 0 failed, 1 up-to-date, 0 skipped ==========
            """,
            new KnownProject
            {
                Name = "Library display",
                FileName = "Library",
                FullPath = @"C:\src\Library\Library.csproj",
                Configuration = "Debug|Any CPU",
                SdkStyleCSharp = true
            });

        var library = Assert.Single(report.Projects);
        Assert.Equal(@"C:\src\Library\Library.csproj", library.ProjectPath);
        Assert.Equal("Library display", library.ProjectName);
        Assert.Equal(ProjectOutcome.UpToDate, library.Outcome);
    }

    [Fact]
    public void NonSdkProjectWithoutDiagnosticsSaysSo()
    {
        var report = Parse(
            """
            1>------ Build started: Project: Legacy, Configuration: Debug Any CPU ------
            ========== Build: 1 succeeded, 0 failed, 0 up-to-date, 0 skipped ==========
            """,
            new KnownProject
            {
                Name = "Legacy",
                FileName = "Legacy",
                FullPath = @"C:\src\Legacy\Legacy.csproj",
                SdkStyleCSharp = false
            });

        Assert.Equal(ReasonText.NotSdkStyle, Assert.Single(report.Projects).Reason);
    }

    [Fact]
    public void ImportUsesTheLastBuildInThePaste()
    {
        var report = BuildOutputParser.Parse(new BuildCapture
        {
            Source = BuildSource.ImportedText,
            OutputText = """
                1>FastUpToDate: Project is up-to-date. (Library)
                ========== Build: 0 succeeded, 0 failed, 1 up-to-date, 0 skipped ==========
                1>FastUpToDate: Input 'C:\src\App\GeneratedRoutes.cs' is newer (2026-11-19 19:02:00.000) than earliest output 'C:\src\App\bin\Debug\net10.0\App.dll' (2026-11-19 19:01:00.000), not up-to-date. (App)
                ========== Build: 1 succeeded, 0 failed, 0 up-to-date, 0 skipped ==========
                """
        });

        Assert.Contains("last completed build", report.Note, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("App", Assert.Single(report.Projects).FileName);
        Assert.Equal(ProjectOutcome.SentToMsBuild, report.Projects[0].Outcome);
    }

    [Fact]
    public void TextWithoutABuildSummaryIsNotComparable()
    {
        var report = BuildOutputParser.Parse(new BuildCapture
        {
            Source = BuildSource.ImportedText,
            OutputText = "This paste does not contain a build."
        });

        Assert.Equal(BuildKind.Unknown, report.Kind);
        Assert.False(report.Comparable);
    }

    [Fact]
    public void ReplaceLatestKeepsANewerBuild()
    {
        var history = new BuildHistory();
        var first = Parse("""
            1>FastUpToDate: Project is up-to-date. (Library)
            ========== Build: 0 succeeded, 0 failed, 1 up-to-date, 0 skipped ==========
            """, BuildKind.Build);
        var second = Parse("""
            1>FastUpToDate: Output 'C:\src\App\bin\Debug\net10.0\App.dll' does not exist, not up-to-date. (App)
            ========== Build: 1 succeeded, 0 failed, 0 up-to-date, 0 skipped ==========
            """, BuildKind.Build);
        history.Add(first);
        history.Add(second);

        Assert.False(history.ReplaceLatest(first with { BinlogStatus = "applied to the older build" }));
        var latest = history.Snapshot().Latest;
        Assert.NotNull(latest);
        Assert.Null(latest.BinlogStatus);
        Assert.True(history.ReplaceLatest(second with { BinlogStatus = "applied" }));
        latest = history.Snapshot().Latest;
        Assert.NotNull(latest);
        Assert.Equal("applied", latest.BinlogStatus);
        Assert.Equal(second.Id, latest.Id);
    }

    private static BuildReport Parse(string text, KnownProject known) =>
        Parse(text, BuildKind.Unknown, known);

    private static BuildReport Parse(string text, BuildKind kind = BuildKind.Unknown, params KnownProject[] known)
    {
        return BuildOutputParser.Parse(new BuildCapture
        {
            OutputText = text,
            Kind = kind,
            Source = BuildSource.VisualStudio,
            Started = Start,
            Completed = End,
            KnownProjects = known
        });
    }
}
