using System.Windows;
using System.Windows.Controls;
using GetMan.Services;
using GetMan.ViewModels;

namespace GetMan.Views;

public partial class BodyView : UserControl
{
    public BodyView() => InitializeComponent();

    private void OnPickBinary(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RequestTabViewModel vm) return;
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = Loc.T("s.dlg_select_body_file") };
        if (dlg.ShowDialog() == true)
            vm.Request.Body.BinaryPath = dlg.FileName;
    }
}
