using System.Windows;
using MpcConverter.App.ViewModels;

namespace MpcConverter.App.Views;

public partial class SettingsWindow : Window
{
    private readonly SettingsViewModel _vm;
    private bool _clearKey;

    public SettingsWindow(SettingsViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        DataContext = vm;
    }

    private void ClearKey_Click(object sender, RoutedEventArgs e)
    {
        _clearKey = true;
        ApiKeyBox.Clear();
        MessageBox.Show("The stored key will be removed when you click Save.", "Clear key",
            MessageBoxButton.OK, MessageBoxImage.Information);
    }

    private void Browse_Click(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = "Choose output folder" };
        if (dlg.ShowDialog() == true)
            _vm.OutputFolder = dlg.FolderName;
    }

    private void Save_Click(object sender, RoutedEventArgs e)
    {
        _vm.Save(ApiKeyBox.Password, _clearKey);
        DialogResult = true;
        Close();
    }
}
