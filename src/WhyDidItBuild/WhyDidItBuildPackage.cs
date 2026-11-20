using System.Runtime.InteropServices;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using WhyDidItBuild.Capture;
using WhyDidItBuild.UI;

namespace WhyDidItBuild;

[PackageRegistration(UseManagedResourcesOnly = true, AllowsBackgroundLoading = true)]
[InstalledProductRegistration("Why Did It Build?", "Explains why Visual Studio built SDK-style projects again.", "1.0")]
[Guid(PackageGuids.PackageString)]
[ProvideMenuResource("Menus.ctmenu", 1)]
[ProvideToolWindow(typeof(WhyDidItBuildToolWindow))]
[ProvideAutoLoad(VSConstants.UICONTEXT.SolutionExists_string, PackageAutoLoadFlags.BackgroundLoad)]
public sealed class WhyDidItBuildPackage : AsyncPackage
{
    private BuildSession? _session;

    public BuildSession Session => _session ?? throw new InvalidOperationException("Why Did It Build? is not initialized.");

    protected override async Task InitializeAsync(CancellationToken cancellationToken, IProgress<ServiceProgressData> progress)
    {
        await JoinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        _session = new BuildSession(this);
        await _session.InitializeAsync(cancellationToken);

        if (await GetServiceAsync(typeof(IMenuCommandService)) is not OleMenuCommandService commands)
        {
            return;
        }

        commands.AddCommand(new MenuCommand(ShowToolWindow, new CommandID(PackageGuids.CmdSet, PackageIds.ShowToolWindow)));
        commands.AddCommand(new MenuCommand(ExplainLastBuild, new CommandID(PackageGuids.CmdSet, PackageIds.ExplainLastBuild)));
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _session?.Dispose();
            _session = null;
        }

        base.Dispose(disposing);
    }

    private void ShowToolWindow(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var window = CreateWindow();
        var frame = (IVsWindowFrame)window.Frame;
        frame.Show();
    }

    private void ExplainLastBuild(object sender, EventArgs e)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        string? path = null;
        string? name = null;
        if (GetService(typeof(SVsShellMonitorSelection)) is IVsMonitorSelection monitor
            && monitor.GetCurrentSelection(out var hierarchy, out _, out _, out _) == VSConstants.S_OK
            && hierarchy is not null
            && hierarchy.GetProperty((uint)VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_ExtObject, out var ext) == VSConstants.S_OK
            && ext is Project project)
        {
            path = project.FullName;
            name = project.Name;
        }

        var window = CreateWindow();
        var frame = (IVsWindowFrame)window.Frame;
        frame.Show();
        window.SelectProject(path, name);
    }

    private WhyDidItBuildToolWindow CreateWindow()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        return FindToolWindow(typeof(WhyDidItBuildToolWindow), 0, true) as WhyDidItBuildToolWindow
            ?? throw new InvalidOperationException("Could not create the Why Did It Build? window.");
    }
}
