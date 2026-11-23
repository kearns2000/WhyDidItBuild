using System.Text.RegularExpressions;

namespace WhyDidItBuild.Core;

public static partial class BuildOutputParser
{
    public static BuildReport Parse(BuildCapture capture)
    {
        var segments = SplitOnSummaries(capture.OutputText ?? "");
        var chosen = segments.Count == 0
            ? new TextSegment("", null, null)
            : segments[^1];
        var note = segments.Count > 1
            ? "This output contains more than one build. Only the last completed build is shown."
            : null;

        var kind = capture.Kind == BuildKind.Unknown
            ? InferKind(chosen)
            : capture.Kind;

        var projects = new Dictionary<string, ProjectBucket>(StringComparer.OrdinalIgnoreCase);
        var unattributed = new List<string>();
        var slots = new Dictionary<string, SlotHint>(StringComparer.Ordinal);
        string? summary = chosen.Summary;
        int? summaryUpToDate = chosen.UpToDateCount;
        var sawFastUpToDate = false;

        foreach (var known in capture.KnownProjects)
        {
            var bucket = GetOrAdd(projects, known.FileName, known.Name, known.FullPath, known.Configuration);
            if (bucket is null)
            {
                continue;
            }

            bucket.SdkStyleCSharp ??= known.SdkStyleCSharp;
            if (!string.IsNullOrWhiteSpace(known.FullPath))
            {
                bucket.FullPath ??= known.FullPath;
            }

            if (!string.IsNullOrWhiteSpace(known.Configuration))
            {
                bucket.Configuration ??= known.Configuration;
            }

            bucket.FromSolutionBuild = true;
        }

        foreach (var rawLine in EnumerateLines(chosen.Body))
        {
            if (string.IsNullOrWhiteSpace(rawLine))
            {
                continue;
            }

            var line = rawLine.TrimEnd();
            if (SummaryPattern().IsMatch(line.Trim()))
            {
                continue;
            }

            var (slot, content) = SplitSlot(line);
            if (TryReadBuildStarted(content, out var startedName, out var startedConfig))
            {
                var hint = slots.GetValueOrDefault(slot ?? "") ?? new SlotHint();
                hint.FileName = startedName;
                hint.Configuration = startedConfig;
                if (slot is not null)
                {
                    slots[slot] = hint;
                }

                var bucket = GetOrAdd(projects, startedName, startedName, hint.FullPath, startedConfig);
                if (bucket is not null)
                {
                    bucket.Configuration ??= startedConfig;
                    bucket.SawBuildStarted = true;
                }

                continue;
            }

            if (TryReadProjectPath(content, out var projectPath))
            {
                var fileName = TextNormalizer.FileNameOf(projectPath);
                fileName = Path.GetFileNameWithoutExtension(fileName);
                if (slot is not null)
                {
                    var hint = slots.GetValueOrDefault(slot) ?? new SlotHint();
                    hint.FullPath = projectPath;
                    hint.FileName ??= fileName;
                    slots[slot] = hint;
                }

                if (!string.IsNullOrEmpty(fileName))
                {
                    var bucket = FindUnique(projects, fileName);
                    if (bucket is not null)
                    {
                        bucket.FullPath ??= projectPath;
                    }
                }

                continue;
            }

            if (TryReadFastUpToDate(content, out var fastMessage, out var fastProject))
            {
                sawFastUpToDate = true;
                AttributeFastLine(projects, unattributed, slots, slot, line, fastMessage, fastProject, capture.KnownProjects);
                continue;
            }

            if (TryReadCompilerEvidence(content, out var compilerRan, out var compilerSkipped, out var compilerText))
            {
                AttributeCompilerLine(projects, unattributed, slots, slot, line, compilerRan, compilerSkipped, compilerText);
                continue;
            }
        }

        var observations = new List<ProjectObservation>();
        foreach (var bucket in projects.Values.OrderBy(item => item.DisplayName, StringComparer.OrdinalIgnoreCase))
        {
            observations.AddRange(bucket.ToObservations());
            if (bucket.UnmergedCompilerLines.Count > 0)
            {
                unattributed.AddRange(bucket.UnmergedCompilerLines);
            }
        }

        return new BuildReport
        {
            SolutionPath = capture.SolutionPath,
            Kind = kind,
            Cancelled = capture.Cancelled,
            Source = capture.Source,
            Started = capture.Started,
            Completed = capture.Completed,
            FastUpToDateDiagnosticsPresent = sawFastUpToDate,
            OutputSummary = summary,
            SummaryUpToDateCount = summaryUpToDate,
            Note = note,
            Projects = observations,
            UnattributedEvidence = unattributed
        };
    }

