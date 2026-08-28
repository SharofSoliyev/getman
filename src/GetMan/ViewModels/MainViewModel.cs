using System.Collections.ObjectModel;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using System.Windows.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GetMan.Models;
using GetMan.Services;

namespace GetMan.ViewModels;

public partial class MainViewModel : ObservableObject
{
    private readonly HttpEngine _engine = new();
    private readonly RequestRunner _runner;
    private readonly DispatcherTimer _saveTimer;
    private WorkspaceFile _workspace;

    public MainViewModel()
    {
        _runner = new RequestRunner(_engine);
        _workspace = PersistenceService.Load();

        Settings = _workspace.Settings;
        Globals = _workspace.Globals;

        // Language first: everything built below reads its labels through the table.
        if (string.IsNullOrEmpty(Settings.Language)) Settings.Language = Loc.Detect();
        Loc.Instance.SetLanguage(Settings.Language);
        _selectedLanguage = Loc.Languages.FirstOrDefault(l => l.Code == Loc.Instance.Code);
        _statusMessage = Loc.T("s.ready");

        foreach (var c in _workspace.Collections) Collections.Add(c);
        foreach (var e in _workspace.Environments) Environments.Add(e);
        foreach (var h in _workspace.History.OrderByDescending(h => h.Timestamp)) History.Add(h);

        // The timer has to exist first: assigning SelectedEnvironment raises its changed
        // handler, which queues a save.
        _saveTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2) };
        _saveTimer.Tick += (_, _) => { _saveTimer.Stop(); SaveWorkspace(); };

        SelectedEnvironment = Environments.FirstOrDefault(e => e.Id == Settings.ActiveEnvironmentId);

        // Applied either way, not only for light: the Fluent controls take their accent from
        // resources this writes at run time, so skipping it on a dark start left them salmon.
        IsLightTheme = string.Equals(Settings.Theme, "Light", StringComparison.OrdinalIgnoreCase);
        Controls.ThemeManager.Apply(IsLightTheme ? Controls.AppTheme.Light : Controls.AppTheme.Dark);

        // What the {{...}} picker offers. A delegate rather than a snapshot, so switching
        // environment or opening a request in another collection changes the list with no
        // bookkeeping - the picker asks at the moment it opens.
        Services.VariableCatalog.Source = () => BuildResolver(SelectedTab?.Node ?? SelectedNode);

        RestoreTabs();
        if (Tabs.Count == 0) NewRequest();
    }

    #region state

    public ObservableCollection<CollectionNode> Collections { get; } = new();
    public ObservableCollection<EnvironmentModel> Environments { get; } = new();
    public ObservableCollection<HistoryEntry> History { get; } = new();
    public ObservableCollection<RequestTabViewModel> Tabs { get; } = new();

    public EnvironmentModel Globals { get; }
    public AppSettings Settings { get; }
    public HttpEngine Engine => _engine;
    public RequestRunner Runner => _runner;

    [ObservableProperty] private RequestTabViewModel _selectedTab;
    [ObservableProperty] private EnvironmentModel _selectedEnvironment;
    [ObservableProperty] private CollectionNode _selectedNode;
    [ObservableProperty] private string _searchText = string.Empty;
    [ObservableProperty] private int _sidebarTabIndex;
    [ObservableProperty] private string _statusMessage = Loc.T("s.ready");
    [ObservableProperty] private bool _isLightTheme;

    /// <summary>The three interface languages, bound to the picker in the app bar.</summary>
    public LanguageOption[] Languages => Loc.Languages;

    [ObservableProperty] private LanguageOption _selectedLanguage;

    partial void OnSelectedLanguageChanged(LanguageOption value)
    {
        if (value == null) return;
        Loc.Instance.SetLanguage(value.Code);
        Settings.Language = value.Code;

        // The status line is a plain string rather than a binding into the table, so it has to
        // be re-rendered by hand. Returning it to idle is the honest thing to show anyway.
        StatusMessage = Loc.T("s.ready");
        QueueSave();
    }

    partial void OnSelectedEnvironmentChanged(EnvironmentModel value)
    {
        Settings.ActiveEnvironmentId = value?.Id ?? string.Empty;
        QueueSave();
    }

    partial void OnSearchTextChanged(string value) => ApplyFilter(value);

    [RelayCommand]
    private void ToggleTheme()
    {
        Controls.ThemeManager.Toggle();
        IsLightTheme = Controls.ThemeManager.Current == Controls.AppTheme.Light;
        Settings.Theme = IsLightTheme ? "Light" : "Dark";
        QueueSave();
    }

    #endregion

    #region variables

    public VariableResolver BuildResolver(CollectionNode node)
    {
        var vars = new VariableResolver();
        vars.LoadGlobals(Globals);
        vars.LoadEnvironment(SelectedEnvironment);
        vars.LoadCollectionChain(node?.Parent ?? node);
        return vars;
    }

    /// <summary>Writes variables a script created back onto the right model. <paramref name="owner"/>
    /// is the request that ran, so collection-scoped writes land on its own collection.</summary>
    public void ApplyVariableOps(IEnumerable<VariableOp> ops, CollectionNode owner = null)
    {
        foreach (var op in ops)
        {
            var target = op.Scope switch
            {
                "globals" => Globals,
                "environment" => SelectedEnvironment,
                _ => null
            };

            if (op.Scope == "collection")
            {
                var col = (owner ?? SelectedTab?.Node)?.AncestorsAndSelf().LastOrDefault();
                if (col == null) continue;
                ApplyOpTo(col.Variables, op);
                continue;
            }

            if (target == null)
            {
                if (op.Scope == "environment")
                    StatusMessage = Loc.T("s.msg_script_no_env");
                continue;
            }
            ApplyOpTo(target.Variables, op);
        }
        QueueSave();
    }

    private static void ApplyOpTo(ObservableCollection<KeyValueItem> list, VariableOp op)
    {
        switch (op.Op)
        {
            case "set":
                {
                    var existing = list.FirstOrDefault(v => v.Key == op.Key);
                    if (existing != null) existing.Value = op.Value;
                    else list.Add(new KeyValueItem(op.Key, op.Value));
                    break;
                }
            case "unset":
                {
                    var existing = list.FirstOrDefault(v => v.Key == op.Key);
                    if (existing != null) list.Remove(existing);
                    break;
                }
            case "clear":
                list.Clear();
                break;
        }
    }

    #endregion

    #region execution

    public async Task<ExecutionResult> ExecuteAsync(CollectionNode node, RequestModel request, CancellationToken ct)
    {
        var vars = BuildResolver(node);
        StatusMessage = Loc.T("s.msg_sending", request.Method);

        var result = await _runner.ExecuteAsync(node, request, vars, Settings, 0, 1, ct).ConfigureAwait(true);

        ApplyVariableOps(result.VariableOps, node);

        if (result.Response != null)
        {
            AddHistory(request, result);
            StatusMessage = result.Response.HasError
                ? Loc.T("s.msg_request_failed", result.Response.Error)
                : Loc.T("s.msg_status_in_time", result.Response.StatusCode, result.Response.StatusText,
                    TextFormatter.HumanTime(result.Response.ElapsedMs));
        }
        return result;
    }

    private void AddHistory(RequestModel request, ExecutionResult result)
    {
        var entry = new HistoryEntry
        {
            Method = result.Request.Method,
            Url = result.Request.Url,
            StatusCode = result.Response.StatusCode,
            ElapsedMs = result.Response.ElapsedMs,
            SizeBytes = result.Response.SizeBytes,
            Request = request.Clone()
        };
        History.Insert(0, entry);
        while (History.Count > Math.Max(10, Settings.HistoryLimit)) History.RemoveAt(History.Count - 1);
        QueueSave();
    }

    #endregion

    #region tabs

    public RequestTabViewModel OpenNode(CollectionNode node)
    {
        if (node == null || node.Kind != NodeKind.Request) return null;

        var existing = Tabs.FirstOrDefault(t => t.Node == node);
        if (existing != null)
        {
            SelectedTab = existing;
            return existing;
        }

        var tab = new RequestTabViewModel(this, node, node.Request ?? new RequestModel(), node.Name);
        Tabs.Add(tab);
        SelectedTab = tab;
        return tab;
    }

    [RelayCommand]
    public void NewRequest()
    {
        var request = new RequestModel { Method = "GET", Url = string.Empty };
        request.Auth.Type = AuthType.Inherit;
        var tab = new RequestTabViewModel(this, null, request, "Untitled Request") { IsDirty = false };
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    [RelayCommand]
    private void CloseTab(RequestTabViewModel tab)
    {
        if (tab == null) return;
        tab.Cancel();
        var index = Tabs.IndexOf(tab);
        Tabs.Remove(tab);
        if (SelectedTab == tab)
            SelectedTab = Tabs.Count == 0 ? null : Tabs[Math.Min(index, Tabs.Count - 1)];
        if (Tabs.Count == 0) NewRequest();
        QueueSave();
    }

    [RelayCommand]
    private void CloseOtherTabs(RequestTabViewModel tab)
    {
        if (tab == null) return;
        foreach (var t in Tabs.Where(t => t != tab).ToList()) { t.Cancel(); Tabs.Remove(t); }
        SelectedTab = tab;
    }

    [RelayCommand]
    private void SaveTab(RequestTabViewModel tab)
    {
        tab ??= SelectedTab;
        if (tab == null) return;

        if (tab.Node == null)
        {
            SaveTabAs(tab);
            return;
        }

        tab.Node.Request = tab.Request;
        tab.Node.Name = tab.Title;
        tab.IsDirty = false;
        StatusMessage = Loc.T("s.msg_saved", tab.Title);
        SaveWorkspace();
    }

    [RelayCommand]
    private void SaveTabAs(RequestTabViewModel tab)
    {
        tab ??= SelectedTab;
        if (tab == null) return;

        var dlg = new Views.SaveRequestWindow(this, tab.Title) { Owner = Application.Current.MainWindow };
        if (dlg.ShowDialog() != true || dlg.TargetContainer == null) return;

        var node = new CollectionNode
        {
            Kind = NodeKind.Request,
            Name = dlg.RequestName,
            Request = tab.Request,
            Parent = dlg.TargetContainer
        };
        node.Request.Auth.Type = node.Request.Auth.Type == AuthType.None ? AuthType.Inherit : node.Request.Auth.Type;
        dlg.TargetContainer.Children.Add(node);
        dlg.TargetContainer.IsExpanded = true;

        tab.Node = node;
        tab.Title = dlg.RequestName;
        tab.IsDirty = false;
        StatusMessage = Loc.T("s.msg_saved_to", node.Name, dlg.TargetContainer.Name);
        SaveWorkspace();
    }

    private void RestoreTabs()
    {
        foreach (var state in _workspace.OpenTabs)
        {
            CollectionNode node = null;
            if (!string.IsNullOrEmpty(state.NodeId))
                node = Collections.SelectMany(c => c.Flatten()).FirstOrDefault(n => n.Id == state.NodeId);

            if (node != null)
            {
                var tab = new RequestTabViewModel(this, node, node.Request ?? new RequestModel(), node.Name) { IsDirty = false };
                Tabs.Add(tab);
                if (state.IsActive) SelectedTab = tab;
            }
            else if (state.Request != null)
            {
                var tab = new RequestTabViewModel(this, null, state.Request, state.Title ?? "Untitled Request") { IsDirty = true };
                Tabs.Add(tab);
                if (state.IsActive) SelectedTab = tab;
            }
        }
        SelectedTab ??= Tabs.FirstOrDefault();
    }

    #endregion

    #region tree operations

    [RelayCommand]
    private void NewCollection()
    {
        var col = new CollectionNode { Kind = NodeKind.Collection, Name = "New Collection", IsExpanded = true };
        Collections.Add(col);
        SelectedNode = col;
        col.IsRenaming = true;
        SaveWorkspace();
    }

    [RelayCommand]
    private void NewFolder(CollectionNode parent)
    {
        parent ??= SelectedNode;
        if (parent == null || parent.Kind == NodeKind.Request) parent = parent?.Parent;
        if (parent == null) return;

        var folder = new CollectionNode { Kind = NodeKind.Folder, Name = "New Folder", Parent = parent, IsExpanded = true };
        parent.Children.Add(folder);
        foreach (var ancestor in parent.AncestorsAndSelf()) ancestor.IsExpanded = true;
        folder.IsRenaming = true;
        SaveWorkspace();
    }

    [RelayCommand]
    private void NewRequestIn(CollectionNode parent)
    {
        parent ??= SelectedNode;
        if (parent == null) return;
        if (parent.Kind == NodeKind.Request) parent = parent.Parent;
        if (parent == null) return;

        var request = new RequestModel { Method = "GET" };
        request.Auth.Type = AuthType.Inherit;
        var node = new CollectionNode { Kind = NodeKind.Request, Name = "New Request", Request = request, Parent = parent };
        parent.Children.Add(node);
        foreach (var ancestor in parent.AncestorsAndSelf()) ancestor.IsExpanded = true;
        OpenNode(node);
        node.IsRenaming = true;
        SaveWorkspace();
    }

    [RelayCommand]
    private void RenameNode(CollectionNode node)
    {
        node ??= SelectedNode;
        if (node == null) return;

        // The inline editor only exists once the row is realised, so open the way down to it.
        foreach (var ancestor in node.AncestorsAndSelf().Skip(1)) ancestor.IsExpanded = true;
        node.IsSelected = true;
        node.IsRenaming = true;
    }

    [RelayCommand]
    private void DeleteNode(CollectionNode node)
    {
        node ??= SelectedNode;
        if (node == null) return;

        var title = node.Kind == NodeKind.Request ? "s.dlg_delete_request_title"
            : node.Kind == NodeKind.Folder ? "s.dlg_delete_folder_title"
            : "s.dlg_delete_collection_title";
        if (!Views.MessageDialog.Confirm(
                Loc.T("s.dlg_delete_node_body", node.Name), Loc.T(title)))
            return;

        foreach (var tab in Tabs.Where(t => t.Node != null && node.Flatten().Contains(t.Node)).ToList())
        {
            tab.Node = null;
            tab.IsDirty = true;
        }

        if (node.Parent != null) node.Parent.Children.Remove(node);
        else Collections.Remove(node);
        SaveWorkspace();
    }

    [RelayCommand]
    private void DuplicateNode(CollectionNode node)
    {
        node ??= SelectedNode;
        if (node == null) return;

        var copy = node.DeepClone();
        copy.Name = node.Name + " Copy";
        if (node.Parent != null)
        {
            copy.Parent = node.Parent;
            node.Parent.Children.Insert(node.Parent.Children.IndexOf(node) + 1, copy);
        }
        else Collections.Add(copy);
        SaveWorkspace();
    }

    /// <summary>Moves a node into a new container (drag and drop).</summary>
    public void MoveNode(CollectionNode source, CollectionNode target)
    {
        if (source == null || target == null || source == target) return;
        if (source.Flatten().Contains(target)) return; // cannot drop into own subtree

        var container = target.Kind == NodeKind.Request ? target.Parent : target;
        if (container == null) return;

        if (source.Parent != null) source.Parent.Children.Remove(source);
        else Collections.Remove(source);

        source.Parent = container;
        if (target.Kind == NodeKind.Request)
        {
            var idx = container.Children.IndexOf(target);
            container.Children.Insert(Math.Max(0, idx + 1), source);
        }
        else
        {
            container.Children.Add(source);
            container.IsExpanded = true;
        }
        SaveWorkspace();
    }

    private void ApplyFilter(string text)
    {
        var q = (text ?? string.Empty).Trim();
        foreach (var root in Collections)
            FilterNode(root, q);
    }

    private static bool FilterNode(CollectionNode node, string q)
    {
        bool self = string.IsNullOrEmpty(q) ||
                    node.Name.Contains(q, StringComparison.OrdinalIgnoreCase) ||
                    (node.Request?.Url?.Contains(q, StringComparison.OrdinalIgnoreCase) ?? false);

        bool anyChild = false;
        foreach (var c in node.Children)
            anyChild |= FilterNode(c, q);

        node.IsVisible = self || anyChild;
        if (!string.IsNullOrEmpty(q) && anyChild) node.IsExpanded = true;
        return node.IsVisible;
    }

    #endregion

    #region import / export

    [RelayCommand]
    private void ImportFile()
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = FileFilters.Import,
            Multiselect = true,
            Title = Loc.T("s.dlg_import_title")
        };
        if (dlg.ShowDialog() != true) return;

        int collections = 0, environments = 0;
        var warnings = new List<string>();
        foreach (var file in dlg.FileNames)
        {
            var result = PostmanImporter.ImportFile(file);
            if (!string.IsNullOrEmpty(result.Error))
            {
                warnings.Add($"{Path.GetFileName(file)}: {result.Error}");
                continue;
            }
            foreach (var c in result.Collections) { Collections.Add(c); collections++; }
            foreach (var e in result.Environments)
            {
                if (e.IsGlobal)
                {
                    foreach (var v in e.Variables)
                        if (!Globals.Variables.Any(g => g.Key == v.Key)) Globals.Variables.Add(v);
                }
                else Environments.Add(e);
                environments++;
            }
            warnings.AddRange(result.Warnings);
        }

        SaveWorkspace();
        StatusMessage = Loc.T("s.msg_imported", collections, environments);
        if (warnings.Count > 0)
            Views.MessageDialog.Warn(Loc.T("s.dlg_import_notes_body"), Loc.T("s.dlg_import_notes_title"),
                string.Join("\n", warnings.Take(15)));
    }

    /// <summary>Pull collections straight from a Postman install or a Postman account.</summary>
    [RelayCommand]
    private void ImportFromPostman()
    {
        var w = new Views.PostmanImportWindow(this) { Owner = Application.Current.MainWindow };
        w.ShowDialog();
    }

    [RelayCommand]
    private void ImportRaw()
    {
        var dlg = new Views.ImportWindow(this) { Owner = Application.Current.MainWindow };
        dlg.ShowDialog();
    }

    public void ImportFromText(string text)
    {
        var trimmed = (text ?? string.Empty).TrimStart();
        if (trimmed.StartsWith("curl", StringComparison.OrdinalIgnoreCase))
        {
            var node = CurlImporter.Parse(trimmed);
            var tab = new RequestTabViewModel(this, null, node.Request, node.Name) { IsDirty = true };
            Tabs.Add(tab);
            SelectedTab = tab;
            StatusMessage = Loc.T("s.msg_curl_imported");
            return;
        }

        var result = PostmanImporter.ImportText(text, "Imported");
        if (!string.IsNullOrEmpty(result.Error))
        {
            Views.MessageDialog.Warn(result.Error, Loc.T("s.dlg_import_failed_title"));
            return;
        }
        foreach (var c in result.Collections) Collections.Add(c);
        foreach (var e in result.Environments)
        {
            if (e.IsGlobal) foreach (var v in e.Variables) Globals.Variables.Add(v);
            else Environments.Add(e);
        }
        SaveWorkspace();
        StatusMessage = Loc.T("s.msg_imported", result.Collections.Count, result.Environments.Count);
    }

    [RelayCommand]
    private void ExportCollection(CollectionNode node)
    {
        node ??= SelectedNode;
        if (node == null) return;
        var root = node.AncestorsAndSelf().Last();

        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = Sanitize(root.Name) + ".postman_collection.json",
            Filter = FileFilters.PostmanCollection
        };
        if (dlg.ShowDialog() != true) return;

        PersistenceService.ExportToFile(dlg.FileName, PostmanExporter.ExportCollection(root));
        StatusMessage = Loc.T("s.msg_exported_to", dlg.FileName);
    }

    [RelayCommand]
    private void ExportEnvironment(EnvironmentModel env)
    {
        env ??= SelectedEnvironment;
        if (env == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = Sanitize(env.Name) + ".postman_environment.json",
            Filter = FileFilters.PostmanEnvironment
        };
        if (dlg.ShowDialog() != true) return;
        PersistenceService.ExportToFile(dlg.FileName, PostmanExporter.ExportEnvironment(env));
        StatusMessage = Loc.T("s.msg_exported_to", dlg.FileName);
    }

    private static string Sanitize(string name)
    {
        foreach (var c in Path.GetInvalidFileNameChars()) name = name.Replace(c, '_');
        return name;
    }

    #endregion

    #region windows

    [RelayCommand]
    private void OpenEnvironments()
    {
        var w = new Views.EnvironmentWindow(this) { Owner = Application.Current.MainWindow };
        w.ShowDialog();
        QueueSave();
    }

    [RelayCommand]
    private void OpenSettings()
    {
        var w = new Views.SettingsWindow(Settings) { Owner = Application.Current.MainWindow };
        w.ShowDialog();
        QueueSave();
    }

    [RelayCommand]
    private void OpenCookies()
    {
        var w = new Views.CookieWindow(_engine) { Owner = Application.Current.MainWindow };
        w.ShowDialog();
    }

    [RelayCommand]
    private void OpenCode()
    {
        if (SelectedTab == null) return;
        var vars = BuildResolver(SelectedTab.Node);
        var prepared = RequestPreparer.Prepare(SelectedTab.Request, SelectedTab.Node, vars, Settings);
        AuthApplier.Apply(prepared, RequestPreparer.ResolveAuthVariables(prepared.Auth, vars));
        var w = new Views.CodeWindow(prepared) { Owner = Application.Current.MainWindow };
        w.ShowDialog();
    }

    [RelayCommand]
    private void RunCollection(CollectionNode node)
    {
        node ??= SelectedNode;
        if (node == null || node.Kind == NodeKind.Request) node = node?.Parent;
        if (node == null)
        {
            Views.MessageDialog.Info(Loc.T("s.dlg_nothing_to_run_body"), Loc.T("s.dlg_nothing_to_run_title"));
            return;
        }
        var w = new Views.RunnerWindow(this, node) { Owner = Application.Current.MainWindow };
        w.Show();
    }

    [RelayCommand]
    private void ClearHistory()
    {
        History.Clear();
        SaveWorkspace();
    }

    [RelayCommand]
    private void OpenHistoryEntry(HistoryEntry entry)
    {
        if (entry?.Request == null) return;
        var tab = new RequestTabViewModel(this, null, entry.Request.Clone(), entry.Method + " " + ShortUrl(entry.Url))
        {
            IsDirty = true
        };
        Tabs.Add(tab);
        SelectedTab = tab;
    }

    private static string ShortUrl(string url)
    {
        try
        {
            var uri = new Uri(UrlUtil.EnsureScheme(url));
            return uri.Host + uri.AbsolutePath;
        }
        catch { return url; }
    }

    #endregion

    #region persistence

    public void QueueSave()
    {
        // Property-changed handlers can fire before construction finishes; never assume the
        // timer is already there.
        if (_saveTimer == null) return;
        _saveTimer.Stop();
        _saveTimer.Start();
    }

    public void SaveWorkspace()
    {
        _workspace.Collections = Collections.ToList();
        _workspace.Environments = Environments.ToList();
        _workspace.Globals = Globals;
        _workspace.History = History.ToList();
        _workspace.Settings = Settings;
        _workspace.OpenTabs = Tabs.Select(t => new OpenTabState
        {
            NodeId = t.Node?.Id,
            Request = t.Node == null ? t.Request : null,
            Title = t.Title,
            IsActive = t == SelectedTab
        }).ToList();

        PersistenceService.Save(_workspace);
    }

    public void Shutdown()
    {
        SaveWorkspace();
        _engine.Dispose();
    }

    #endregion

    #region ui helpers

    /// <summary>Theme-reactive brush lookup shared with the value converters.</summary>
    public static Brush Brush(string key) => Controls.ThemePalette.Get(key);

    public static Brush BrushForMethod(string method) => Controls.ThemePalette.ForMethod(method);

    public static ImageSource LoadImage(byte[] data)
    {
        if (data == null || data.Length == 0) return null;
        try
        {
            var image = new BitmapImage();
            using var ms = new MemoryStream(data);
            image.BeginInit();
            image.CacheOption = BitmapCacheOption.OnLoad;
            image.StreamSource = ms;
            image.EndInit();
            image.Freeze();
            return image;
        }
        catch { return null; }
    }

    #endregion
}
