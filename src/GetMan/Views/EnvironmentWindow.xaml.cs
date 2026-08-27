using System.Collections.ObjectModel;
using System.Windows;
using System.Windows.Controls;
using GetMan.Models;
using GetMan.Services;
using GetMan.ViewModels;

namespace GetMan.Views;

public partial class EnvironmentWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ObservableCollection<EnvironmentModel> _all = new();
    private EnvironmentModel _current;
    private bool _loading;

    public EnvironmentWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        _all.Add(_vm.Globals);
        foreach (var e in _vm.Environments) _all.Add(e);
        EnvList.ItemsSource = _all;
        EnvList.SelectedItem = _vm.SelectedEnvironment ?? _all.FirstOrDefault();
    }

    private void OnEnvSelected(object sender, SelectionChangedEventArgs e)
    {
        _current = EnvList.SelectedItem as EnvironmentModel;
        _loading = true;
        NameBox.Text = _current?.Name ?? string.Empty;
        NameBox.IsEnabled = _current is { IsGlobal: false };
        ActiveBox.IsEnabled = _current is { IsGlobal: false };
        ActiveBox.IsChecked = _current != null && _current == _vm.SelectedEnvironment;
        VarGrid.Rows = _current?.Variables;
        _loading = false;
    }

    private void OnNameChanged(object sender, TextChangedEventArgs e)
    {
        if (_loading || _current == null || _current.IsGlobal) return;
        _current.Name = NameBox.Text;
    }

    private void OnSetActive(object sender, RoutedEventArgs e)
    {
        if (_current == null || _current.IsGlobal) return;
        _vm.SelectedEnvironment = ActiveBox.IsChecked == true ? _current : null;
    }

    private void OnAdd(object sender, RoutedEventArgs e)
    {
        var env = new EnvironmentModel { Name = "New Environment" };
        _vm.Environments.Add(env);
        _all.Add(env);
        EnvList.SelectedItem = env;
        NameBox.Focus();
        NameBox.SelectAll();
    }

    private void OnDuplicate(object sender, RoutedEventArgs e)
    {
        if (_current == null || _current.IsGlobal) return;
        var copy = _current.Clone();
        _vm.Environments.Add(copy);
        _all.Add(copy);
        EnvList.SelectedItem = copy;
    }

    private void OnDelete(object sender, RoutedEventArgs e)
    {
        if (_current == null || _current.IsGlobal) return;
        if (!MessageDialog.Confirm(Loc.T("s.dlg_delete_env_body", _current.Name),
                Loc.T("s.dlg_delete_env_title"), owner: this))
            return;

        if (_vm.SelectedEnvironment == _current) _vm.SelectedEnvironment = null;
        _vm.Environments.Remove(_current);
        _all.Remove(_current);
        EnvList.SelectedItem = _all.FirstOrDefault();
    }

    private void OnExport(object sender, RoutedEventArgs e)
    {
        if (_current == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = _current.Name.Replace(' ', '_') + ".postman_environment.json",
            Filter = FileFilters.PostmanEnvironment
        };
        if (dlg.ShowDialog() == true)
            PersistenceService.ExportToFile(dlg.FileName, PostmanExporter.ExportEnvironment(_current));
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        _vm.SaveWorkspace();
        DialogResult = true;
        Close();
    }
}
