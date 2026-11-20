using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;
using WhyDidItBuild.Core.History;

namespace WhyDidItBuild.Capture;

internal sealed class SolutionEventsSink : IVsSolutionEvents
{
    private readonly BuildSession _session;

    public SolutionEventsSink(BuildSession session)
    {
        _session = session;
    }

    public int OnAfterOpenProject(IVsHierarchy pHierarchy, int fAdded) => VSConstants.S_OK;

    public int OnQueryCloseProject(IVsHierarchy pHierarchy, int fRemoving, out int pfCancel)
    {
        pfCancel = 0;
        return VSConstants.S_OK;
    }

    public int OnBeforeCloseProject(IVsHierarchy pHierarchy, int fRemoved) => VSConstants.S_OK;

    public int OnAfterLoadProject(IVsHierarchy pStubHierarchy, IVsHierarchy pRealHierarchy) => VSConstants.S_OK;

    public int OnQueryUnloadProject(IVsHierarchy pRealHierarchy, out int pfCancel)
    {
        pfCancel = 0;
        return VSConstants.S_OK;
    }

    public int OnBeforeUnloadProject(IVsHierarchy pRealHierarchy, IVsHierarchy pStubHierarchy) => VSConstants.S_OK;

    public int OnAfterOpenSolution(object pUnkReserved, int fNewSolution)
    {
        _session.NoteSolutionOpened();
        return VSConstants.S_OK;
    }

    public int OnQueryCloseSolution(object pUnkReserved, out int pfCancel)
    {
        pfCancel = 0;
        return VSConstants.S_OK;
    }

    public int OnBeforeCloseSolution(object pUnkReserved) => VSConstants.S_OK;

    public int OnAfterCloseSolution(object pUnkReserved)
    {
        _session.ClearForClosedSolution();
        return VSConstants.S_OK;
    }
}
