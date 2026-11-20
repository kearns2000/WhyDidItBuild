using WhyDidItBuild.Core.Comparison;

namespace WhyDidItBuild.Core.History;

public sealed class BuildSnapshot
{
    public BuildSnapshot(BuildReport? latest, BuildReport? previousComparable)
    {
        Latest = latest;
        PreviousComparable = previousComparable;
    }

    public BuildReport? Latest { get; }

    public BuildReport? PreviousComparable { get; }
}

/// <summary>
/// Keeps the latest completed build and the last two comparable builds for the open solution.
/// Clean, Rebuild, and design-time builds stay out of the repeat comparison.
/// </summary>
public sealed class BuildHistory
{
    private readonly object _gate = new();
    private readonly List<BuildReport> _comparable = new();
    private BuildReport? _latest;

    public void Clear()
    {
        lock (_gate)
        {
            _latest = null;
            _comparable.Clear();
        }
    }

    public void Add(BuildReport report)
    {
        lock (_gate)
        {
            var previous = _comparable.Count == 0 ? null : _comparable[^1];
            _latest = Repetition.Apply(report, previous);
            if (report.Comparable)
            {
                _comparable.Add(_latest);
                if (_comparable.Count > 2)
                {
                    _comparable.RemoveAt(0);
                }
            }
        }
    }

    public bool ReplaceLatest(BuildReport report)
    {
        lock (_gate)
        {
            if (_latest is null || _latest.Id != report.Id)
            {
                return false;
            }

            _latest = report;
            if (report.Comparable
                && _comparable.Count > 0
                && _comparable[^1].Id == report.Id)
            {
                _comparable[^1] = report;
            }

            return true;
        }
    }

    public BuildSnapshot Snapshot()
    {
        lock (_gate)
        {
            BuildReport? previous = null;
            if (_latest?.Comparable == true)
            {
                previous = _comparable.Count >= 2 ? _comparable[^2] : null;
            }
            else if (_comparable.Count >= 1)
            {
                previous = _comparable[^1];
            }

            return new BuildSnapshot(_latest, previous);
        }
    }
}
