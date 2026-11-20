using System.IO;
using System.Windows;
using Microsoft.Win32;

namespace WhyDidItBuild.UI;

public partial class ImportBuildOutputDialog : Window
{
    public ImportBuildOutputDialog()
    {
        InitializeComponent();
    }

    public string? OutputText { get; private set; }

    private void LoadFileClick(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Open Build output",
            Filter = "Text logs (*.txt;*.log)|*.txt;*.log|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == true)
        {
            OutputBox.Text = File.ReadAllText(dialog.FileName);
        }
    }

    private void ImportClick(object sender, RoutedEventArgs e)
    {
        OutputText = OutputBox.Text ?? "";
        DialogResult = true;
    }
}
