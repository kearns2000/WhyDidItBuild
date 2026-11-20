using Microsoft.VisualStudio.Shell;
using WhyDidItBuild.Capture;

namespace WhyDidItBuild.UI;

[System.Runtime.InteropServices.Guid(PackageGuids.ToolWindowString)]
public sealed class WhyDidItBuildToolWindow : ToolWindowPane
{
    private WhyDidItBuildControl? _control;

    public WhyDidItBuildToolWindow()
        : base(null)
    {
        Caption = "Why Did It Build?";
    }

    protected override void Initialize()
    {
        base.Initialize();
        var package = (WhyDidItBuildPackage)Package;
        _control = new WhyDidItBuildControl(package.Session);
        Content = _control;
    }

    public void SelectProject(string? path, string? name)
    {
        _control?.SelectProject(path, name);
    }
}
