namespace WhyDidItBuild;

/// <summary>
/// Compiled only when this project is built off Windows. The extension itself is a Windows VSIX.
/// </summary>
internal static class NonWindowsBuildStub
{
    public const string Message = "Build WhyDidItBuild.csproj on Windows to produce the VSIX.";
}
