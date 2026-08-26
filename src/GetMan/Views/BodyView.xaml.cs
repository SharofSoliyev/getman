using System.Windows;
using System.Windows.Controls;
using GetMan.ViewModels;

namespace GetMan.Views;

public partial class BodyView : UserControl
{
    public BodyView() => InitializeComponent();

    private void OnPickBinary(object sender, RoutedEventArgs e)
    {
        if (DataContext is not RequestTabViewModel vm) return;
        var dlg = new Microsoft.Win32.OpenFileDialog { Title = "Select the file to send as the request body" };
        if (dlg.ShowDialog() == true)
            vm.Request.Body.BinaryPath = dlg.FileName;
    }
}
