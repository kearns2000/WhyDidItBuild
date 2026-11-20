using WhyDidItBuild.Binlog;
using WhyDidItBuild.Core;
using WhyDidItBuild.Core.Correlation;
using Xunit;

namespace WhyDidItBuild.Tests;

public class BinlogCorrelationTests
{
    private static readonly DateTimeOffset Start = new(2026, 11, 19, 19, 0, 0, TimeSpan.Zero);

    [Fact]
    public void MatchingBinlogDistinguishesCompilerExecutionFromAnExplicitSkip()
    {
        var report = Report("""
            1>FastUpToDate: Output 'C:\src\Library\bin\Debug\net10.0\Library.dll' does not exist, not up-to-date. (Library)
            1>------ Build started: Project: Library, Configuration: Debug Any CPU ------
            2>FastUpToDate: Input 'C:\src\App\GeneratedRoutes.cs' is newer (2026-11-19 19:00:30.000) than earliest output 'C:\src\App\bin\Debug\net10.0\App.dll' (2026-11-19 18:00:00.000), not up-to-date. (App)
            2>------ Build started: Project: App, Configuration: Debug Any CPU ------
            ========== Build: 2 succeeded, 0 failed, 0 up-to-date, 0 skipped ==========
            """,
            new KnownProject { Name = "Library", FileName = "Library", FullPath = @"C:\src\Library\Library.csproj", Configuration = "Debug|Any CPU", SdkStyleCSharp = true },
            new KnownProject { Name = "App", FileName = "App", FullPath = @"C:\src\App\App.csproj", Configuration = "Debug|Any CPU", SdkStyleCSharp = true });

        var merged = BinlogCorrelator.Apply(report, @"C:\logs\build.binlog", new[]
        {
            Entry(@"C:\src\Library\Library.csproj", compilerRan: true, "Csc task started (net10.0)."),
            Entry(@"C:\src\App\App.csproj", compilerRan: false, "Skipping target \"CoreCompile\" because all output files are up-to-date with respect to the input files.")
        });

        var library = Assert.Single(merged.Projects, project => project.FileName == "Library");
        var app = Assert.Single(merged.Projects, project => project.FileName == "App");
        Assert.Equal(ProjectOutcome.CompilationRan, library.Outcome);
        Assert.Contains("Library.dll", library.Reason);
        Assert.Contains("Csc task started", library.Evidence[^1]);
        Assert.Equal(ProjectOutcome.CompilationSkipped, app.Outcome);
        Assert.Equal("GeneratedRoutes.cs was newer than the last build output.", app.Reason);
        Assert.Contains("CoreCompile", app.Evidence[^1]);
    }