    private static void AttributeFastLine(
        Dictionary<string, ProjectBucket> projects,
        List<string> unattributed,
        Dictionary<string, SlotHint> slots,
        string? slot,
        string originalLine,
        string message,
        string? projectSuffix,
        IReadOnlyList<KnownProject> knownProjects)
    {
        var fileName = projectSuffix;
        if (string.IsNullOrWhiteSpace(fileName) && slot is not null && slots.TryGetValue(slot, out var hint))
        {
            fileName = hint.FileName;
        }

        if (string.IsNullOrWhiteSpace(fileName))
        {
            unattributed.Add(originalLine);
            return;
        }

        var matches = projects.Values.Where(item => NamesEqual(item.FileName, fileName)).ToList();
        string? pathFromSlot = null;
        string? configuration = null;
        if (slot is not null && slots.TryGetValue(slot, out var slotHint))
        {
            pathFromSlot = slotHint.FullPath;
            configuration = slotHint.Configuration;
        }

        if (!string.IsNullOrWhiteSpace(pathFromSlot))
        {
            var pathMatches = matches.Where(item => PathsEqual(item.FullPath, pathFromSlot)).ToList();
            if (pathMatches.Count == 1)
            {
                pathMatches[0].AddFast(message, originalLine);
                return;
            }

            if (matches.Count > 1)
            {
                unattributed.Add(originalLine);
                return;
            }
        }

        if (matches.Count > 1)
        {
            unattributed.Add(originalLine);
            return;
        }

        if (matches.Count == 1)
        {
            matches[0].AddFast(message, originalLine);
            return;
        }

        var known = knownProjects.Where(item => NamesEqual(item.FileName, fileName)).ToList();
        if (known.Count > 1 && string.IsNullOrWhiteSpace(pathFromSlot))
        {
            unattributed.Add(originalLine);
            return;
        }

        var knownOne = known.Count == 1 ? known[0] : null;
        var bucket = GetOrAdd(
            projects,
            fileName,
            knownOne?.Name ?? fileName,
            knownOne?.FullPath ?? pathFromSlot,
            knownOne?.Configuration ?? configuration);
        if (bucket is null)
        {
            unattributed.Add(originalLine);
            return;
        }

        bucket.SdkStyleCSharp ??= knownOne?.SdkStyleCSharp;
        bucket.AddFast(message, originalLine);
    }

    private static void AttributeCompilerLine(
        Dictionary<string, ProjectBucket> projects,
        List<string> unattributed,
        Dictionary<string, SlotHint> slots,
        string? slot,
        string originalLine,
        bool compilerRan,
        bool compilerSkipped,
        string evidenceText)
    {
        if (slot is null || !slots.TryGetValue(slot, out var hint) || string.IsNullOrWhiteSpace(hint.FileName))
        {
            unattributed.Add(originalLine);
            return;
        }

        var matches = projects.Values.Where(item => NamesEqual(item.FileName, hint.FileName)).ToList();
        if (!string.IsNullOrWhiteSpace(hint.FullPath))
        {
            var pathMatches = matches.Where(item => item.FullPath is null || PathsEqual(item.FullPath, hint.FullPath)).ToList();
            if (pathMatches.Count == 1)
            {
                pathMatches[0].AddCompiler(compilerRan, compilerSkipped, evidenceText, originalLine);
                return;
            }
        }

        if (matches.Count == 1)
        {
            matches[0].AddCompiler(compilerRan, compilerSkipped, evidenceText, originalLine);
            return;
        }

        unattributed.Add(originalLine);
    }

    private static ProjectBucket? GetOrAdd(
        Dictionary<string, ProjectBucket> projects,
        string fileName,
        string displayName,
        string? fullPath,
        string? configuration)
    {
        var sameName = projects.Values.Where(item => NamesEqual(item.FileName, fileName)).ToList();
        if (!string.IsNullOrWhiteSpace(fullPath))
        {
            var pathMatch = sameName.FirstOrDefault(item => PathsEqual(item.FullPath, fullPath));
            if (pathMatch is not null)
            {
                Touch(pathMatch, displayName, fullPath, configuration);
                return pathMatch;
            }
        }

        if (sameName.Count == 1
            && (string.IsNullOrWhiteSpace(fullPath)
                || sameName[0].FullPath is null
                || PathsEqual(sameName[0].FullPath, fullPath)))
        {
            Touch(sameName[0], displayName, fullPath, configuration);
            return sameName[0];
        }

        if (sameName.Count > 1 && string.IsNullOrWhiteSpace(fullPath))
        {
            return null;
        }

        var created = new ProjectBucket(string.IsNullOrWhiteSpace(displayName) ? fileName : displayName, fileName);
        Touch(created, displayName, fullPath, configuration);
        var key = !string.IsNullOrWhiteSpace(created.FullPath)
            ? TextNormalizer.NormalizePath(created.FullPath)
            : "name:" + fileName.ToLowerInvariant() + "|" + projects.Count.ToString();
        projects[key] = created;
        return created;
    }

