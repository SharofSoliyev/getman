using System.Collections;
using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using GetMan.Models;
using GetMan.Services;

namespace GetMan.Controls;

public partial class KeyValueGrid : UserControl
{
    public static readonly DependencyProperty RowsProperty = DependencyProperty.Register(
        nameof(Rows), typeof(IEnumerable), typeof(KeyValueGrid), new PropertyMetadata(null));

    public static readonly DependencyProperty ShowDescriptionProperty = DependencyProperty.Register(
        nameof(ShowDescription), typeof(bool), typeof(KeyValueGrid), new PropertyMetadata(true, OnColumnsChanged));

    public static readonly DependencyProperty AllowFilesProperty = DependencyProperty.Register(
        nameof(AllowFiles), typeof(bool), typeof(KeyValueGrid), new PropertyMetadata(false, OnColumnsChanged));

    public static readonly DependencyProperty ReadOnlyRowsProperty = DependencyProperty.Register(
        nameof(ReadOnlyRows), typeof(bool), typeof(KeyValueGrid), new PropertyMetadata(false, OnReadOnlyChanged));

    public IEnumerable Rows
    {
        get => (IEnumerable)GetValue(RowsProperty);
        set => SetValue(RowsProperty, value);
    }

    public bool ShowDescription
    {
        get => (bool)GetValue(ShowDescriptionProperty);
        set => SetValue(ShowDescriptionProperty, value);
    }

    public bool AllowFiles
    {
        get => (bool)GetValue(AllowFilesProperty);
        set => SetValue(AllowFilesProperty, value);
    }

    public bool ReadOnlyRows
    {
        get => (bool)GetValue(ReadOnlyRowsProperty);
        set => SetValue(ReadOnlyRowsProperty, value);
    }

    public ParamKind[] Kinds { get; } = { ParamKind.Text, ParamKind.File };

    public KeyValueGrid()
    {
        InitializeComponent();
        Loaded += (_, _) => ApplyColumns();
    }

    private static void OnColumnsChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
        => (d as KeyValueGrid)?.ApplyColumns();

    private static void OnReadOnlyChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is KeyValueGrid g)
        {
            g.Grid.CanUserAddRows = !g.ReadOnlyRows;
            g.Grid.CanUserDeleteRows = !g.ReadOnlyRows;
            g.Grid.IsReadOnly = g.ReadOnlyRows;
        }
    }

    private void ApplyColumns()
    {
        if (Grid == null) return;
        TypeColumn.Visibility = AllowFiles ? Visibility.Visible : Visibility.Collapsed;
        DescriptionColumn.Visibility = ShowDescription ? Visibility.Visible : Visibility.Collapsed;
    }

    private void OnRemoveRow(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not KeyValueItem item) return;
        if (Rows is IList list && list.Contains(item)) list.Remove(item);
    }

    private void OnPickFile(object sender, RoutedEventArgs e)
    {
        if (sender is not FrameworkElement fe || fe.Tag is not KeyValueItem item) return;
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = Loc.T("s.dlg_select_upload") };
        if (dlg.ShowDialog() == true)
        {
            item.FilePath = dlg.FileName;
            item.Value = System.IO.Path.GetFileName(dlg.FileName);
        }
    }
}
