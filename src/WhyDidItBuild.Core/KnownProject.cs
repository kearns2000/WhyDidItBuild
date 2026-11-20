namespace WhyDidItBuild.Core;

/// <summary>
/// A project the solution build manager visited, or a project named by imported text.
/// FileName is the project file name without an extension, which is what FastUpToDate lines append.
/// </summary>
public sealed record KnownProject
{
    public required string Name { get; init; }

    public required string FileName { get; init; }

    public string? FullPath { get; init; }

    public string? Configuration { get; init; }

    public bool? SdkStyleCSharp { get; init; }
}
