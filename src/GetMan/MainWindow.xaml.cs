using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Threading;
using GetMan.Models;
using GetMan.ViewModels;
using GetMan.Views;

namespace GetMan;

public partial class MainWindow : Window
{
    private readonly MainViewModel _vm;
    private Point _dragStart;
    private CollectionNode _dragNode;

    public MainWindow()
    {
        InitializeComponent();
        _vm = new MainViewModel();
        DataContext = _vm;
        Closing += (_, _) => _vm.Shutdown();
        ApplyWindowStateChrome();
    }

    #region window chrome

    private void OnMinimiseWindow(object sender, RoutedEventArgs e) => WindowState = WindowState.Minimized;

    private void OnMaximiseWindow(object sender, RoutedEventArgs e) =>
        WindowState = WindowState == WindowState.Maximized ? WindowState.Normal : WindowState.Maximized;

    private void OnCloseWindow(object sender, RoutedEventArgs e) => Close();

    private void OnWindowStateChanged(object sender, EventArgs e) => ApplyWindowStateChrome();

    /// <summary>
    /// A WindowChrome window maximises to the monitor rather than the work area's visible edge,
    /// so its own resize border ends up off-screen and the app bar loses its top few pixels. The
    /// content gets that thickness back as padding while maximised.
    ///
    /// The maximise glyph is swapped by visibility rather than by retargeting one Path: a trigger
    /// cannot change Path.Data on a named element inside a template.
    /// </summary>
    private void ApplyWindowStateChrome()
    {
        var maximised = WindowState == WindowState.Maximized;

        WindowRoot.Margin = maximised
            ? new Thickness(SystemParameters.WindowResizeBorderThickness.Left,
                            SystemParameters.WindowResizeBorderThickness.Top,
                            SystemParameters.WindowResizeBorderThickness.Right,
                            SystemParameters.WindowResizeBorderThickness.Bottom)
            : new Thickness(0);

        MaximiseGlyph.Visibility = maximised ? Visibility.Collapsed : Visibility.Visible;
        RestoreGlyph.Visibility = maximised ? Visibility.Visible : Visibility.Collapsed;

        var tip = Services.Loc.T(maximised ? "s.restore" : "s.maximise");
        MaximiseButton.ToolTip = tip;
        System.Windows.Automation.AutomationProperties.SetName(MaximiseButton, tip);
    }

    #endregion

    #region tree

    private void OnTreeSelectionChanged(object sender, RoutedPropertyChangedEventArgs<object> e)
    {
        _vm.SelectedNode = e.NewValue as CollectionNode;
    }

