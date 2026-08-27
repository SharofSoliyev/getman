using System.IO;
using System.Windows;
using GetMan.Services;
using GetMan.ViewModels;

namespace GetMan.Views;

public partial class ImportWindow : Window
{
    private readonly MainViewModel _vm;

    public ImportWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;
        try
        {
            if (Clipboard.ContainsText()) Editor.BoundText = Clipboard.GetText();
        }
        catch { }
    }

    private void OnPickFile(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog { Filter = FileFilters.Import };
        if (dlg.ShowDialog() == true)
            Editor.BoundText = File.ReadAllText(dlg.FileName);
    }

    private void OnImport(object sender, RoutedEventArgs e)
    {
        var text = Editor.Text;
        if (string.IsNullOrWhiteSpace(text)) return;
        _vm.ImportFromText(text);
        DialogResult = true;
        Close();
    }

    private void OnCancel(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