    private static void Touch(ProjectBucket bucket, string displayName, string? fullPath, string? configuration)
    {
        if (!string.IsNullOrWhiteSpace(fullPath))
        {
            bucket.FullPath ??= fullPath;
        }

        if (!string.IsNullOrWhiteSpace(configuration))
        {
            bucket.Configuration ??= configuration;
        }

        if (!string.IsNullOrWhiteSpace(displayName)
            && bucket.DisplayName == bucket.FileName
            && !string.Equals(displayName, bucket.FileName, StringComparison.OrdinalIgnoreCase))
        {
            bucket.DisplayName = displayName;
        }
    }

    private static ProjectBucket? FindUnique(Dictionary<string, ProjectBucket> projects, string fileName)
    {
        var matches = projects.Values.Where(item => NamesEqual(item.FileName, fileName)).ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool NamesEqual(string left, string right) =>
        string.Equals(left, right, StringComparison.OrdinalIgnoreCase);

    private static bool PathsEqual(string? left, string? right) =>
        string.Equals(TextNormalizer.NormalizePath(left), TextNormalizer.NormalizePath(right), StringComparison.Ordinal);

    private static IEnumerable<string> EnumerateLines(string text)
    {
        using var reader = new StringReader(text);
        while (reader.ReadLine() is { } line)
        {
            yield return line;
        }
    }

    private static (string? Slot, string Content) SplitSlot(string line)
    {
        var match = SlotPattern().Match(line.TrimStart());
        if (!match.Success)
        {
            return (null, line.Trim());
        }

        return (match.Groups["slot"].Value, match.Groups["rest"].Value.Trim());
    }

    private static bool TryReadFastUpToDate(string content, out string message, out string? project)
    {
        message = "";
        project = null;
        var match = FastLineWithProjectPattern().Match(content);
        if (match.Success)
        {
            message = match.Groups["message"].Value.Trim();
            project = match.Groups["project"].Value.Trim();
            return project.Length > 0;
        }

        match = FastLinePlainPattern().Match(content);
        if (!match.Success)
        {
            return false;
        }

        message = match.Groups["message"].Value.Trim();
        return true;
    }

    private static bool TryReadBuildStarted(string content, out string name, out string configuration)
    {
        var match = BuildStartedPattern().Match(content);
        name = match.Success ? match.Groups["name"].Value.Trim() : "";
        configuration = match.Success ? match.Groups["config"].Value.Trim() : "";
        return match.Success;
    }

    private static bool TryReadProjectPath(string content, out string path)
    {
        var match = ProjectPathPattern().Match(content);
        path = match.Success ? match.Groups["path"].Value.Trim() : "";
        return match.Success && path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase);
    }

    private static bool TryReadCompilerEvidence(string content, out bool compilerRan, out bool compilerSkipped, out string evidence)
    {
        compilerRan = false;
        compilerSkipped = false;
        evidence = content;
        var task = TaskPattern().Match(content);
        if (task.Success && string.Equals(task.Groups["name"].Value, "Csc", StringComparison.Ordinal))
        {
            compilerRan = true;
            evidence = "Task \"Csc\" ran.";
            return true;
        }

        var skipped = SkipTargetPattern().Match(content);
        if (skipped.Success && string.Equals(skipped.Groups["name"].Value, "CoreCompile", StringComparison.Ordinal))
        {
            compilerSkipped = true;
            evidence = content;
            return true;
        }

        var targetSkipped = TargetSkippedPattern().Match(content);
        if (targetSkipped.Success && string.Equals(targetSkipped.Groups["name"].Value, "CoreCompile", StringComparison.Ordinal))
        {
            compilerSkipped = true;
            evidence = content;
            return true;
        }

        return false;
    }

