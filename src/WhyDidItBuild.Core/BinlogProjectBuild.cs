namespace WhyDidItBuild.Core;

/// <summary>
    /// One MSBuild project invocation read from a binary log. Compiler flags are set only from
    /// an observed Csc task or an explicit CoreCompile skip. A skip that only says the target was
    /// previously built does not count as skipping compilation.
/// </summary>
public sealed record BinlogProjectBuild
{
    public required string ProjectPath { get; init; }

    public string? Configuration { get; init; }

    public string? Platform { get; init; }

    public string? TargetFramework { get; init; }

    public DateTimeOffset? Started { get; init; }

    public bool CompilerRan { get; init; }

    public bool CompilationSkipped { get; init; }

    public IReadOnlyList<string> Evidence { get; init; } = Array.Empty<string>();
}
