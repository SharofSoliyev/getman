using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using GetMan.Models;
using GetMan.ViewModels;

namespace GetMan.Views;

public partial class SaveRequestWindow : Window
{
    private readonly MainViewModel _vm;

    public CollectionNode TargetContainer { get; private set; }
    public string RequestName => string.IsNullOrWhiteSpace(NameBox.Text) ? "New Request" : NameBox.Text.Trim();

    public SaveRequestWindow(MainViewModel vm, string suggestedName)
    {
        InitializeComponent();
        _vm = vm;
        NameBox.Text = suggestedName;
        NameBox.Focus();
        NameBox.SelectAll();
        Rebuild();
    }

    private void Rebuild()
    {
        Tree.ItemsSource = null;
        Tree.ItemsSource = _vm.Collections;
        foreach (var c in _vm.Collections) c.IsExpanded = true;
        TargetContainer = _vm.SelectedNode?.Kind == NodeKind.Request
            ? _vm.SelectedNode.Parent
            : _vm.SelectedNode ?? _vm.Collections.FirstOrDefault();
    }

    private void OnSelected(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        if (e.NewValue is CollectionNode node && node.Kind != NodeKind.Request)
            TargetContainer = node;
    }

    private void OnNewCollection(object sender, RoutedEventArgs e)
    {
        var col = new CollectionNode { Kind = NodeKind.Collection, Name = "New Collection", IsExpanded = true };
        _vm.Collections.Add(col);
        TargetContainer = col;
        Rebuild();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (TargetContainer == null)
        {
            var col = new CollectionNode { Kind = NodeKind.Collection, Name = "My Collection", IsExpanded = true };
            _vm.Collections.Add(col);
            TargetContainer = col;
        }
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
