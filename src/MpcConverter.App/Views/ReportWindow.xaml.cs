using System.Diagnostics;
using System.IO;
using System.Windows;
using MpcConverter.Core.Conversion;

namespace MpcConverter.App.Views;

public partial class ReportWindow : Window
{
    private readonly string _folder;

    public ReportWindow(ConversionReport report, string folder)
    {
        InitializeComponent();
        _folder = folder;

        SummaryText.Text =
            $"Tracks created: {report.TracksCreated}    " +
            $"Pads placed: {report.PadsPlaced}    " +
            $"Events moved: {report.EventsMoved}    " +
            $"Samples copied: {report.SamplesCopied}";
        PathRun.Text = folder;

        WarningsList.ItemsSource = report.Warnings.Count > 0
            ? report.Warnings
            : new[] { "(none)" };
        DecisionsList.ItemsSource = report.Decisions.Count > 0
            ? report.Decisions
            : new[] { "(none)" };
    }

    private void OpenFolder_Click(object sender, RoutedEventArgs e)
    {
        if (Directory.Exists(_folder))
            Process.Start(new ProcessStartInfo { FileName = _folder, UseShellExecute = true });
    }
}
