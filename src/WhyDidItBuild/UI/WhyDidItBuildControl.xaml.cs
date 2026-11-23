using System.IO;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Documents;
using System.Windows.Media;
using Microsoft.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Shell;
using Microsoft.Win32;
using WhyDidItBuild.Capture;
using WhyDidItBuild.Core;
using WhyDidItBuild.Core.History;

namespace WhyDidItBuild.UI;

public partial class WhyDidItBuildControl : UserControl
{
    private const string SetupInstructions =
        "No Fast Up-to-Date Check diagnostics were captured for this build. Visual Studio writes them only when logging is enabled.\n\n" +
        "Set Tools > Options > Projects and Solutions > SDK-Style Projects > Logging Level to Info. " +
        "Minimal records out-of-date projects only. Info also records projects that are up to date. Verbose adds the input and output lists.\n\n" +
        "Build again after changing the setting. This window reads the Build output for that build. It does not start a build.\n\n" +
        "You can also paste or open saved Build output with Import Build Output.";

    private static readonly Regex PathRegex = new(
        @"\\\\(?:[^\\/:*?""<>|\r\n]+\\)+[^\\/:*?""<>|\r\n]+|[A-Za-z]:\\(?:[^\\/:*?""<>|\r\n]+\\)*[^\\/:*?""<>|\r\n]+",
        RegexOptions.Compiled | RegexOptions.CultureInvariant);

    private readonly BuildSession _session;
    private readonly List<ProjectRow> _latestRows = new();
    private readonly List<ProjectRow> _previousRows = new();
    private bool _suppressSelection;
    private string? _selectPath;
    private string? _selectName;
    private string? _selectFramework;
    private BuildReport? _latest;
    private BuildReport? _previous;

    internal WhyDidItBuildControl(BuildSession session)
    {
        InitializeComponent();
        _session = session;
        Subscribe();
        Loaded += OnLoaded;
        Unloaded += OnUnloaded;
        Refresh();
    }

    public void SelectProject(string? path, string? name)
    {
        _selectPath = path;
        _selectName = name;
        _selectFramework = null;
        ApplySelection();
    }

    private void OnLoaded(object sender, RoutedEventArgs e)
    {
        Subscribe();
        ApplyTheme();
        Refresh();
    }

    private void OnUnloaded(object sender, RoutedEventArgs e)
    {
        _session.Changed -= OnSessionChanged;
        VSColorTheme.ThemeChanged -= OnThemeChanged;
    }

    private void Subscribe()
    {
        _session.Changed -= OnSessionChanged;
        _session.Changed += OnSessionChanged;
        VSColorTheme.ThemeChanged -= OnThemeChanged;
        VSColorTheme.ThemeChanged += OnThemeChanged;
    }

    private void OnThemeChanged(ThemeChangedEventArgs e) => Dispatcher.Invoke(ApplyTheme);

    private void OnSessionChanged(object? sender, EventArgs e) => Dispatcher.Invoke(Refresh);

    private void ImportClick(object sender, RoutedEventArgs e)
    {
        var dialog = new ImportBuildOutputDialog();
        if (dialog.ShowDialog() == true && dialog.OutputText is not null)
        {
            _session.Import(dialog.OutputText);
        }
    }