    private static BuildKind InferKind(TextSegment segment)
    {
        var summary = segment.Summary ?? "";
        if (summary.StartsWith("Rebuild", StringComparison.OrdinalIgnoreCase)
            || segment.Body.Contains("Rebuild All started:", StringComparison.OrdinalIgnoreCase))
        {
            return BuildKind.Rebuild;
        }

        if (summary.StartsWith("Clean", StringComparison.OrdinalIgnoreCase)
            || segment.Body.Contains("Clean started:", StringComparison.OrdinalIgnoreCase))
        {
            return BuildKind.Clean;
        }

        if (summary.StartsWith("Deploy", StringComparison.OrdinalIgnoreCase))
        {
            return BuildKind.Deploy;
        }

        if (summary.StartsWith("Build", StringComparison.OrdinalIgnoreCase)
            || segment.Body.Contains("Build started:", StringComparison.OrdinalIgnoreCase))
        {
            return BuildKind.Build;
        }

        return BuildKind.Unknown;
    }

    private static List<TextSegment> SplitOnSummaries(string text)
    {
        var lines = EnumerateLines(text).ToList();
        var segments = new List<TextSegment>();
        var body = new List<string>();
        foreach (var line in lines)
        {
            var match = SummaryPattern().Match(line.Trim());
            if (!match.Success)
            {
                body.Add(line);
                continue;
            }

            var title = match.Groups["title"].Value.Trim();
            int? upToDate = null;
            var count = UpToDateCountPattern().Match(title);
            if (count.Success && int.TryParse(count.Groups["uptodate"].Value, out var parsed))
            {
                upToDate = parsed;
            }

            segments.Add(new TextSegment(string.Join('\n', body), title, upToDate));
            body.Clear();
        }

        if (body.Count > 0 && body.Any(line => !string.IsNullOrWhiteSpace(line)))
        {
            if (segments.Count == 0)
            {
                segments.Add(new TextSegment(string.Join('\n', body), null, null));
            }
            else
            {
                var previous = segments[^1];
                segments[^1] = previous with { Body = previous.Body + "\n" + string.Join('\n', body) };
            }
        }

        return segments;
    }

    private sealed record TextSegment(string Body, string? Summary, int? UpToDateCount);

    private sealed class SlotHint
    {
        public string? FileName { get; set; }

        public string? FullPath { get; set; }

        public string? Configuration { get; set; }
    }

    private sealed class ProjectBucket
    {
        private readonly Dictionary<string, Section> _sections = new(StringComparer.OrdinalIgnoreCase);
        private string _currentFramework = "";

        public ProjectBucket(string displayName, string fileName)
        {
            DisplayName = displayName;
            FileName = fileName;
        }

        public string DisplayName { get; set; }

        public string FileName { get; }

        public string? FullPath { get; set; }

        public string? Configuration { get; set; }

        public bool? SdkStyleCSharp { get; set; }

        public bool FromSolutionBuild { get; set; }

        public bool SawBuildStarted { get; set; }

        public void AddFast(string message, string originalLine)
        {
            if (FastUpToDateInterpreter.TryReadConfiguration(message, out var configuration, out var framework))
            {
                _currentFramework = framework ?? "";
                if (!string.IsNullOrWhiteSpace(framework))
                {
                    var parts = configuration.Split('|');
                    if (parts.Length >= 2)
                    {
                        configuration = string.Join("|", parts.Take(parts.Length - 1));
                    }
                }

                if (!string.IsNullOrWhiteSpace(configuration))
                {
                    Configuration ??= configuration;
                }

                SectionFor(_currentFramework).Evidence.Add(originalLine);
                return;
            }

            var section = SectionFor(_currentFramework);
            section.Evidence.Add(originalLine);
            if (FastUpToDateInterpreter.TryReadChangedItem(message, out var changed))
            {
                section.ChangedItems.Add(changed);
            }

            var acceleration = FastUpToDateInterpreter.TryReadAcceleration(message);
            if (acceleration is not null)
            {
                section.AccelerationCount = acceleration;
            }

            var interpreted = FastUpToDateInterpreter.Interpret(message);
            if (!interpreted.Decisive || section.Decision is not null)
            {
                return;
            }

            section.Decision = interpreted;
        }

        public void AddCompiler(bool ran, bool skipped, string evidenceText, string originalLine)
        {
            if (_sections.Count > 1)
            {
                PendingCompiler.Add((ran, skipped, evidenceText, originalLine));
                return;
            }

            var section = SectionFor(_currentFramework);
            ApplyCompiler(section, ran, skipped, evidenceText, originalLine);
        }

