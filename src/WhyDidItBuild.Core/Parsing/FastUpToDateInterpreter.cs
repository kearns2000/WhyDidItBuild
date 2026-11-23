using System.Text.RegularExpressions;

namespace WhyDidItBuild.Core;

internal readonly record struct InterpretedLine(ReasonKind Kind, string Display, string? RelevantFile, bool Decisive)
{
    public bool IsFailure => Decisive && Kind != ReasonKind.UpToDate;

    public bool IsUpToDate => Decisive && Kind == ReasonKind.UpToDate;
}

/// <summary>
/// Maps one FastUpToDate message to a reason. Patterns follow the strings in
/// Microsoft.VisualStudio.ProjectSystem.Managed.VS VSResources (FUTD_*).
/// A line that is not a decision returns Decisive = false.
/// </summary>
internal static partial class FastUpToDateInterpreter
{
    public static InterpretedLine Interpret(string message)
    {
        var text = Regex.Replace(message.Trim(), "not up to date", "not up-to-date", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
        if (text.Length == 0)
        {
            return Informational();
        }

        if (UpToDatePattern().IsMatch(text))
        {
            return new InterpretedLine(ReasonKind.UpToDate, "Project is up-to-date.", null, true);
        }

        var inputNewer = TypedInputNewerPattern().Match(text);
        if (!inputNewer.Success)
        {
            inputNewer = InputNewerPattern().Match(text);
        }

        if (inputNewer.Success)
        {
            var file = inputNewer.Groups["file"].Value;
            return Failure(ReasonKind.InputNewerThanOutput, $"{TextNormalizer.FileNameOf(file)} was newer than the last build output.", file);
        }

        var modified = TypedModifiedPattern().Match(text);
        if (!modified.Success)
        {
            modified = ModifiedPattern().Match(text);
        }

        if (modified.Success)
        {
            var file = modified.Groups["file"].Value;
            return Failure(ReasonKind.InputModifiedSinceBuild, $"{TextNormalizer.FileNameOf(file)} was modified since the last successful build started.", file);
        }

        var outputMissing = OutputMissingPattern().Match(text);
        if (outputMissing.Success)
        {
            var file = outputMissing.Groups["file"].Value;
            return Failure(ReasonKind.OutputMissing, $"Output {TextNormalizer.FileNameOf(file)} does not exist.", file);
        }

        var inputMissing = TypedInputMissingPattern().Match(text);
        if (!inputMissing.Success)
        {
            inputMissing = InputMissingPattern().Match(text);
        }

        if (inputMissing.Success)
        {
            var file = inputMissing.Groups["file"].Value;
            return Failure(ReasonKind.InputMissing, $"Input {TextNormalizer.FileNameOf(file)} does not exist.", file);
        }

        if (ItemsChangedPattern().IsMatch(text))
        {
            return Failure(ReasonKind.ItemsChanged, "The set of project items changed more recently than the last successful build.", null);
        }

        if (FirstRunPattern().IsMatch(text))
        {
            return Failure(ReasonKind.FirstRun, "The up-to-date check has not run for this project yet.", null);
        }

        if (DisabledPattern().IsMatch(text))
        {
            return Failure(ReasonKind.CheckDisabled, "The fast up-to-date check is disabled for this project.", null);
        }

        if (CriticalPattern().IsMatch(text))
        {
            return Failure(ReasonKind.CriticalTasks, "Critical build tasks were running.", null);
        }

        var copyNewer = CopyToOutputNewerPattern().Match(text);
        if (copyNewer.Success)
        {
            var file = copyNewer.Groups["file"].Value;
            return Failure(ReasonKind.CopySourceNewer, $"{TextNormalizer.FileNameOf(file)} was newer than the last build output.", file);
        }

        if (BuiltSourceNewerPattern().IsMatch(text))
        {
            return Failure(ReasonKind.CopySourceNewer, "A build input was newer than its output.", null);
        }

        var copySourceMissing = CopySourceMissingPattern().Match(text);
        if (!copySourceMissing.Success)
        {
            copySourceMissing = BuiltSourceMissingPattern().Match(text);
        }

        if (copySourceMissing.Success)
        {
            var file = copySourceMissing.Groups["file"].Value;
            return Failure(ReasonKind.CopySourceMissing, $"Source {TextNormalizer.FileNameOf(file)} does not exist.", file);
        }

        var copyDestMissing = CopyDestinationMissingPattern().Match(text);
        if (!copyDestMissing.Success)
        {
            copyDestMissing = BuiltDestinationMissingPattern().Match(text);
        }

        if (copyDestMissing.Success)
        {
            var file = copyDestMissing.Groups["file"].Value;
            return Failure(ReasonKind.CopyDestinationMissing, $"Destination {TextNormalizer.FileNameOf(file)} does not exist.", file);
        }

        var always = CopyAlwaysPattern().Match(text);
        if (always.Success)
        {
            var file = always.Groups["file"].Value;
            return Failure(ReasonKind.CopyAlwaysDiffers, $"{TextNormalizer.FileNameOf(file)} differs from the copy in the build output.", file);
        }

        var marker = MarkerPattern().Match(text);
        if (marker.Success)
        {
            var file = marker.Groups["file"].Value;
            return Failure(ReasonKind.MarkerNewer, $"{TextNormalizer.FileNameOf(file)} was newer than the last build output.", file);
        }

        if (ExceptionPattern().IsMatch(text) || CopyExceptionPattern().IsMatch(text))
        {
            return Failure(ReasonKind.Exception, text, FirstPath(text));
        }

        if (text.Contains("not up-to-date", StringComparison.OrdinalIgnoreCase))
        {
            return Failure(ReasonKind.Unparsed, text, FirstPath(text));
        }

        return Informational();
    }

    public static bool TryReadConfiguration(string message, out string configuration, out string? targetFramework)
    {
        configuration = "";
        targetFramework = null;
        var match = ConfigurationPattern().Match(message.Trim());
        if (!match.Success)
        {
            return false;
        }

        configuration = match.Groups["config"].Value.Trim();
        var parts = configuration.Split('|');
        if (parts.Length >= 3 && LooksLikeTargetFramework(parts[^1]))
        {
            targetFramework = parts[^1].Trim();
        }

        return true;
    }

    public static bool TryReadChangedItem(string message, out string path)
    {
        var match = ChangedItemPattern().Match(message.Trim());
        path = match.Success ? match.Groups["file"].Value : "";
        return match.Success;
    }

    public static string? TryReadAcceleration(string message)
    {
        var match = AccelerationPattern().Match(message.Trim());
        return match.Success ? match.Groups["count"].Value : null;
    }

    private static bool LooksLikeTargetFramework(string value)
    {
        var text = value.Trim();
        return text.StartsWith("net", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("netcoreapp", StringComparison.OrdinalIgnoreCase)
            || text.StartsWith("netstandard", StringComparison.OrdinalIgnoreCase);
    }

    private static string? FirstPath(string text)
    {
        var quoted = QuotedPathPattern().Match(text);
        return quoted.Success ? quoted.Groups["file"].Value : null;
    }

    private static InterpretedLine Failure(ReasonKind kind, string display, string? file) =>
        new(kind, display, string.IsNullOrEmpty(file) ? null : file, true);

    private static InterpretedLine Informational() => new(ReasonKind.None, "", null, false);

    private static Regex? upToDatePattern;
    private static Regex UpToDatePattern() => upToDatePattern ??= new Regex(@"^Project is up-to-date\.?$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? inputNewerPattern;
    private static Regex InputNewerPattern() => inputNewerPattern ??= new Regex(@"^Input '(?<file>[^']+)' is newer \([^)]*\) than earliest output '[^']+' \([^)]*\), not up-to-date\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? typedInputNewerPattern;
    private static Regex TypedInputNewerPattern() => typedInputNewerPattern ??= new Regex(@"^Input \S+ item '(?<file>[^']+)' is newer \([^)]*\) than earliest output '[^']+' \([^)]*\), not up-to-date\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? modifiedPattern;
    private static Regex ModifiedPattern() => modifiedPattern ??= new Regex(@"^Input '(?<file>[^']+)' \([^)]*\) has been modified since the last successful build started \([^)]*\), not up-to-date\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? typedModifiedPattern;
    private static Regex TypedModifiedPattern() => typedModifiedPattern ??= new Regex(@"^Input \S+ item '(?<file>[^']+)' \([^)]*\) has been modified since the last successful build started \([^)]*\), not up-to-date\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? outputMissingPattern;
    private static Regex OutputMissingPattern() => outputMissingPattern ??= new Regex(@"^Output '(?<file>[^']+)' does not exist, not up-to-date\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? inputMissingPattern;
    private static Regex InputMissingPattern() => inputMissingPattern ??= new Regex(@"^Input '(?<file>[^']+)' does not exist and is required, not up-to-date\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? typedInputMissingPattern;
    private static Regex TypedInputMissingPattern() => typedInputMissingPattern ??= new Regex(@"^Input \S+ item '(?<file>[^']+)' does not exist and is required, not up-to-date\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? itemsChangedPattern;
    private static Regex ItemsChangedPattern() => itemsChangedPattern ??= new Regex(@"^The set of project items was changed more recently \([^)]*\) than the last successful build start time \([^)]*\), not up-to-date\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? firstRunPattern;
    private static Regex FirstRunPattern() => firstRunPattern ??= new Regex(@"^The up-to-date check has not yet run for this project\. Not up-to-date\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? disabledPattern;
    private static Regex DisabledPattern() => disabledPattern ??= new Regex(@"^The 'DisableFastUpToDateCheck' property is 'true', not up-to-date\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? criticalPattern;
    private static Regex CriticalPattern() => criticalPattern ??= new Regex(@"^Critical build tasks are running, not up-to-date\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? copyToOutputNewerPattern;
    private static Regex CopyToOutputNewerPattern() => copyToOutputNewerPattern ??= new Regex(@"^Item with CopyToOutputDirectory=""[^""]*"" source '(?<file>[^']+)' is newer than destination '[^']+', not up-to-date\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? builtSourceNewerPattern;
    private static Regex BuiltSourceNewerPattern() => builtSourceNewerPattern ??= new Regex(@"^Source is newer than build output destination, not up-to-date\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? copySourceMissingPattern;
    private static Regex CopySourceMissingPattern() => copySourceMissingPattern ??= new Regex(@"^Source '(?<file>[^']+)' does not exist, not up-to-date\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? builtSourceMissingPattern;
    private static Regex BuiltSourceMissingPattern() => builtSourceMissingPattern ??= new Regex(@"^Source '(?<file>[^']+)' does not exist for build into target '[^']+', not up-to-date\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? copyDestinationMissingPattern;
    private static Regex CopyDestinationMissingPattern() => copyDestinationMissingPattern ??= new Regex(@"^Destination '(?<file>[^']+)' does not exist, not up-to-date\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? builtDestinationMissingPattern;
    private static Regex BuiltDestinationMissingPattern() => builtDestinationMissingPattern ??= new Regex(@"^Destination '(?<file>[^']+)' does not exist for copy from '[^']+', not up-to-date\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? copyAlwaysPattern;
    private static Regex CopyAlwaysPattern() => copyAlwaysPattern ??= new Regex(@"^Item with CopyToOutputDirectory=""Always"" source \('(?<file>[^']+)' [^)]*\) differs from destination \('[^']+' [^)]*\), not up-to-date\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? markerPattern;
    private static Regex MarkerPattern() => markerPattern ??= new Regex(@"^Input marker '(?<file>[^']+)' is newer \([^)]*\) than output marker '[^']+' \([^)]*\), not up-to-date\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? exceptionPattern;
    private static Regex ExceptionPattern() => exceptionPattern ??= new Regex(@"^Up-to-date check threw an exception\. Not up-to-date\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? copyExceptionPattern;
    private static Regex CopyExceptionPattern() => copyExceptionPattern ??= new Regex(@"^Exception copying file, scheduling MSBuild:", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? configurationPattern;
    private static Regex ConfigurationPattern() => configurationPattern ??= new Regex(@"^Checking configuration (?<config>.+):\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? changedItemPattern;
    private static Regex ChangedItemPattern() => changedItemPattern ??= new Regex(@"^\S+ item (?:added|removed) '(?<file>[^']+)'\s*$", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? accelerationPattern;
    private static Regex AccelerationPattern() => accelerationPattern ??= new Regex(@"^Build acceleration copied (?<count>\d+) files\.", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static Regex? quotedPathPattern;
    private static Regex QuotedPathPattern() => quotedPathPattern ??= new Regex(@"'(?<file>[^']+)'", RegexOptions.CultureInvariant);
}
