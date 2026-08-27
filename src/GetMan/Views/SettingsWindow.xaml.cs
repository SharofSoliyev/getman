using System.Diagnostics;
using System.Windows;
using GetMan.Models;
using GetMan.Services;

namespace GetMan.Views;

public partial class SettingsWindow : Window
{
    public SettingsWindow(AppSettings settings)
    {
        InitializeComponent();
        DataContext = settings;
        DataPathText.Text = "Workspace file: " + PersistenceService.WorkspacePath;
    }

    private void OnBrowseCert(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = FileFilters.Certificates
        };
        if (dlg.ShowDialog() == true && DataContext is AppSettings s)
            s.ClientCertPath = dlg.FileName;
    }

    private void OnOpenDataFolder(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo(PersistenceService.RootDir) { UseShellExecute = true });
        }
        catch { }
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
