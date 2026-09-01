using System.Collections.Generic;
using System.Windows;

namespace MpcConverter.App.Views;

public partial class InputDialog : Window
{
    public string Value => ValueBox.Text?.Trim() ?? "";

    public InputDialog(string prompt, string title, string initial = "",
        IEnumerable<string>? suggestions = null)
    {
        InitializeComponent();
        Title = title;
        PromptText.Text = prompt;
        if (suggestions is not null)
            ValueBox.ItemsSource = new List<string>(suggestions);
        ValueBox.Text = initial;
        Loaded += (_, _) => { ValueBox.Focus(); };
    }

    /// <summary>
    /// Shows the dialog; returns the entered/selected text, or null if cancelled/blank.
    /// When <paramref name="suggestions"/> is given the input is an editable dropdown.
    /// </summary>
    public static string? Ask(Window owner, string prompt, string title,
        string initial = "", IEnumerable<string>? suggestions = null)
    {
        var dlg = new InputDialog(prompt, title, initial, suggestions) { Owner = owner };
        if (dlg.ShowDialog() == true && !string.IsNullOrWhiteSpace(dlg.Value))
            return dlg.Value;
        return null;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