        private readonly List<(bool Ran, bool Skipped, string Text, string Line)> PendingCompiler = new();

        public IEnumerable<ProjectObservation> ToObservations()
        {
            if (_sections.Count == 1 && PendingCompiler.Count > 0)
            {
                var only = _sections.Values.First();
                foreach (var pending in PendingCompiler)
                {
                    ApplyCompiler(only, pending.Ran, pending.Skipped, pending.Text, pending.Line);
                }

                PendingCompiler.Clear();
            }

            if (_sections.Count == 0)
            {
                yield return Create(null);
                yield break;
            }

            foreach (var pair in _sections)
            {
                yield return Create(pair.Value, pair.Key);
            }
        }

        public IReadOnlyList<string> UnmergedCompilerLines =>
            _sections.Count > 1
                ? PendingCompiler.Select(item => item.Line).ToList()
                : Array.Empty<string>();

        private ProjectObservation Create(Section? section, string? frameworkKey = null)
        {
            var framework = string.IsNullOrEmpty(frameworkKey) ? null : frameworkKey;
            var evidence = new List<string>();
            if (section is not null)
            {
                evidence.AddRange(section.Evidence);
            }

            var decision = section?.Decision;
            var relevant = decision?.RelevantFile;
            var repeatFiles = "";
            if (decision?.Kind == ReasonKind.ItemsChanged && section is not null && section.ChangedItems.Count > 0)
            {
                var files = section.ChangedItems
                    .Select(TextNormalizer.NormalizePath)
                    .Where(item => item.Length > 0)
                    .Distinct(StringComparer.Ordinal)
                    .OrderBy(item => item, StringComparer.Ordinal)
                    .ToList();
                repeatFiles = string.Join(";", files);
                if (files.Count == 1)
                {
                    relevant = section.ChangedItems[0];
                }
            }
            else if (!string.IsNullOrWhiteSpace(relevant))
            {
                repeatFiles = TextNormalizer.NormalizePath(relevant);
            }

            string? copySourceReason = null;
            if (decision?.Kind == ReasonKind.CopySourceNewer
                && string.IsNullOrEmpty(relevant)
                && section is not null
                && TryLastSource(section.Evidence, out var source))
            {
                relevant = source;
                repeatFiles = TextNormalizer.NormalizePath(source);
                copySourceReason = $"{TextNormalizer.FileNameOf(source)} was newer than the last build output.";
            }

            var (outcome, reasonKind, reason) = Describe(decision, section, relevant);
            if (copySourceReason is not null)
            {
                reason = copySourceReason;
            }
            if (decision?.Kind == ReasonKind.UpToDate && section?.AccelerationCount is { } copied && outcome == ProjectOutcome.UpToDate)
            {
                reason = $"Project is up-to-date. Build acceleration copied {copied} files.";
            }

            return new ProjectObservation
            {
                ProjectName = DisplayName,
                FileName = FileName,
                ProjectPath = FullPath,
                Configuration = Configuration ?? "",
                TargetFramework = framework,
                Outcome = outcome,
                ReasonKind = reasonKind,
                Reason = reason,
                RelevantFile = relevant,
                RepeatFiles = repeatFiles,
                Evidence = evidence
            };
        }

        private string DefaultMissingReason()
        {
            if (SdkStyleCSharp == false)
            {
                return ReasonText.NotSdkStyle;
            }

            return ReasonText.MissingDiagnostics;
        }

        private (ProjectOutcome Outcome, ReasonKind Kind, string Reason) Describe(InterpretedLine? decision, Section? section, string? relevantFile)
        {
            var compilerRan = section?.CompilerRan == true;
            var compilerSkipped = section?.CompilerSkipped == true;
            if (compilerRan && compilerSkipped)
            {
                return (ProjectOutcome.Unknown, ReasonKind.Unparsed, "The MSBuild evidence contains both a C# compiler task and an explicit compilation skip, so neither was applied.");
            }

            if (decision?.IsUpToDate == true && compilerRan)
            {
                return (ProjectOutcome.Unknown, ReasonKind.Unparsed, "The up-to-date check reported the project up to date, and a C# compiler task also ran. The outcome could not be determined.");
            }

            if (decision is null && compilerRan)
            {
                return (ProjectOutcome.CompilationRan, ReasonKind.Unparsed, ReasonText.CompilerRanWithoutCheck);
            }

            if (decision is null && compilerSkipped)
            {
                return (ProjectOutcome.CompilationSkipped, ReasonKind.Unparsed, ReasonText.CompilerSkippedWithoutCheck);
            }

            if (decision?.IsUpToDate == true)
            {
                return (ProjectOutcome.UpToDate, ReasonKind.UpToDate, decision.Value.Display);
            }

            if (decision?.IsFailure == true && compilerRan)
            {
                return (ProjectOutcome.CompilationRan, decision.Value.Kind, decision.Value.Display);
            }

            if (decision?.IsFailure == true && compilerSkipped)
            {
                return (ProjectOutcome.CompilationSkipped, decision.Value.Kind, decision.Value.Display);
            }

            if (decision?.IsFailure == true)
            {
                return (ProjectOutcome.SentToMsBuild, decision.Value.Kind, decision.Value.Display);
            }

            _ = relevantFile;
            return (ProjectOutcome.Unknown, ReasonKind.MissingDiagnostics, DefaultMissingReason());
        }

