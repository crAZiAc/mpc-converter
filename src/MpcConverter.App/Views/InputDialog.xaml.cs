using System.Windows;

namespace MpcConverter.App.Views;

public partial class InputDialog : Window
{
    public string Value => ValueBox.Text;

    public InputDialog(string prompt, string title, string initial = "")
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        ValueBox.Text = initial;
        Loaded += (_, _) => { ValueBox.Focus(); ValueBox.SelectAll(); };
    }

    /// <summary>Shows the dialog; returns the entered text, or null if cancelled/blank.</summary>
    public static string? Ask(Window owner, string prompt, string title, string initial = "")
    {
        var dlg = new InputDialog(prompt, title, initial) { Owner = owner };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Value))
            return dlg.Value.Trim();
        return null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
