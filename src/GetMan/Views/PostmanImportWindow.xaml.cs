using System.Collections.ObjectModel;
using System.Diagnostics;
using System.Windows;
using GetMan.Models;
using GetMan.Services;
using GetMan.ViewModels;

namespace GetMan.Views;

public partial class PostmanImportWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly ObservableCollection<DiscoveredFile> _files = new();
    private readonly ObservableCollection<PostmanRemoteItem> _remote = new();
    private readonly List<string> _extraFolders = new();
    private PostmanInstall _install;

    public PostmanImportWindow(MainViewModel vm)
    {
        InitializeComponent();
        _vm = vm;

        FileGrid.ItemsSource = _files;
        RemoteGrid.ItemsSource = _remote;
        ApiKeyBox.Text = _vm.Settings.PostmanApiKey ?? string.Empty;

        Footnote.Text = Loc.T("s.postman_footnote");

        Loaded += (_, _) => { if (_preview) return; Detect(); Rescan(); };
    }

    private void Detect()
    {
        _install = PostmanDiscovery.Detect();
        if (_install.Installed)
        {
            StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CheckCircleOutline;
            StatusTitle.Text = string.IsNullOrEmpty(_install.Version)
                ? Loc.T("s.postman_installed")
                : Loc.T("s.postman_installed_version", _install.Version);
            StatusDetail.Text = _install.HasLocalDatabase
                ? Loc.T("s.postman_db_found")
                : Loc.T("s.postman_db_missing");
        }
        else
        {
            StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.InformationOutline;
            StatusTitle.Text = Loc.T("s.postman_not_installed");
            StatusDetail.Text = Loc.T("s.postman_not_installed_detail");
        }
    }

    private bool _preview;

    /// <summary>
    /// Shows a made-up scan for the documentation screenshots. The real scan would put the
    /// reader's own collection names and file paths into the README.
    /// </summary>
    internal void SeedPreview()
    {
        _preview = true;

        StatusIcon.Kind = MaterialDesignThemes.Wpf.PackIconKind.CheckCircleOutline;
        StatusTitle.Text = Loc.T("s.postman_installed_version", "11.2.0");
        StatusDetail.Text = Loc.T("s.postman_db_found");

        foreach (var (name, kind, requests, size) in new[]
                 {
                     ("Billing API", "collection", 34, 62_144L),
                     ("Billing API - staging", "environment", 0, 1_208L),
                     ("Identity service", "collection", 21, 41_020L),
                     ("Payments webhooks", "collection", 9, 15_360L)
                 })
        {
            _files.Add(new DiscoveredFile
            {
                Name = name,
                Title = name,
                Kind = kind,
                RequestCount = requests,
                Size = size,
                Modified = new DateTime(2025, 8, 11, 9, 53, 0),
                Path = @"C:\Users\example\Downloads\" + name.Replace(' ', '-') + ".postman_collection.json"
            });
        }

        NoFilesPanel.Visibility = Visibility.Collapsed;
        FileGrid.Visibility = Visibility.Visible;
        FileSummary.Text = Loc.T("s.files_found_summary", _files.Count,
            _files.Count(f => f.Kind == "collection"), _files.Count(f => f.Kind == "environment"));
    }

    private void Rescan()
    {
        _files.Clear();
        foreach (var f in PostmanDiscovery.FindExportFiles(_extraFolders)) _files.Add(f);

        NoFilesPanel.Visibility = _files.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
        FileGrid.Visibility = _files.Count == 0 ? Visibility.Collapsed : Visibility.Visible;
        FileSummary.Text = _files.Count == 0
            ? string.Empty
            : Loc.T("s.files_found_summary", _files.Count, _files.Count(f => f.Kind == "collection"),
                  _files.Count(f => f.Kind == "environment"));

        ShowSnapshotNote();
    }

    /// <summary>
    /// Says out loud what the Modified column only implies. An export holds what existed when it
    /// was written, so importing a folder of old files looks like a complete import and is not one:
    /// anything created or edited since, and anything never exported at all, is simply absent.
    /// </summary>
    private void ShowSnapshotNote()
    {
        if (_files.Count == 0)
        {
            SnapshotNote.Visibility = Visibility.Collapsed;
            return;
        }

        var oldest = _files.Min(f => f.Modified);
        var newest = _files.Max(f => f.Modified);
        var age = (int)Math.Floor((DateTime.Now - newest).TotalDays);

        SnapshotNoteText.Text = Loc.T("s.export_files_are_snapshots",
            oldest.ToString("yyyy-MM-dd"), newest.ToString("yyyy-MM-dd"), Math.Max(0, age));

        SnapshotNote.Visibility = Visibility.Visible;
    }

    private void OnRescan(object sender, RoutedEventArgs e) => Rescan();

    private void OnAddFolder(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFolderDialog { Title = Loc.T("s.pick_scan_folder") };
        if (dlg.ShowDialog() == true)
        {
            _extraFolders.Add(dlg.FolderName);
            Rescan();
        }
    }

    private void OnSelectAllFiles(object sender, RoutedEventArgs e) => SetAll(true);
    private void OnSelectNoFiles(object sender, RoutedEventArgs e) => SetAll(false);

    private void SetAll(bool value)
    {
        foreach (var f in _files) f.Selected = value;
        FileGrid.Items.Refresh();
    }

    private void OnOpenKeyPage(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo("https://go.postman.co/settings/me/api-keys") { UseShellExecute = true });
        }
        catch { }
    }

    private async void OnFetch(object sender, RoutedEventArgs e)
    {
        var key = ApiKeyBox.Text?.Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            ApiStatus.Text = Loc.T("s.paste_api_key_first");
            return;
        }

        FetchButton.IsEnabled = false;
        ApiStatus.Text = Loc.T("s.contacting_postman");
        _remote.Clear();

        try
        {
            using var client = new PostmanApiClient(key);
            var items = await client.ListAsync();
            foreach (var i in items) _remote.Add(i);

            _vm.Settings.PostmanApiKey = key;
            _vm.QueueSave();

            ApiStatus.Text = items.Count == 0
                ? Loc.T("s.key_worked_no_collections")
                : Loc.T("s.found_remote_summary", items.Count(i => i.Kind == "collection"),
                    items.Count(i => i.Kind == "environment"));
        }
        catch (Exception ex)
        {
            ApiStatus.Text = Loc.T("s.postman_api_error", ex.Message);
        }
        finally
        {
            FetchButton.IsEnabled = true;
        }
    }

    private async void OnImport(object sender, RoutedEventArgs e)
    {
        ImportButton.IsEnabled = false;
        int collections = 0, environments = 0;
        var problems = new List<string>();

        try
        {
            foreach (var file in _files.Where(f => f.Selected).ToList())
            {
                var result = PostmanImporter.ImportFile(file.Path);
                if (!string.IsNullOrEmpty(result.Error))
                {
                    problems.Add($"{file.Name}: {result.Error}");
                    continue;
                }
                Apply(result, ref collections, ref environments);
            }

            var picked = _remote.Where(r => r.Selected).ToList();
            if (picked.Count > 0)
            {
                using var client = new PostmanApiClient(ApiKeyBox.Text?.Trim());
                foreach (var item in picked)
                {
                    try
                    {
                        var json = await client.DownloadAsync(item);
                        var result = PostmanImporter.ImportText(json, item.Name);
                        if (!string.IsNullOrEmpty(result.Error))
                        {
                            problems.Add($"{item.Name}: {result.Error}");
                            continue;
                        }
                        Apply(result, ref collections, ref environments);
                    }
                    catch (Exception ex)
                    {
                        problems.Add($"{item.Name}: {ex.Message}");
                    }
                }
            }
        }
        finally
        {
            ImportButton.IsEnabled = true;
        }

        if (collections == 0 && environments == 0 && problems.Count == 0)
        {
            MessageDialog.Info(Loc.T("s.dlg_nothing_selected_body"), Loc.T("s.dlg_nothing_selected_title"), this);
            return;
        }

        _vm.SaveWorkspace();
        _vm.StatusMessage = Loc.T("s.msg_imported_postman", collections, environments);

        if (problems.Count > 0)
            MessageDialog.Warn(Loc.T("s.dlg_partial_import_body"), Loc.T("s.dlg_partial_import_title"),
                string.Join("\n", problems.Take(12)), this);

        DialogResult = true;
        Close();
    }

    private void Apply(ImportResult result, ref int collections, ref int environments)
    {
        foreach (var c in result.Collections)
        {
            _vm.Collections.Add(c);
            collections++;
        }
        foreach (var env in result.Environments)
        {
            if (env.IsGlobal)
            {
                foreach (var v in env.Variables)
                {
                    var existing = _vm.Globals.Variables.FirstOrDefault(g => g.Key == v.Key);
                    if (existing != null) existing.Value = v.Value;
                    else _vm.Globals.Variables.Add(v);
                }
            }
            else _vm.Environments.Add(env);
            environments++;
        }
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }
}
