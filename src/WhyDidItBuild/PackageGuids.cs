namespace WhyDidItBuild;

internal static class PackageGuids
{
    public const string PackageString = "5ecb7f3a-294f-4635-b645-0b9f0b26a614";
    public const string CmdSetString = "95b61417-dfec-410d-bb22-ceba33d357de";
    public const string ToolWindowString = "4e8d1c72-0a5b-4f93-8e16-7c2b9d4a6f18";

    public static readonly Guid CmdSet = new(CmdSetString);
}

internal static class PackageIds
{
    public const int ShowToolWindow = 0x0100;
    public const int ExplainLastBuild = 0x0101;
}
