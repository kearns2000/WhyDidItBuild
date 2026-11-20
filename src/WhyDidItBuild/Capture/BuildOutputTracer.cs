using System.Runtime.InteropServices;
using System.Text;
using EnvDTE;
using EnvDTE80;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace WhyDidItBuild.Capture;

/// <summary>
/// Copies new Build-pane text for the solution update that is in progress.
/// Reading the pane uses EnvDTE and stays on the UI thread. Parsing does not.
/// </summary>
internal sealed class BuildOutputTracer
{
    private readonly StringBuilder _buffer = new();
    private DTE2? _dte;
    private OutputWindowEvents? _events;
    private OutputWindowPane? _pane;
    private bool _capturing;
    private bool _reset;
    private int _lastOffset = 1;

    public string? ReadError { get; private set; }

    public void Attach(DTE2 dte)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _dte = dte;
        _events = dte.Events.OutputWindowEvents;
        _events.PaneUpdated += OnPaneUpdated;
        _events.PaneClearing += OnPaneClearing;
    }

    public void Detach()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_events is null)
        {
            return;
        }

        _events.PaneUpdated -= OnPaneUpdated;
        _events.PaneClearing -= OnPaneClearing;
        _events = null;
        _dte = null;
    }

    public void Begin()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        _buffer.Clear();
        ReadError = null;
        _capturing = true;
        _reset = false;
        _pane = FindBuildPane();
        _lastOffset = EndOffset(_pane);
    }

    public string End()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        try
        {
            if (_reset)
            {
                _buffer.Clear();
                _reset = false;
                _lastOffset = 1;
            }

            if (_pane is not null)
            {
                ReadDelta(_pane);
            }
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            ReadError = ex.Message;
        }

        _capturing = false;
        return _buffer.ToString();
    }

    private void OnPaneClearing(OutputWindowPane pane)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!_capturing || !IsBuildPane(pane))
        {
            return;
        }

        _pane = pane;
        _reset = true;
    }

    private void OnPaneUpdated(OutputWindowPane pane)
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (!_capturing || !IsBuildPane(pane))
        {
            return;
        }

        _pane = pane;
        try
        {
            if (_reset)
            {
                _buffer.Clear();
                _reset = false;
                _lastOffset = 1;
            }

            ReadDelta(pane);
        }
        catch (Exception ex) when (IsReadFailure(ex))
        {
            ReadError = ex.Message;
        }
    }

    private void ReadDelta(OutputWindowPane pane)
    {
        var document = pane.TextDocument;
        if (document is null)
        {
            return;
        }

        var end = document.EndPoint.CreateEditPoint();
        var endOffset = end.AbsoluteCharOffset;
        if (endOffset < _lastOffset)
        {
            _lastOffset = 1;
        }

        if (endOffset <= _lastOffset)
        {
            _lastOffset = Math.Max(1, endOffset);
            return;
        }

        var start = document.StartPoint.CreateEditPoint();
        start.MoveToAbsoluteOffset(Math.Max(1, _lastOffset));
        _buffer.Append(start.GetText(end));
        _lastOffset = endOffset;
    }

    private OutputWindowPane? FindBuildPane()
    {
        ThreadHelper.ThrowIfNotOnUIThread();
        if (_dte is null)
        {
            return null;
        }

        var panes = _dte.ToolWindows.OutputWindow.OutputWindowPanes;
        foreach (OutputWindowPane pane in panes)
        {
            if (IsBuildPane(pane))
            {
                return pane;
            }
        }

        return null;
    }

    private static int EndOffset(OutputWindowPane? pane)
    {
        if (pane?.TextDocument is null)
        {
            return 1;
        }

        return Math.Max(1, pane.TextDocument.EndPoint.CreateEditPoint().AbsoluteCharOffset);
    }

    private static bool IsBuildPane(OutputWindowPane pane)
    {
        return Guid.TryParse(pane.Guid, out var guid) && guid == VSConstants.GUID_BuildOutputWindowPane;
    }

    private static bool IsReadFailure(Exception exception) =>
        exception is COMException or InvalidOperationException or ArgumentException or NotImplementedException;
}
