using System.IO;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;
using WhyDidItBuild.Binlog;
using WhyDidItBuild.Core;
using WhyDidItBuild.Core.Correlation;
using WhyDidItBuild.Core.History;

namespace WhyDidItBuild.Capture;

internal sealed class BuildSession : IDisposable
{
    private readonly WhyDidItBuildPackage _package;
    private readonly BuildHistory _history = new();
    private readonly BuildOutputTracer _tracer = new();
    private readonly JoinableTaskFactory _joinableTaskFactory;
    private SolutionBuildObserver? _observer;
    private SolutionEventsSink? _solutionEvents;
    private IVsSolutionBuildManager? _buildManager;
    private IVsSolution? _solution;
    private uint _buildCookie;
    private uint _solutionCookie;
    private string? _openSolutionPath;

    public BuildSession(WhyDidItBuildPackage package)
    {
        _package = package;
        _joinableTaskFactory = package.JoinableTaskFactory;
    }

    public event EventHandler? Changed;

    public string? Status { get; private set; }

    public string? SolutionPath
    {
        get
        {
            ThreadHelper.ThrowIfNotOnUIThread();
            return _openSolutionPath ?? CurrentSolutionPath();
        }
    }

    public async Task InitializeAsync(CancellationToken cancellationToken)
    {
        await _joinableTaskFactory.SwitchToMainThreadAsync(cancellationToken);
        var dte = await _package.GetServiceAsync(typeof(EnvDTE.DTE)) as DTE2
            ?? throw new InvalidOperationException("The Visual Studio automation model is unavailable.");
        _tracer.Attach(dte);
        _openSolutionPath = dte.Solution?.FullName;

        _observer = new SolutionBuildObserver(_tracer, this, _joinableTaskFactory);
        _buildManager = await _package.GetServiceAsync(typeof(SVsSolutionBuildManager)) as IVsSolutionBuildManager
            ?? throw new InvalidOperationException("The solution build manager is unavailable.");
        _buildManager.AdviseUpdateSolutionEvents(_observer, out _buildCookie);

        _solutionEvents = new SolutionEventsSink(this);
        _solution = await _package.GetServiceAsync(typeof(SVsSolution)) as IVsSolution
            ?? throw new InvalidOperationException("The solution service is unavailable.");
        _solution.AdviseSolutionEvents(_solutionEvents, out _solutionCookie);
        if (_buildManager is IVsSolutionBuildManager2 buildManager2
            && buildManager2.QueryBuildManagerBusy(out var busy) == VSConstants.S_OK
            && busy != 0)
        {
            Status = "A build was already running when Why Did It Build? loaded, so that build was not captured. Build again to record the next one.";
            RaiseChanged();
        }
    }

    public BuildSnapshot Snapshot() => _history.Snapshot();

    public void AddCaptured(BuildReport report)
    {
        _history.Add(report);
        Status = null;
        RaiseChanged();
    }

    public void ReportFailure(string message)
    {
        Status = message;
        RaiseChanged();
    }

    public void Import(string text)
    {
        var solutionPath = _openSolutionPath;
        _ = Task.Run(() =>
        {
            var report = BuildOutputParser.Parse(new BuildCapture
            {
                OutputText = text,
                SolutionPath = solutionPath,
                Kind = BuildKind.Unknown,
                Source = BuildSource.ImportedText
            });
            _history.Add(report);
            Status = "Imported build output. This build has no capture time, so an MSBuild log is applied only when exactly one project build matches.";
            RaiseChanged();
        });
    }

    public async Task AttachBinlogAsync(string path)
    {
        var snapshot = _history.Snapshot();
        if (snapshot.Latest is null)
        {
            Status = "Capture or import a build before attaching an MSBuild log. This command does not start a build.";
            RaiseChanged();
            return;
        }

        try
        {
            var projects = await Task.Run(() => BinlogReader.Read(path)).ConfigureAwait(false);
            var updated = BinlogCorrelator.Apply(snapshot.Latest, path, projects);
            if (!_history.ReplaceLatest(updated))
            {
                Status = "A newer build was captured while the log was read, so the log was not applied. Attach it again for the latest build.";
            }
            else
            {
                Status = updated.BinlogStatus;
            }
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or InvalidDataException or NotSupportedException or InvalidOperationException or TypeLoadException or FileNotFoundException)
        {
            Status = ex is FileNotFoundException or TypeLoadException
                ? "Could not read the MSBuild log because Visual Studio has not loaded MSBuild in this session. Build once, then attach the log again. " + ex.Message
                : "Could not read the MSBuild log. " + ex.Message;
        }

        RaiseChanged();
    }

    public void Clear()
    {
        _history.Clear();
        Status = null;
        RaiseChanged();
    }

    public void ClearForClosedSolution()
    {
        _openSolutionPath = null;
        _history.Clear();
        Status = null;
        RaiseChanged();
    }

    public void NoteSolutionOpened()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        var path = CurrentSolutionPath();
        if (string.Equals(path, _openSolutionPath, StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        _openSolutionPath = path;
        _history.Clear();
        Status = null;
        RaiseChanged();
    }

    public void OpenFile(string path)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!File.Exists(path))
        {
            Status = "The file is not on disk: " + path;
            RaiseChanged();
            return;
        }

        VsShellUtilities.OpenDocument(_package, path);
    }

    public void Dispose()
    {
        if (!ThreadHelper.CheckAccess())
        {
            _ = _joinableTaskFactory.Run(async () =>
            {
                await _joinableTaskFactory.SwitchToMainThreadAsync();
                Dispose();
            });
            return;
        }

        _observer?.Invalidate();

        if (_buildManager is not null && _buildCookie != 0)
        {
            _buildManager.UnadviseUpdateSolutionEvents(_buildCookie);
            _buildCookie = 0;
        }

        if (_solution is not null && _solutionCookie != 0)
        {
            _solution.UnadviseSolutionEvents(_solutionCookie);
            _solutionCookie = 0;
        }

        _tracer.Detach();
    }

    private string? CurrentSolutionPath()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_package.GetService(typeof(EnvDTE.DTE)) is DTE2 dte && dte.Solution is not null)
        {
            var fullName = dte.Solution.FullName;
            return string.IsNullOrWhiteSpace(fullName) ? null : fullName;
        }

        return _openSolutionPath;
    }

    private void RaiseChanged()
    {
        _ = _joinableTaskFactory.RunAsync(async () =>
        {
            await _joinableTaskFactory.SwitchToMainThreadAsync();
            Changed?.Invoke(this, EventArgs.Empty);
        });
    }
}