        private static void ApplyCompiler(Section section, bool ran, bool skipped, string evidenceText, string originalLine)
        {
            section.CompilerRan |= ran;
            section.CompilerSkipped |= skipped;
            section.Evidence.Add(originalLine);
        }

        private Section SectionFor(string framework)
        {
            if (!_sections.TryGetValue(framework, out var section))
            {
                section = new Section();
                _sections[framework] = section;
            }

            return section;
        }

        private static bool TryLastSource(List<string> evidence, out string source)
        {
            source = "";
            for (var index = evidence.Count - 1; index >= 0; index--)
            {
                var match = SourcePathPattern().Match(evidence[index]);
                if (match.Success)
                {
                    source = match.Groups["file"].Value;
                    return true;
                }
            }

            return false;
        }

        private sealed class Section
        {
            public List<string> Evidence { get; } = new();

            public List<string> ChangedItems { get; } = new();

            public InterpretedLine? Decision { get; set; }

            public bool CompilerRan { get; set; }

            public bool CompilerSkipped { get; set; }

            public string? AccelerationCount { get; set; }
        }
    }

    private static Regex? slotPattern;
    private static Regex SlotPattern() => slotPattern ??= new Regex(@"^(?<slot>\d+)>\s?(?<rest>.*)$", RegexOptions.CultureInvariant);

    private static Regex? fastLineWithProjectPattern;
    private static Regex FastLineWithProjectPattern() => fastLineWithProjectPattern ??= new Regex(@"^FastUpToDate:\s*(?<message>.*)\s\((?<project>[^()]*)\)\s*$", RegexOptions.CultureInvariant);

    private static Regex? fastLinePlainPattern;
    private static Regex FastLinePlainPattern() => fastLinePlainPattern ??= new Regex(@"^FastUpToDate:\s*(?<message>.*)\s*$", RegexOptions.CultureInvariant);

    private static Regex? buildStartedPattern;
    private static Regex BuildStartedPattern() => buildStartedPattern ??= new Regex(@"^------\s*(?:Build|Rebuild All|Clean|Deploy) started:\s*Project:\s*(?<name>.*?),\s*Configuration:\s*(?<config>.*?)\s*------\s*$", RegexOptions.CultureInvariant);

    private static Regex? projectPathPattern;
    private static Regex ProjectPathPattern() => projectPathPattern ??= new Regex(@"(?:^Project |^Done Building Project )""(?<path>[^""]+\.csproj)""", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? taskPattern;
    private static Regex TaskPattern() => taskPattern ??= new Regex(@"^Task ""(?<name>[^""]+)""\s*$", RegexOptions.CultureInvariant);

    private static Regex? skipTargetPattern;
    private static Regex SkipTargetPattern() => skipTargetPattern ??= new Regex(@"Skipping target ""(?<name>[^""]+)"" because .+$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? targetSkippedPattern;
    private static Regex TargetSkippedPattern() => targetSkippedPattern ??= new Regex(@"^Target ""(?<name>[^""]+)"" skipped\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? summaryPattern;
    private static Regex SummaryPattern() => summaryPattern ??= new Regex(@"^={5,}\s*(?<title>.*?)\s*={5,}$", RegexOptions.CultureInvariant);

    private static Regex? upToDateCountPattern;
    private static Regex UpToDateCountPattern() => upToDateCountPattern ??= new Regex(@"\b(?<uptodate>\d+)\s+up-to-date\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? sourcePathPattern;
    private static Regex SourcePathPattern() => sourcePathPattern ??= new Regex(@"Source\s+(?<time>[^:]+):\s+'(?<file>[^']+)'", RegexOptions.CultureInvariant);
}