    private void OnTreeDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if (e.OriginalSource is DependencyObject d && FindAncestor<TextBox>(d) != null) return;
        if (_vm.SelectedNode is { Kind: NodeKind.Request } node)
        {
            _vm.OpenNode(node);
            e.Handled = true;
        }
    }

    private void OnContextOpen(object sender, RoutedEventArgs e)
    {
        if (_vm.SelectedNode is { Kind: NodeKind.Request } node) _vm.OpenNode(node);
    }

    private void OnEditNodeVariables(object sender, RoutedEventArgs e)
    {
        var node = _vm.SelectedNode;
        if (node == null) return;
        var container = node.Kind == NodeKind.Request ? node.Parent : node;
        if (container == null) return;

        var w = new NodeSettingsWindow(container) { Owner = this };
        if (w.ShowDialog() == true) _vm.SaveWorkspace();
    }

    /// <summary>
    /// WPF does not select a TreeViewItem on right-click, so a context menu would otherwise act
    /// on whatever was left-clicked last. Select the row under the pointer first.
    /// </summary>
    private void OnTreeRightButtonDown(object sender, MouseButtonEventArgs e)
    {
        var item = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject);
        if (item == null) return;
        item.IsSelected = true;
        item.Focus();
    }

    private void OnTreeKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.F2 || _vm.SelectedNode == null) return;
        _vm.RenameNodeCommand.Execute(_vm.SelectedNode);
        e.Handled = true;
    }

    /// <summary>
    /// The editor is created collapsed, so Loaded fires long before the row enters rename mode.
    /// Focus has to follow the visibility change instead, one dispatcher turn later so the box
    /// has been laid out by then.
    /// </summary>
    private void OnRenameBoxVisibleChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (sender is not TextBox box || e.NewValue is not true) return;

        // Remember the name we started from so Escape can put it back.
        if (box.DataContext is CollectionNode node) box.Tag = node.Name;

        box.Dispatcher.BeginInvoke(DispatcherPriority.Input, new Action(() =>
        {
            box.BringIntoView();
            box.Focus();
            Keyboard.Focus(box);
            box.SelectAll();
        }));
    }

    private void OnRenameKeyDown(object sender, KeyEventArgs e)
    {
        var box = sender as TextBox;
        switch (e.Key)
        {
            case Key.Enter:
                CommitRename(box);
                e.Handled = true;
                break;
            case Key.Escape:
                CancelRename(box);
                e.Handled = true;
                break;
        }
    }

    private void OnRenameLostFocus(object sender, RoutedEventArgs e) => CommitRename(sender as TextBox);

    private void CommitRename(TextBox box)
    {
        if (box?.DataContext is not CollectionNode node) return;
        if (!node.IsRenaming) return;   // Enter already committed; ignore the LostFocus echo.

        box.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
        if (string.IsNullOrWhiteSpace(node.Name))
            node.Name = box.Tag as string is { Length: > 0 } previous ? previous : "Untitled";

        EndRename(box, node);
    }

    private void CancelRename(TextBox box)
    {
        if (box?.DataContext is not CollectionNode node) return;
        if (box.Tag is string original && !string.IsNullOrEmpty(original)) node.Name = original;
        EndRename(box, node);
    }

    private void EndRename(TextBox box, CollectionNode node)
    {
        node.IsRenaming = false;

        foreach (var tab in _vm.Tabs.Where(t => t.Node == node))
            tab.Title = node.Name;

        // Hand focus back to the row so the tree keeps its keyboard context.
        FindAncestor<TreeViewItem>(box)?.Focus();
        _vm.SaveWorkspace();
    }

    #endregion

    #region drag & drop

    private void OnTreeMouseDown(object sender, MouseButtonEventArgs e)
    {
        _dragStart = e.GetPosition(null);
        _dragNode = (e.OriginalSource as DependencyObject) is { } src
            ? FindAncestor<TreeViewItem>(src)?.DataContext as CollectionNode
            : null;
    }

    private void OnTreeMouseMove(object sender, MouseEventArgs e)
    {
        if (e.LeftButton != MouseButtonState.Pressed || _dragNode == null) return;
        var pos = e.GetPosition(null);
        if (Math.Abs(pos.X - _dragStart.X) < SystemParameters.MinimumHorizontalDragDistance &&
            Math.Abs(pos.Y - _dragStart.Y) < SystemParameters.MinimumVerticalDragDistance) return;

        try { DragDrop.DoDragDrop(Tree, _dragNode, DragDropEffects.Move); }
        catch { }
        finally { _dragNode = null; }
    }

    private void OnTreeDragOver(object sender, DragEventArgs e)
    {
        e.Effects = e.Data.GetDataPresent(typeof(CollectionNode)) ? DragDropEffects.Move : DragDropEffects.None;
        e.Handled = true;
    }

    private void OnTreeDrop(object sender, DragEventArgs e)
    {
        if (!e.Data.GetDataPresent(typeof(CollectionNode))) return;
        var source = e.Data.GetData(typeof(CollectionNode)) as CollectionNode;
        var target = FindAncestor<TreeViewItem>(e.OriginalSource as DependencyObject)?.DataContext as CollectionNode;
        _vm.MoveNode(source, target);
        e.Handled = true;
    }

    #endregion

    #region environments & history

    private void OnAddEnvironment(object sender, RoutedEventArgs e)
    {
        var env = new EnvironmentModel { Name = "New Environment" };
        _vm.Environments.Add(env);
        _vm.SelectedEnvironment = env;
        var w = new VariableEditorWindow(env.Name, env.Variables, env) { Owner = this };
        w.ShowDialog();
        _vm.SaveWorkspace();
    }

    private void OnEditGlobals(object sender, RoutedEventArgs e)
    {
        var w = new VariableEditorWindow("Global variables", _vm.Globals.Variables) { Owner = this };
        w.ShowDialog();
        _vm.SaveWorkspace();
    }

    private void OnEnvironmentDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as ListBox)?.SelectedItem is not EnvironmentModel env) return;
        var w = new VariableEditorWindow(env.Name, env.Variables, env) { Owner = this };
        w.ShowDialog();
        _vm.SaveWorkspace();
    }

    private void OnHistoryDoubleClick(object sender, MouseButtonEventArgs e)
    {
        if ((sender as ListBox)?.SelectedItem is HistoryEntry entry)
            _vm.OpenHistoryEntryCommand.Execute(entry);
    }

    #endregion

    private void OnNewMenuClick(object sender, RoutedEventArgs e)
    {
        if (sender is not Button button || button.ContextMenu == null) return;
        button.ContextMenu.PlacementTarget = button;
        button.ContextMenu.Placement = System.Windows.Controls.Primitives.PlacementMode.Bottom;
        button.ContextMenu.DataContext = DataContext;
        button.ContextMenu.IsOpen = true;
    }

    private static T FindAncestor<T>(DependencyObject current) where T : DependencyObject
    {
        while (current != null)
        {
            if (current is T match) return match;
            current = current is Visual or System.Windows.Media.Media3D.Visual3D
                ? VisualTreeHelper.GetParent(current)
                : LogicalTreeHelper.GetParent(current);
        }
        return null;
    }
}