    [Fact]
    public void AmbiguousBinlogIsNotMerged()
    {
        var report = Report("""
            1>FastUpToDate: Input 'C:\src\App\GeneratedRoutes.cs' is newer (2026-11-19 19:00:30.000) than earliest output 'C:\src\App\bin\Debug\net10.0\App.dll' (2026-11-19 18:00:00.000), not up-to-date. (App)
            ========== Build: 1 succeeded, 0 failed, 0 up-to-date, 0 skipped ==========
            """,
            new KnownProject { Name = "App", FileName = "App", FullPath = @"C:\src\App\App.csproj", Configuration = "Debug|Any CPU", SdkStyleCSharp = true });

        var merged = BinlogCorrelator.Apply(report, @"C:\logs\build.binlog", new[]
        {
            Entry(@"C:\src\App\App.csproj", compilerRan: true, "Csc task started (net10.0)."),
            Entry(@"C:\src\App\App.csproj", compilerRan: false, "Skipping target \"CoreCompile\" because all output files are up-to-date with respect to the input files.")
        });

        var project = Assert.Single(merged.Projects);
        Assert.Equal(ProjectOutcome.SentToMsBuild, project.Outcome);
        Assert.Contains(project.Evidence, line => line.Contains("Csc", StringComparison.Ordinal));
        Assert.Contains(project.Evidence, line => line.Contains("CoreCompile", StringComparison.Ordinal));
        Assert.Contains("ambiguous", merged.BinlogStatus, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void BinlogOutsideTheCaptureWindowIsNotMerged()
    {
        var report = Report("""
            1>FastUpToDate: Input 'C:\src\App\GeneratedRoutes.cs' is newer (2026-11-19 19:00:30.000) than earliest output 'C:\src\App\bin\Debug\net10.0\App.dll' (2026-11-19 18:00:00.000), not up-to-date. (App)
            ========== Build: 1 succeeded, 0 failed, 0 up-to-date, 0 skipped ==========
            """,
            new KnownProject { Name = "App", FileName = "App", FullPath = @"C:\src\App\App.csproj", Configuration = "Debug|Any CPU", SdkStyleCSharp = true });

        var merged = BinlogCorrelator.Apply(report, @"C:\logs\earlier.binlog", new[]
        {
            Entry(@"C:\src\App\App.csproj", compilerRan: true, "Csc task started (net10.0).") with
            {
                Started = Start.AddHours(-2)
            }
        });

        Assert.Equal(ProjectOutcome.SentToMsBuild, Assert.Single(merged.Projects).Outcome);
        Assert.DoesNotContain(Assert.Single(merged.Projects).Evidence, line => line.Contains("Csc task", StringComparison.Ordinal));
    }

    [Fact]
    public void UpToDateProjectDoesNotAbsorbACompilerLog()
    {
        var report = Report("""
            1>FastUpToDate: Project is up-to-date. (Library)
            ========== Build: 0 succeeded, 0 failed, 1 up-to-date, 0 skipped ==========
            """,
            new KnownProject { Name = "Library", FileName = "Library", FullPath = @"C:\src\Library\Library.csproj", Configuration = "Debug|Any CPU", SdkStyleCSharp = true });

        var merged = BinlogCorrelator.Apply(report, @"C:\logs\build.binlog", new[]
        {
            Entry(@"C:\src\Library\Library.csproj", compilerRan: false, "Skipping target \"CoreCompile\" because all output files are up-to-date with respect to the input files.")
        });

        var project = Assert.Single(merged.Projects);
        Assert.Equal(ProjectOutcome.UpToDate, project.Outcome);
        Assert.Contains(project.Evidence, line => line.Contains("CoreCompile", StringComparison.Ordinal));
    }

    [Fact]
    public void PreviouslyBuiltSkipDoesNotCancelCompilerExecution()
    {
        var report = Report("""
            1>FastUpToDate: Output 'C:\src\App\bin\Debug\net10.0\App.dll' does not exist, not up-to-date. (App)
            ========== Build: 1 succeeded, 0 failed, 0 up-to-date, 0 skipped ==========
            """,
            new KnownProject { Name = "App", FileName = "App", FullPath = @"C:\src\App\App.csproj", Configuration = "Debug|Any CPU", SdkStyleCSharp = true });

        var merged = BinlogCorrelator.Apply(report, @"C:\logs\build.binlog", new[]
        {
            Entry(@"C:\src\App\App.csproj", compilerRan: true, "Csc task started (net10.0)."),
            Entry(@"C:\src\App\App.csproj", compilerRan: false, "Target \"CoreCompile\" skipped. Previously built successfully.") with
            {
                CompilationSkipped = false
            }
        });

        Assert.Equal(ProjectOutcome.CompilationRan, Assert.Single(merged.Projects).Outcome);
    }

    [Fact]
    public void BinlogWithoutATargetFrameworkDoesNotMergeIntoANamedFramework()
    {
        var report = new BuildReport
        {
            Kind = BuildKind.Build,
            Source = BuildSource.VisualStudio,
            Started = Start,
            Completed = Start.AddMinutes(1),
            FastUpToDateDiagnosticsPresent = true,
            Projects = new[]
            {
                new ProjectObservation
                {
                    ProjectName = "App",
                    FileName = "App",
                    ProjectPath = @"C:\src\App\App.csproj",
                    Configuration = "Debug|Any CPU",
                    TargetFramework = "net8.0",
                    Outcome = ProjectOutcome.SentToMsBuild,
                    ReasonKind = ReasonKind.InputNewerThanOutput,
                    Reason = "GeneratedRoutes.cs was newer than the last build output.",
                    Evidence = new[] { "FastUpToDate: Input 'GeneratedRoutes.cs' is newer, not up-to-date. (App)" }
                }
            }
        };

        var merged = BinlogCorrelator.Apply(report, @"C:\logs\build.binlog", new[]
        {
            Entry(@"C:\src\App\App.csproj", compilerRan: true, "Csc task started.") with { TargetFramework = null }
        });

        var project = Assert.Single(merged.Projects);
        Assert.Equal(ProjectOutcome.SentToMsBuild, project.Outcome);
        Assert.DoesNotContain(project.Evidence, line => line.Contains("Csc task", StringComparison.Ordinal));
    }

    [Fact]
    public async Task SampleBuildsProduceCompilerExecutionThenAnExplicitSkip()
    {
        var sample = FindSample();
        var temp = Path.Combine(Path.GetTempPath(), "why-did-it-build-" + Guid.NewGuid().ToString("N"));
        CopySample(sample, temp);
        try
        {
            var first = Path.Combine(temp, "first.binlog");
            var second = Path.Combine(temp, "second.binlog");
            await RunDotnet(temp, $"build RecurringBuild.sln -bl:\"{first}\" --nologo");
            await RunDotnet(temp, $"build RecurringBuild.sln -bl:\"{second}\" --nologo");

            var firstProjects = BinlogReader.Read(first);
            var secondProjects = BinlogReader.Read(second);

            Assert.Contains(firstProjects, project => IsProject(project, "Library.csproj") && project.CompilerRan && !project.CompilationSkipped);
            Assert.Contains(firstProjects, project => IsProject(project, "App.csproj") && project.CompilerRan && !project.CompilationSkipped);

            var secondLibraryBuilds = secondProjects.Where(project => IsProject(project, "Library.csproj") && (project.CompilerRan || project.CompilationSkipped)).ToList();
            var secondAppBuilds = secondProjects.Where(project => IsProject(project, "App.csproj") && (project.CompilerRan || project.CompilationSkipped)).ToList();
            Assert.NotEmpty(secondLibraryBuilds);
            Assert.NotEmpty(secondAppBuilds);
            Assert.All(secondLibraryBuilds, project => Assert.True(project.CompilationSkipped && !project.CompilerRan));
            Assert.All(secondAppBuilds, project => Assert.True(project.CompilationSkipped && !project.CompilerRan));
            var secondLibrary = secondLibraryBuilds[0];
            var secondApp = secondAppBuilds[0];

            var windowStart = secondApp.Started!.Value.AddSeconds(-5);
            var windowEnd = secondApp.Started.Value.AddMinutes(2);
            var captured = new BuildReport
            {
                Kind = BuildKind.Build,
                Source = BuildSource.VisualStudio,
                Started = windowStart,
                Completed = windowEnd,
                FastUpToDateDiagnosticsPresent = true,
                Projects = new[]
                {
                    new ProjectObservation
                    {
                        ProjectName = "Library",
                        FileName = "Library",
                        ProjectPath = secondLibrary.ProjectPath,
                        Configuration = "Debug|Any CPU",
                        Outcome = ProjectOutcome.UpToDate,
                        ReasonKind = ReasonKind.UpToDate,
                        Reason = "Project is up-to-date.",
                        Evidence = new[] { "FastUpToDate: Project is up-to-date. (Library)" }
                    },
                    new ProjectObservation
                    {
                        ProjectName = "App",
                        FileName = "App",
                        ProjectPath = secondApp.ProjectPath,
                        Configuration = "Debug|Any CPU",
                        Outcome = ProjectOutcome.SentToMsBuild,
                        ReasonKind = ReasonKind.InputNewerThanOutput,
                        Reason = "GeneratedRoutes.cs was newer than the last build output.",
                        RelevantFile = Path.Combine(temp, "App", "GeneratedRoutes.cs"),
                        Evidence = new[] { "FastUpToDate: Input 'GeneratedRoutes.cs' is newer than earliest output, not up-to-date. (App)" }
                    }
                }
            };

            var merged = BinlogCorrelator.Apply(captured, second, secondProjects);
            Assert.Equal(ProjectOutcome.UpToDate, Assert.Single(merged.Projects, project => project.FileName == "Library").Outcome);
            Assert.Equal(ProjectOutcome.CompilationSkipped, Assert.Single(merged.Projects, project => project.FileName == "App").Outcome);
        }
        finally
        {
            try
            {
                Directory.Delete(temp, recursive: true);
            }
            catch (IOException)
            {
            }
        }
    }

    private static BuildReport Report(string text, params KnownProject[] known)
    {
        return BuildOutputParser.Parse(new BuildCapture
        {
            OutputText = text,
            Kind = BuildKind.Build,
            Source = BuildSource.VisualStudio,
            Started = Start,
            Completed = Start.AddMinutes(1),
            KnownProjects = known
        });
    }

    private static BinlogProjectBuild Entry(string path, bool compilerRan, string evidence)
    {
        return new BinlogProjectBuild
        {
            ProjectPath = path,
            Configuration = "Debug",
            Platform = "AnyCPU",
            TargetFramework = "net10.0",
            Started = Start.AddSeconds(10),
            CompilerRan = compilerRan,
            CompilationSkipped = !compilerRan,
            Evidence = new[] { evidence }
        };
    }

    private static bool IsProject(BinlogProjectBuild project, string fileName) =>
        project.ProjectPath.EndsWith(fileName, StringComparison.OrdinalIgnoreCase);

    private static string FindSample()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(directory.FullName, "samples", "RecurringBuild");
            if (File.Exists(Path.Combine(candidate, "RecurringBuild.sln")))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new InvalidOperationException("Could not find samples/RecurringBuild.");
    }

    private static void CopySample(string source, string destination)
    {
        foreach (var file in Directory.EnumerateFiles(source, "*", SearchOption.AllDirectories))
        {
            var relative = Path.GetRelativePath(source, file);
            if (relative.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar).Any(part => part is "bin" or "obj"))
            {
                continue;
            }

            var target = Path.Combine(destination, relative);
            Directory.CreateDirectory(Path.GetDirectoryName(target)!);
            File.Copy(file, target);
        }
    }

    private static async Task RunDotnet(string workingDirectory, string arguments)
    {
        var start = new System.Diagnostics.ProcessStartInfo("dotnet", arguments + " -nodeReuse:false")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false
        };
        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        using var process = System.Diagnostics.Process.Start(start) ?? throw new InvalidOperationException("Could not start dotnet.");
        var stdoutTask = process.StandardOutput.ReadToEndAsync();
        var stderrTask = process.StandardError.ReadToEndAsync();
        await process.WaitForExitAsync();
        var stdout = await stdoutTask;
        var stderr = await stderrTask;
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"dotnet {arguments} failed:{Environment.NewLine}{stdout}{Environment.NewLine}{stderr}");
        }
    }
}
