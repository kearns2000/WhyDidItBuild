using System.Collections;
using Microsoft.Build.Framework;
using Microsoft.Build.Logging;
using WhyDidItBuild.Core;

namespace WhyDidItBuild.Binlog;

/// <summary>
/// Reads a .binlog with MSBuild's BinaryLogReplayEventSource. Compiler state comes only from a Csc
/// task or an explicit CoreCompile skip. The reader does not start a build.
/// </summary>
public static class BinlogReader
{
    public static IReadOnlyList<BinlogProjectBuild> Read(string path)
    {
        var contexts = new Dictionary<int, MutableProject>();
        var source = new BinaryLogReplayEventSource();
        // Set through reflection. The property is init-only, and a newer Microsoft.Build (the one
        // Visual Studio 2026 or a current SDK loads) changes the setter's modreq, so a direct call
        // compiled against 17.14 does not bind. A newer log needs this flag, and the error subscription
        // below, before Replay will continue past records it does not know.
        source.GetType().GetProperty(nameof(BinaryLogReplayEventSource.AllowForwardCompatibility))?.SetValue(source, true);
        source.RecoverableReadError += _ => { };

        source.ProjectStarted += (_, args) =>
        {
            if (string.IsNullOrWhiteSpace(args.ProjectFile)
                || !args.ProjectFile.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            var contextId = args.BuildEventContext?.ProjectContextId ?? -1;
            if (contextId < 0 || contexts.ContainsKey(contextId))
            {
                contextId = contexts.Count == 0 ? -1 : contexts.Keys.Min() - 1;
            }

            contexts[contextId] = new MutableProject
            {
                ProjectPath = args.ProjectFile,
                Configuration = Property(args.GlobalProperties, "Configuration") ?? Property(args.Properties, "Configuration"),
                Platform = Property(args.GlobalProperties, "Platform") ?? Property(args.Properties, "Platform"),
                TargetFramework = Property(args.GlobalProperties, "TargetFramework") ?? Property(args.Properties, "TargetFramework"),
                Started = ToOffset(args.Timestamp)
            };
        };

        // 17.14's BinaryLogReplayEventSource exposes AnyEventRaised. TargetSkipped is not a separate event on that type.
        source.AnyEventRaised += (_, args) =>
        {
            switch (args)
            {
                case TaskStartedEventArgs task when string.Equals(task.TaskName, "Csc", StringComparison.Ordinal):
                    var compiled = Find(contexts, task.BuildEventContext, task.ProjectFile);
                    if (compiled is null)
                    {
                        return;
                    }

                    compiled.CompilerRan = true;
                    compiled.Evidence.Add($"Csc task started ({Describe(compiled)}).");
                    break;

                case TargetSkippedEventArgs skipped when string.Equals(skipped.TargetName, "CoreCompile", StringComparison.Ordinal):
                    var skippedProject = Find(contexts, skipped.BuildEventContext, skipped.TargetFile);
                    if (skippedProject is null)
                    {
                        return;
                    }

                    var skipMessage = string.IsNullOrWhiteSpace(skipped.Message)
                        ? "Target \"CoreCompile\" was skipped."
                        : skipped.Message.Trim();
                    if (IsPreviouslyBuilt(skipped, skipMessage))
                    {
                        skippedProject.Evidence.Add(skipMessage);
                        return;
                    }

                    if (skippedProject.SkipRecorded)
                    {
                        return;
                    }

                    skippedProject.CompilationSkipped = true;
                    skippedProject.SkipRecorded = true;
                    skippedProject.Evidence.Add(skipMessage);
                    break;

                case BuildMessageEventArgs message
                    when message.Message is not null
                    && message.Message.IndexOf("Skipping target \"CoreCompile\"", StringComparison.OrdinalIgnoreCase) >= 0:
                    var mentioned = Find(contexts, message.BuildEventContext, null);
                    if (mentioned is null || mentioned.SkipRecorded)
                    {
                        return;
                    }

                    mentioned.CompilationSkipped = true;
                    mentioned.SkipRecorded = true;
                    mentioned.Evidence.Add(message.Message.Trim());
                    break;
            }
        };

        source.Replay(path);

        return contexts.Values
            .Select(project => new BinlogProjectBuild
            {
                ProjectPath = project.ProjectPath,
                Configuration = project.Configuration,
                Platform = project.Platform,
                TargetFramework = project.TargetFramework,
                Started = project.Started,
                CompilerRan = project.CompilerRan,
                CompilationSkipped = project.CompilationSkipped,
                Evidence = project.Evidence
            })
            .ToList();
    }

    private static MutableProject? Find(Dictionary<int, MutableProject> contexts, BuildEventContext? context, string? projectFile)
    {
        if (context is not null && contexts.TryGetValue(context.ProjectContextId, out var byContext))
        {
            return byContext;
        }

        if (string.IsNullOrWhiteSpace(projectFile))
        {
            return null;
        }

        var matches = contexts.Values
            .Where(item => string.Equals(item.ProjectPath, projectFile, StringComparison.OrdinalIgnoreCase))
            .ToList();
        return matches.Count == 1 ? matches[0] : null;
    }

    private static bool IsPreviouslyBuilt(TargetSkippedEventArgs args, string message)
    {
        if (message.IndexOf("Previously built successfully", StringComparison.OrdinalIgnoreCase) >= 0)
        {
            return true;
        }

        return args.SkipReason.ToString().IndexOf("PreviouslyBuilt", StringComparison.OrdinalIgnoreCase) >= 0;
    }

    private static string? Property(IEnumerable? properties, string name)
    {
        if (properties is null)
        {
            return null;
        }

        foreach (var entry in properties)
        {
            switch (entry)
            {
                case DictionaryEntry pair
                    when string.Equals(pair.Key?.ToString(), name, StringComparison.OrdinalIgnoreCase):
                    return pair.Value?.ToString();
                case KeyValuePair<string, string> pair
                    when string.Equals(pair.Key, name, StringComparison.OrdinalIgnoreCase):
                    return pair.Value;
                default:
                    var key = entry.GetType().GetProperty("Key")?.GetValue(entry)
                        ?? entry.GetType().GetProperty("Name")?.GetValue(entry);
                    if (key is not null && string.Equals(key.ToString(), name, StringComparison.OrdinalIgnoreCase))
                    {
                        return entry.GetType().GetProperty("Value")?.GetValue(entry)?.ToString();
                    }

                    break;
            }
        }

        return null;
    }

    private static DateTimeOffset ToOffset(DateTime timestamp)
    {
        var specified = timestamp.Kind == DateTimeKind.Unspecified
            ? DateTime.SpecifyKind(timestamp, DateTimeKind.Local)
            : timestamp;
        return new DateTimeOffset(specified);
    }

    private static string Describe(MutableProject project)
    {
        var framework = string.IsNullOrWhiteSpace(project.TargetFramework) ? "target framework not recorded" : project.TargetFramework;
        return framework;
    }

    private sealed class MutableProject
    {
        public string ProjectPath { get; set; } = "";

        public string? Configuration { get; set; }

        public string? Platform { get; set; }

        public string? TargetFramework { get; set; }

        public DateTimeOffset? Started { get; set; }

        public bool CompilerRan { get; set; }

        public bool CompilationSkipped { get; set; }

        public bool SkipRecorded { get; set; }

        public List<string> Evidence { get; } = new();
    }
}
