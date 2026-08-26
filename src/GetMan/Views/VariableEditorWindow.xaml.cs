using System.Collections.ObjectModel;
using System.Windows;
using GetMan.Models;

namespace GetMan.Views;

public partial class VariableEditorWindow : Window
{
    private readonly EnvironmentModel _environment;

    public VariableEditorWindow(string title, ObservableCollection<KeyValueItem> rows, EnvironmentModel environment = null)
    {
        InitializeComponent();
        Title = title;
        Heading.Text = title;
        Grid.Rows = rows;
        _environment = environment;

        if (environment != null)
        {
            NameRow.Visibility = Visibility.Visible;
            NameBox.Text = environment.Name;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        if (_environment != null && !string.IsNullOrWhiteSpace(NameBox.Text))
            _environment.Name = NameBox.Text.Trim();
        DialogResult = true;
        Close();
    }
}
