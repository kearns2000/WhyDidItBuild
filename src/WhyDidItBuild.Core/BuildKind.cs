namespace WhyDidItBuild.Core;

public enum BuildKind
{
    Unknown = 0,
    Build,
    Rebuild,
    Clean,
    Deploy,
    Launch,
    DesignTime
}
