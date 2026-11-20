namespace WhyDidItBuild.Core.Correlation;

public static class BinlogCorrelator
{
    public static readonly TimeSpan TimeTolerance = TimeSpan.FromSeconds(5);

    public static BuildReport Apply(BuildReport report, string binlogPath, IReadOnlyList<BinlogProjectBuild> projects)
    {
        var unattributed = report.UnattributedEvidence.ToList();
        var notes = new List<string>();
        var used = new HashSet<BinlogProjectBuild>();
        var updated = new List<ProjectObservation>(report.Projects.Count);

        foreach (var project in report.Projects)
        {
            var candidates = projects.Where(candidate => Matches(report, project, candidate)).ToList();
            var informative = candidates.Where(candidate => candidate.CompilerRan || candidate.CompilationSkipped).ToList();
            var pool = informative.Count > 0 ? informative : candidates;
            if (pool.Count == 0)
            {
                updated.Add(project);
                continue;
            }

            var sawCompiler = pool.Any(candidate => candidate.CompilerRan);
            var sawSkip = pool.Any(candidate => candidate.CompilationSkipped && !candidate.CompilerRan);
            var conflicting = pool.Any(candidate => candidate.CompilerRan && candidate.CompilationSkipped) || (sawCompiler && sawSkip);
            var noCompilerEvidence = pool.All(candidate => !candidate.CompilerRan && !candidate.CompilationSkipped);
            if (conflicting || (pool.Count > 1 && noCompilerEvidence))
            {
                notes.Add($"MSBuild log entries for {project.ProjectLabel} were ambiguous, so they were not merged into the project outcome.");
                var evidence = project.Evidence.Concat(pool.SelectMany(candidate => candidate.Evidence)).Distinct().ToList();
                foreach (var candidate in pool)
                {
                    used.Add(candidate);
                }

                updated.Add(project with { Evidence = evidence });
                continue;
            }

            var match = Combine(pool);
            foreach (var candidate in pool)
            {
                used.Add(candidate);
            }

            if (project.Outcome == ProjectOutcome.UpToDate && (match.CompilerRan || match.CompilationSkipped))
            {
                notes.Add($"{project.ProjectLabel} was up to date in the Fast Up-to-Date Check, so the MSBuild log entry was not merged.");
                updated.Add(project with { Evidence = project.Evidence.Concat(match.Evidence).Distinct().ToList() });
                continue;
            }

            updated.Add(Merge(project, match));
        }

        if (updated.All(project => project.Outcome == report.Projects.FirstOrDefault(original => SameRow(original, project))?.Outcome)
            && notes.Count == 0
            && projects.Count > 0
            && used.Count == 0)
        {
            notes.Add("The MSBuild log did not match this build's projects and time, so it was not merged.");
        }
        else if (used.Count > 0 && notes.Count == 0)
        {
            notes.Add("A matching MSBuild log was applied.");
        }

        var status = notes.Count == 0
            ? "The MSBuild log did not contain project builds to apply."
            : string.Join(" ", notes);

        return report with
        {
            Projects = updated,
            UnattributedEvidence = unattributed,
            BinlogPath = binlogPath,
            BinlogStatus = status
        };
    }

    private static bool Matches(BuildReport report, ProjectObservation project, BinlogProjectBuild candidate)
    {
        if (!PathsEqual(project.ProjectPath, candidate.ProjectPath)
            && !FileNamesEqual(project, candidate.ProjectPath))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(project.ProjectPath)
            && !string.IsNullOrWhiteSpace(candidate.ProjectPath)
            && !PathsEqual(project.ProjectPath, candidate.ProjectPath))
        {
            return false;
        }

        if (!TextNormalizer.ConfigurationsMatch(project.Configuration, CombineConfiguration(candidate)))
        {
            return false;
        }

        if (!string.IsNullOrWhiteSpace(project.TargetFramework)
            && !string.Equals(project.TargetFramework, candidate.TargetFramework, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        if (report.Started is null || report.Completed is null || candidate.Started is null)
        {
            return report.Source == BuildSource.ImportedText;
        }

        var start = report.Started.Value - TimeTolerance;
        var end = report.Completed.Value + TimeTolerance;
        return candidate.Started.Value >= start && candidate.Started.Value <= end;
    }

    private static BinlogProjectBuild Combine(IReadOnlyList<BinlogProjectBuild> pool)
    {
        var first = pool[0];
        return first with
        {
            CompilerRan = pool.Any(candidate => candidate.CompilerRan),
            CompilationSkipped = pool.Any(candidate => candidate.CompilationSkipped) && pool.All(candidate => !candidate.CompilerRan),
            Evidence = pool.SelectMany(candidate => candidate.Evidence).Distinct().ToList()
        };
    }

    private static ProjectObservation Merge(ProjectObservation project, BinlogProjectBuild match)
    {
        var evidence = project.Evidence.Concat(match.Evidence).ToList();
        if (match.CompilerRan)
        {
            var reason = project.ReasonKind == ReasonKind.MissingDiagnostics
                ? ReasonText.CompilerRanWithoutCheck
                : project.Reason;
            var kind = project.ReasonKind == ReasonKind.MissingDiagnostics ? ReasonKind.Unparsed : project.ReasonKind;
            return project with
            {
                Outcome = ProjectOutcome.CompilationRan,
                Reason = reason,
                ReasonKind = kind,
                Evidence = evidence,
                TargetFramework = project.TargetFramework ?? match.TargetFramework
            };
        }

        if (match.CompilationSkipped)
        {
            var reason = project.ReasonKind == ReasonKind.MissingDiagnostics
                ? ReasonText.CompilerSkippedWithoutCheck
                : project.Reason;
            var kind = project.ReasonKind == ReasonKind.MissingDiagnostics ? ReasonKind.Unparsed : project.ReasonKind;
            return project with
            {
                Outcome = ProjectOutcome.CompilationSkipped,
                Reason = reason,
                ReasonKind = kind,
                Evidence = evidence,
                TargetFramework = project.TargetFramework ?? match.TargetFramework
            };
        }

        return project with { Evidence = evidence };
    }

    private static bool SameRow(ProjectObservation left, ProjectObservation right) =>
        string.Equals(left.ProjectPath, right.ProjectPath, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.FileName, right.FileName, StringComparison.OrdinalIgnoreCase)
        && string.Equals(left.TargetFramework ?? "", right.TargetFramework ?? "", StringComparison.OrdinalIgnoreCase);

    private static string CombineConfiguration(BinlogProjectBuild candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate.Configuration))
        {
            return candidate.Platform ?? "";
        }

        if (string.IsNullOrWhiteSpace(candidate.Platform))
        {
            return candidate.Configuration;
        }

        return candidate.Configuration + "|" + candidate.Platform;
    }

    private static bool PathsEqual(string? left, string? right) =>
        !string.IsNullOrWhiteSpace(left)
        && !string.IsNullOrWhiteSpace(right)
        && string.Equals(TextNormalizer.NormalizePath(left), TextNormalizer.NormalizePath(right), StringComparison.Ordinal);

    private static bool FileNamesEqual(ProjectObservation project, string candidatePath)
    {
        if (!string.IsNullOrWhiteSpace(project.ProjectPath))
        {
            return false;
        }

        var candidateName = Path.GetFileNameWithoutExtension(candidatePath);
        return string.Equals(project.FileName, candidateName, StringComparison.OrdinalIgnoreCase);
    }
}
