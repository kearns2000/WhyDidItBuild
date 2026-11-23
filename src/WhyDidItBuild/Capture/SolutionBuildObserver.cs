using System.IO;
using System.Runtime.InteropServices;
using EnvDTE;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using WhyDidItBuild.Core;

namespace WhyDidItBuild.Capture;

/// <summary>
/// Watches one solution build through the supported solution-build interfaces.
/// IVsUpdateSolutionEvents2/4 report which build is running and which projects and configurations
/// it visited. They do not include Fast Up-to-Date Check reasons. Those strings are written only to
/// the Build output pane, and only after the user sets SDK-style logging above None. The check's
/// own provider (IBuildUpToDateCheckProvider) is an internal project-system contract, so this
/// extension does not import it. A build summary count is not treated as an up-to-date decision:
/// the project system can report a project to the solution build manager before the check finishes.
/// Design-time builds do not come through these solution events and are not captured.
/// </summary>
internal sealed class SolutionBuildObserver : IVsUpdateSolutionEvents, IVsUpdateSolutionEvents2, IVsUpdateSolutionEvents3, IVsUpdateSolutionEvents4
{
    private const string SdkStyleCSharpProjectType = "9A19103F-16F7-4668-BE54-9A1E7A4F7556";

    private readonly BuildOutputTracer _tracer;
    private readonly BuildSession _session;
    private readonly JoinableTaskFactory _joinableTaskFactory;
    private readonly List<KnownProject> _projects = new();
    private BuildKind _kind = BuildKind.Unknown;
    private bool _nonComparable;
    private bool _sawBuild;
    private bool _cancelled;
    private int _depth;
    private volatile int _generation;
    private DateTimeOffset _started;
    private string? _solutionPath;

    public SolutionBuildObserver(BuildOutputTracer tracer, BuildSession session, JoinableTaskFactory joinableTaskFactory)
    {
        _tracer = tracer;
        _session = session;
        _joinableTaskFactory = joinableTaskFactory;
    }

    public int UpdateSolution_Begin(ref int pfCancelUpdate)
    {
        pfCancelUpdate = 0;
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_depth == 0)
        {
            _generation++;
            _started = DateTimeOffset.Now;
            _kind = BuildKind.Unknown;
            _nonComparable = false;
            _sawBuild = false;
            _cancelled = false;
            _projects.Clear();
            _solutionPath = _session.SolutionPath;
            _tracer.Begin();
        }