    private async void AttachClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Attach MSBuild log",
            Filter = "MSBuild binary log (*.binlog)|*.binlog"
        };
        if (dialog.ShowDialog() != true)
        {
            return;
        }

        await _session.AttachBinlogAsync(dialog.FileName);
    }

    private void ClearClick(object sender, RoutedEventArgs e) => _session.Clear();

    private void LatestSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || LatestGrid.SelectedItem is not ProjectRow row)
        {
            return;
        }

        _selectPath = row.Observation.ProjectPath ?? row.Observation.FileName;
        _selectName = row.Observation.ProjectName;
        _selectFramework = row.Observation.TargetFramework;
        _suppressSelection = true;
        PreviousGrid.SelectedItem = null;
        _suppressSelection = false;
        ShowEvidence(row.Observation, _latest);
    }

    private void PreviousSelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_suppressSelection || PreviousGrid.SelectedItem is not ProjectRow row)
        {
            return;
        }

        _suppressSelection = true;
        LatestGrid.SelectedItem = null;
        _suppressSelection = false;
        ShowEvidence(row.Observation, _previous);
    }

    private void Refresh()
    {
        var snapshot = _session.Snapshot();
        _latest = snapshot.Latest;
        _previous = snapshot.PreviousComparable;
        _latestRows.Clear();
        _previousRows.Clear();

        if (_latest is null)
        {
            HeadingText.Text = "No build has been captured for this solution yet.";
            DetailText.Text = string.IsNullOrWhiteSpace(_session.Status)
                ? "Build the solution, or start debugging, then return here. The last two comparable builds stay in memory for this solution."
                : _session.Status;
            SetupText.Text = SetupInstructions;
            SetupText.Visibility = Visibility.Visible;
            LatestGrid.ItemsSource = null;
            PreviousGrid.ItemsSource = null;
            ShowLines(Array.Empty<string>(), null);
            return;
        }

        HeadingText.Text = $"{_latest.KindLabel}, {TextNormalizer.DescribeWhen(_latest.Completed)}";
        DetailText.Text = Describe(_latest, _previous);
        if (!_latest.FastUpToDateDiagnosticsPresent)
        {
            SetupText.Text = SetupInstructions;
            SetupText.Visibility = Visibility.Visible;
        }
        else if (_latest.Projects.Any(project => project.ReasonKind == ReasonKind.MissingDiagnostics))
        {
            SetupText.Text = "Some projects have no FastUpToDate line, so their outcome stays Unknown. Missing diagnostics are not treated as up to date. Minimal logging does not record up-to-date projects; Info does.";
            SetupText.Visibility = Visibility.Visible;
        }
        else
        {
            SetupText.Visibility = Visibility.Collapsed;
        }

        _latestRows.AddRange(_latest.Projects.Select(project => new ProjectRow(project)));
        if (_previous is not null)
        {
            _previousRows.AddRange(_previous.Projects.Select(project => new ProjectRow(project)));
        }

        _suppressSelection = true;
        LatestGrid.ItemsSource = null;
        LatestGrid.ItemsSource = _latestRows;
        PreviousGrid.ItemsSource = null;
        PreviousGrid.ItemsSource = _previousRows;
        PreviousHeading.Text = _previous is null
            ? "No previous comparable build"
            : $"Previous comparable build, {TextNormalizer.DescribeWhen(_previous.Completed)}";
        _suppressSelection = false;
        ApplySelection();
    }

    private void ApplySelection()
    {
        if (_latestRows.Count == 0)
        {
            ShowLines(_latest?.UnattributedEvidence ?? Array.Empty<string>(), _latest);
            return;
        }

        ProjectRow? match = null;
        if (!string.IsNullOrWhiteSpace(_selectPath))
        {
            var pathMatches = _latestRows.Where(row =>
                string.Equals(TextNormalizer.NormalizePath(row.Observation.ProjectPath), TextNormalizer.NormalizePath(_selectPath), StringComparison.Ordinal)).ToList();
            if (!string.IsNullOrWhiteSpace(_selectFramework))
            {
                match = pathMatches.FirstOrDefault(row =>
                    string.Equals(row.Observation.TargetFramework, _selectFramework, StringComparison.OrdinalIgnoreCase));
            }

            match ??= pathMatches.Count > 0 ? pathMatches[0] : null;
        }

        if (match is null && !string.IsNullOrWhiteSpace(_selectName))
        {
            var byName = _latestRows.Where(row =>
                string.Equals(row.Observation.ProjectName, _selectName, StringComparison.OrdinalIgnoreCase)
                || string.Equals(row.Observation.FileName, _selectName, StringComparison.OrdinalIgnoreCase)).ToList();
            if (!string.IsNullOrWhiteSpace(_selectFramework))
            {
                match = byName.FirstOrDefault(row =>
                    string.Equals(row.Observation.TargetFramework, _selectFramework, StringComparison.OrdinalIgnoreCase));
            }

            if (match is null && byName.Count == 1)
            {
                match = byName[0];
            }
        }

        match ??= _latestRows[0];
        _suppressSelection = true;
        LatestGrid.SelectedItem = match;
        LatestGrid.ScrollIntoView(match);
        _suppressSelection = false;
        ShowEvidence(match.Observation, _latest);
    }

    private static string Describe(BuildReport latest, BuildReport? previous)
    {
        var lines = new List<string>();
        if (!latest.Comparable)
        {
            lines.Add($"{latest.KindLabel} is shown and is left out of the repeat comparison. Clean, Rebuild, and design-time builds are not compared.");
        }
        else if (previous is null)
        {
            lines.Add("There is no previous comparable build yet. A repeated reason needs the same project, configuration, reason, and file on two builds.");
        }
        else
        {
            lines.Add("Repeated means the same project, configuration, reason, and file appeared in this build and the previous comparable build. Timestamps are ignored.");
        }

        if (!string.IsNullOrWhiteSpace(latest.OutputSummary))
        {
            lines.Add("Build output summary: " + latest.OutputSummary + ". The summary counts are not used to decide whether a project was up to date.");
        }

        if (latest.SummaryUpToDateCount is int count
            && count > latest.Projects.Count(project => project.Outcome == ProjectOutcome.UpToDate))
        {
            lines.Add($"The summary says {count} project(s) were up to date. A project is listed as up to date only when a FastUpToDate line says so.");
        }

        if (!string.IsNullOrWhiteSpace(latest.Note))
        {
            lines.Add(latest.Note);
        }

        if (!string.IsNullOrWhiteSpace(latest.BinlogStatus))
        {
            lines.Add(latest.BinlogStatus + (string.IsNullOrWhiteSpace(latest.BinlogPath) ? "" : " (" + latest.BinlogPath + ")"));
        }

        if (latest.UnattributedEvidence.Count > 0)
        {
            lines.Add($"{latest.UnattributedEvidence.Count} line(s) could not be attributed to a project.");
        }

        return string.Join(Environment.NewLine, lines);
    }

    private void ShowEvidence(ProjectObservation project, BuildReport? report)
    {
        var lines = new List<string>();
        if (!string.IsNullOrWhiteSpace(project.RepeatedLabel))
        {
            lines.Add(project.RepeatedLabel);
        }

        lines.AddRange(project.Evidence);
        if (project.Evidence.Count == 0)
        {
            lines.Add(project.Reason);
        }

        if (report?.UnattributedEvidence.Count > 0)
        {
            lines.Add("");
            lines.Add("Unattributed lines");
            lines.AddRange(report.UnattributedEvidence);
        }

        ShowLines(lines, report);
    }

    private void ShowLines(IReadOnlyList<string> lines, BuildReport? report)
    {
        var document = new FlowDocument { PagePadding = new Thickness(4) };
        if (lines.Count == 0 && report?.UnattributedEvidence.Count > 0)
        {
            lines = report.UnattributedEvidence.Prepend("Unattributed lines").ToList();
        }

        if (lines.Count == 0)
        {
            document.Blocks.Add(new Paragraph(new Run("Select a project to see the original evidence.")));
        }

        foreach (var line in lines)
        {
            var paragraph = new Paragraph { Margin = new Thickness(0, 0, 0, 2) };
            AppendLine(paragraph, line);
            document.Blocks.Add(paragraph);
        }

        EvidenceBox.Document = document;
        ApplyEvidenceColors();
    }

    private void AppendLine(Paragraph paragraph, string line)
    {
        var index = 0;
        foreach (Match match in PathRegex.Matches(line))
        {
            if (match.Index > index)
            {
                paragraph.Inlines.Add(new Run(line[index..match.Index]));
            }

            var path = match.Value.TrimEnd('.', ',', ';', ')', ']', '"', '\'');
            if (File.Exists(path))
            {
                var link = new Hyperlink(new Run(path));
                var captured = path;
                link.Click += (_, _) => _session.OpenFile(captured);
                paragraph.Inlines.Add(link);
                var trailing = match.Value.Length - path.Length;
                if (trailing > 0)
                {
                    paragraph.Inlines.Add(new Run(match.Value[^trailing..]));
                }
            }
            else
            {
                paragraph.Inlines.Add(new Run(match.Value));
            }

            index = match.Index + match.Length;
        }

        if (index < line.Length)
        {
            paragraph.Inlines.Add(new Run(line[index..]));
        }
    }

    private void ApplyTheme()
    {
        var background = BrushFrom(EnvironmentColors.ToolWindowBackgroundColorKey, SystemColors.WindowColor);
        var foreground = BrushFrom(EnvironmentColors.ToolWindowTextColorKey, SystemColors.WindowTextColor);
        var border = BrushFrom(EnvironmentColors.ToolWindowBorderColorKey, SystemColors.ActiveBorderColor);
        Background = background;
        Foreground = foreground;
        HeadingText.Foreground = foreground;
        DetailText.Foreground = foreground;
        SetupText.Foreground = foreground;
        PreviousHeading.Foreground = foreground;
        StyleGrid(LatestGrid, background, foreground, border);
        StyleGrid(PreviousGrid, background, foreground, border);
        foreach (var button in new[] { ImportButton, AttachButton, ClearButton })
        {
            button.Background = background;
            button.Foreground = foreground;
            button.BorderBrush = border;
        }

        ApplyEvidenceColors();
    }

    private void ApplyEvidenceColors()
    {
        if (EvidenceBox is null)
        {
            return;
        }

        var background = BrushFrom(EnvironmentColors.ToolWindowBackgroundColorKey, SystemColors.WindowColor);
        var foreground = BrushFrom(EnvironmentColors.ToolWindowTextColorKey, SystemColors.WindowTextColor);
        EvidenceBox.Background = background;
        EvidenceBox.Foreground = foreground;
        if (EvidenceBox.Document is not null)
        {
            EvidenceBox.Document.Background = background;
            EvidenceBox.Document.Foreground = foreground;
        }
    }

    private static void StyleGrid(DataGrid grid, Brush background, Brush foreground, Brush border)
    {
        grid.Background = background;
        grid.Foreground = foreground;
        grid.BorderBrush = border;
        grid.RowBackground = background;
        grid.AlternatingRowBackground = background;
        grid.HorizontalGridLinesBrush = border;
        grid.VerticalGridLinesBrush = border;
        grid.ColumnHeaderStyle = new Style(typeof(DataGridColumnHeader))
        {
            Setters =
            {
                new Setter(Control.BackgroundProperty, background),
                new Setter(Control.ForegroundProperty, foreground),
                new Setter(Control.BorderBrushProperty, border),
                new Setter(Control.PaddingProperty, new Thickness(4, 2, 4, 2))
            }
        };
        var cellStyle = new Style(typeof(DataGridCell))
        {
            Setters =
            {
                new Setter(Control.BackgroundProperty, background),
                new Setter(Control.ForegroundProperty, foreground),
                new Setter(Control.BorderBrushProperty, border)
            }
        };
        var selected = new Trigger { Property = DataGridCell.IsSelectedProperty, Value = true };
        selected.Setters.Add(new Setter(Control.BackgroundProperty, SystemColors.HighlightBrush));
        selected.Setters.Add(new Setter(Control.ForegroundProperty, SystemColors.HighlightTextBrush));
        cellStyle.Triggers.Add(selected);
        grid.CellStyle = cellStyle;
    }

    private static Brush BrushFrom(ThemeResourceKey key, System.Windows.Media.Color fallback)
    {
        try
        {
            var color = VSColorTheme.GetThemedColor(key);
            var brush = new SolidColorBrush(Color.FromRgb(color.R, color.G, color.B));
            brush.Freeze();
            return brush;
        }
        catch (Exception)
        {
            var brush = new SolidColorBrush(fallback);
            brush.Freeze();
            return brush;
        }
    }

    private sealed class ProjectRow
    {
        public ProjectRow(ProjectObservation observation)
        {
            Observation = observation;
        }

        public ProjectObservation Observation { get; }

        public string ProjectLabel => Observation.ProjectLabel;

        public string OutcomeLabel => Observation.Outcome.ToLabel();

        public string Reason => Observation.Reason;

        public string RepeatedLabel => Observation.RepeatedLabel;
    }
}
