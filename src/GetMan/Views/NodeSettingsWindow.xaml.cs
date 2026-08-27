using System.Collections.ObjectModel;
using System.Windows;
using GetMan.Models;
using GetMan.Services;

namespace GetMan.Views;

public partial class NodeSettingsWindow : Window
{
    private readonly CollectionNode _node;
    private readonly AuthConfig _authDraft;
    private readonly ObservableCollection<KeyValueItem> _varsDraft = new();

    public NodeSettingsWindow(CollectionNode node)
    {
        InitializeComponent();
        _node = node;

        Title = node.Name + " settings";
        Heading.Text = node.Name;
        Subheading.Text = (node.Kind == NodeKind.Collection ? "Collection" : "Folder") + " - " + node.PathString();

        foreach (var v in node.Variables) _varsDraft.Add(v.Clone());
        VarGrid.Rows = _varsDraft;

        _authDraft = node.Auth?.Clone() ?? new AuthConfig { Type = AuthType.Inherit };
        Auth.DataContext = _authDraft;

        PreScript.BoundText = node.PreRequestScript ?? string.Empty;
        TestScript.BoundText = node.TestScript ?? string.Empty;
        DescriptionBox.BoundText = node.Description ?? string.Empty;
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        _node.Variables.Clear();
        foreach (var v in _varsDraft)
            if (!string.IsNullOrWhiteSpace(v.Key)) _node.Variables.Add(v);

        _node.Auth = _authDraft;
        _node.PreRequestScript = PreScript.Text;
        _node.TestScript = TestScript.Text;
        _node.Description = DescriptionBox.Text;

        DialogResult = true;
        Close();
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        var root = _node.AncestorsAndSelf().Last();
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = root.Name.Replace(' ', '_') + ".postman_collection.json",
            Filter = FileFilters.PostmanCollection
        };
        if (dlg.ShowDialog() == true)
            PersistenceService.ExportToFile(dlg.FileName, PostmanExporter.ExportCollection(root));
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