        _depth++;
        return VSConstants.S_OK;
    }

    public int UpdateSolution_Done(int fSucceeded, int fModified, int fCancelCommand)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _depth = Math.Max(0, _depth - 1);
        if (_depth != 0)
        {
            return VSConstants.S_OK;
        }

        if (fCancelCommand != 0)
        {
            _cancelled = true;
        }

        var pending = new PendingUpdate(
            _generation,
            _started,
            _solutionPath,
            _projects.ToArray(),
            _kind,
            _nonComparable,
            _sawBuild,
            _cancelled);
        _ = _joinableTaskFactory.RunAsync(async () =>
        {
            // The build summary and the last FastUpToDate lines can be written after this callback returns.
            await Task.Delay(TimeSpan.FromMilliseconds(500)).ConfigureAwait(false);
            await _joinableTaskFactory.SwitchToMainThreadAsync();
            Complete(pending);
        });

        return VSConstants.S_OK;
    }

    public void Invalidate()
    {
        _generation++;
    }

    private void Complete(PendingUpdate pending)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (pending.Generation != _generation || _depth != 0)
        {
            return;
        }

        var text = _tracer.End();
        var readError = _tracer.ReadError;
        var hasFastUpToDate = text.Contains("FastUpToDate:", StringComparison.Ordinal);
        if (!pending.SawBuild && pending.Kind == BuildKind.Unknown && !hasFastUpToDate)
        {
            return;
        }

        var kind = pending.SawBuild && !pending.NonComparable ? BuildKind.Build : pending.Kind;
        if (kind == BuildKind.Unknown && hasFastUpToDate)
        {
            kind = BuildKind.Build;
        }

        var capture = new BuildCapture
        {
            OutputText = text,
            SolutionPath = pending.SolutionPath,
            Kind = kind,
            Cancelled = pending.Cancelled,
            Started = pending.Started,
            Completed = DateTimeOffset.Now,
            Source = BuildSource.VisualStudio,
            KnownProjects = pending.Projects
        };

        _ = _joinableTaskFactory.RunAsync(async () =>
        {
            try
            {
                var report = await Task.Run(() => BuildOutputParser.Parse(capture)).ConfigureAwait(false);
                if (!string.IsNullOrWhiteSpace(readError))
                {
                    report = report with
                    {
                        Note = string.IsNullOrWhiteSpace(report.Note)
                            ? "Part of the Build output could not be read. " + readError
                            : report.Note + " Part of the Build output could not be read. " + readError
                    };
                }

                _session.AddCaptured(report);
            }
            catch (Exception ex)
            {
                _session.ReportFailure("The build output could not be parsed. " + ex.Message);
            }
        });
    }

    public int UpdateSolution_StartUpdate(ref int pfCancelUpdate)
    {
        pfCancelUpdate = 0;
        return VSConstants.S_OK;
    }

    public int UpdateSolution_Cancel()
    {
        _cancelled = true;
        return VSConstants.S_OK;
    }

    public int OnActiveProjectCfgChange(IVsHierarchy pIVsHierarchy) => VSConstants.S_OK;

    public int UpdateProjectCfg_Begin(IVsHierarchy pHierProj, IVsCfg pCfgProj, IVsCfg pCfgSln, uint dwAction, ref int pfCancel)
    {
        pfCancel = 0;
        ThreadHelper.ThrowIfNotOnUIThread();
        _ = pCfgSln;
        _ = dwAction;
        var project = TryReadProject(pHierProj, pCfgProj);
        if (project is null)
        {
            return VSConstants.S_OK;
        }

        if (_projects.Any(existing => string.Equals(existing.FullPath, project.FullPath, StringComparison.OrdinalIgnoreCase)))
        {
            return VSConstants.S_OK;
        }

        _projects.Add(project);
        return VSConstants.S_OK;
    }

    public int UpdateProjectCfg_Done(IVsHierarchy pHierProj, IVsCfg pCfgProj, IVsCfg pCfgSln, uint dwAction, int fSuccess, int fCancel) =>
        VSConstants.S_OK;

    public int OnBeforeActiveSolutionCfgChange(IVsCfg pOldActiveSlnCfg, IVsCfg pNewActiveSlnCfg) => VSConstants.S_OK;

    public int OnAfterActiveSolutionCfgChange(IVsCfg pOldActiveSlnCfg, IVsCfg pNewActiveSlnCfg) => VSConstants.S_OK;

    public void UpdateSolution_QueryDelayFirstUpdateAction(out int pfDelay) => pfDelay = 0;

    public void UpdateSolution_BeginFirstUpdateAction()
    {
    }

    public void UpdateSolution_EndLastUpdateAction()
    {
    }

    public void UpdateSolution_BeginUpdateAction(uint dwAction)
    {
        var flags = (VSSOLNBUILDUPDATEFLAGS)dwAction;
        var build = flags.HasFlag(VSSOLNBUILDUPDATEFLAGS.SBF_OPERATION_BUILD);
        var clean = flags.HasFlag(VSSOLNBUILDUPDATEFLAGS.SBF_OPERATION_CLEAN);
        var force = flags.HasFlag(VSSOLNBUILDUPDATEFLAGS.SBF_OPERATION_FORCE_UPDATE);
        var deploy = flags.HasFlag(VSSOLNBUILDUPDATEFLAGS.SBF_OPERATION_DEPLOY);
        if (clean && !build)
        {
            _nonComparable = true;
            _kind = BuildKind.Clean;
            return;
        }

        if (force)
        {
            _nonComparable = true;
            _kind = BuildKind.Rebuild;
            _sawBuild = build;
            return;
        }

        if (build)
        {
            _sawBuild = true;
            if (!_nonComparable)
            {
                _kind = BuildKind.Build;
            }

            return;
        }

        if (deploy)
        {
            _nonComparable = true;
            if (_kind == BuildKind.Unknown)
            {
                _kind = BuildKind.Deploy;
            }
        }
    }

    public void UpdateSolution_EndUpdateAction(uint dwAction)
    {
    }

    public void OnActiveProjectCfgChangeBatchBegin()
    {
    }

    public void OnActiveProjectCfgChangeBatchEnd()
    {
    }

    private static KnownProject? TryReadProject(IVsHierarchy hierarchy, IVsCfg configuration)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            hierarchy.GetProperty((uint)VSConstants.VSITEMID_ROOT, (int)__VSHPROPID.VSHPROPID_ExtObject, out var ext);
            if (ext is not Project project)
            {
                return null;
            }

            var fullPath = project.FullName;
            if (string.IsNullOrWhiteSpace(fullPath) || !fullPath.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            configuration.get_DisplayName(out var displayName);
            return new KnownProject
            {
                Name = project.Name,
                FileName = Path.GetFileNameWithoutExtension(fullPath),
                FullPath = fullPath,
                Configuration = displayName,
                SdkStyleCSharp = IsSdkStyleCSharp(hierarchy)
            };
        }
        catch (Exception ex) when (ex is COMException or InvalidCastException or NotImplementedException)
        {
            return null;
        }
    }

    private sealed record PendingUpdate(
        int Generation,
        DateTimeOffset Started,
        string? SolutionPath,
        KnownProject[] Projects,
        BuildKind Kind,
        bool NonComparable,
        bool SawBuild,
        bool Cancelled);

    private static bool? IsSdkStyleCSharp(IVsHierarchy hierarchy)
    {
        if (hierarchy is not IVsAggregatableProject aggregatable)
        {
            return null;
        }

        if (aggregatable.GetAggregateProjectTypeGuids(out var guids) != VSConstants.S_OK || string.IsNullOrEmpty(guids))
        {
            return null;
        }

        return guids.IndexOf(SdkStyleCSharpProjectType, StringComparison.OrdinalIgnoreCase) >= 0;
    }
}
