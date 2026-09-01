using System.Linq;
using System.Windows;
using MpcConverter.App.ViewModels;
using MpcConverter.App.Views;

namespace MpcConverter.App;

/// <summary>
/// Interaction logic for MainWindow.xaml
/// </summary>
public partial class MainWindow : Window
{
    public MainWindow()
    {
        InitializeComponent();
    }

    private MainViewModel Vm => (MainViewModel)DataContext;

    private PadRowViewModel[] SelectedRows() =>
        PadGrid.SelectedItems.OfType<PadRowViewModel>().ToArray();

    private void GroupSelected_Click(object sender, RoutedEventArgs e)
    {
        var rows = SelectedRows();
        if (rows.Length == 0)
        {
            MessageBox.Show("Select one or more pad rows first (Ctrl/Shift-click).",
                "Group selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        var initial = rows[0].DestTrackName ?? "";
        var name = InputDialog.Ask(this,
            $"Assign {rows.Length} selected pad(s) to track:", "Group selected", initial,
            suggestions: Vm.TrackNameOptions);
        if (name is not null)
            Vm.GroupRows(rows, name);
    }

    private void SkipSelected_Click(object sender, RoutedEventArgs e)
    {
        var rows = SelectedRows();
        if (rows.Length == 0)
        {
            MessageBox.Show("Select one or more pad rows first (Ctrl/Shift-click).",
                "Skip selected", MessageBoxButton.OK, MessageBoxImage.Information);
            return;
        }
        Vm.SkipRows(rows);
    }
}
