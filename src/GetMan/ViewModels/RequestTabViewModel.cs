using System.Collections.ObjectModel;
using System.IO;
using System.Windows.Media;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GetMan.Models;
using GetMan.Services;

namespace GetMan.ViewModels;

public partial class RequestTabViewModel : ObservableObject
{
    private readonly MainViewModel _main;
    private bool _syncingUrl;
    private CancellationTokenSource _cts;

    public RequestTabViewModel(MainViewModel main, CollectionNode node, RequestModel request, string title)
    {
        _main = main;
        Node = node;
        Request = request;
        _title = title;

        SyncParamsFromUrl();
        ChangeWatcher.Watch(Request, OnRequestChanged);
        Request.PropertyChanged += (_, e) =>
        {
            if (e.PropertyName == nameof(RequestModel.Url)) SyncParamsFromUrl();
            if (e.PropertyName == nameof(RequestModel.Method)) OnPropertyChanged(nameof(MethodBrush));
        };
        ChangeWatcher.WatchCollection(Request.QueryParams, SyncUrlFromParams);
        UpdateCounts();
    }

    public CollectionNode Node { get; set; }
    public RequestModel Request { get; }

    [ObservableProperty] private string _title = "Untitled Request";
    [ObservableProperty] private bool _isDirty;
    [ObservableProperty] private bool _isSending;

    [ObservableProperty] private ResponseModel _response;
    [ObservableProperty] private string _responseBody = string.Empty;
    [ObservableProperty] private string _responseRaw = string.Empty;
    [ObservableProperty] private string _responseLanguage = "text";
    [ObservableProperty] private string _responseView = "Pretty";
    [ObservableProperty] private ImageSource _responseImage;
    [ObservableProperty] private bool _hasResponse;
    [ObservableProperty] private string _errorText = string.Empty;

    [ObservableProperty] private int _activeSectionIndex;
    [ObservableProperty] private int _activeResponseIndex;

    [ObservableProperty] private int _paramCount;
    [ObservableProperty] private int _headerCount;
    [ObservableProperty] private int _passedCount;
    [ObservableProperty] private int _failedCount;

    public ObservableCollection<TestResult> TestResults { get; } = new();
    public ObservableCollection<ConsoleEntry> ConsoleEntries { get; } = new();
    public ObservableCollection<KeyValuePair<string, string>> ResponseHeaders { get; } = new();
    public ObservableCollection<ResponseCookie> ResponseCookies { get; } = new();

    public string[] Methods { get; } = { "GET", "POST", "PUT", "PATCH", "DELETE", "HEAD", "OPTIONS", "TRACE", "LINK", "UNLINK", "PURGE", "LOCK", "UNLOCK", "PROPFIND", "VIEW" };
    public string[] BodyLanguages { get; } = { "json", "xml", "html", "javascript", "text" };

    public Brush MethodBrush => MainViewModel.BrushForMethod(Request.Method);

    public string StatusLine
    {
        get
        {
            if (Response == null) return string.Empty;
            if (Response.HasError) return "Error";
            return $"{Response.StatusCode} {Response.StatusText}";
        }
    }

    public Brush StatusBrush
    {
        get
        {
            if (Response == null || Response.HasError) return MainViewModel.Brush("Danger");
            return Response.StatusCode switch
            {
                >= 200 and < 300 => MainViewModel.Brush("Ok"),
                >= 300 and < 400 => MainViewModel.Brush("Info"),
                >= 400 and < 500 => MainViewModel.Brush("Warn"),
                >= 500 => MainViewModel.Brush("Danger"),
                _ => MainViewModel.Brush("FgDim")
            };
        }
    }

    public string TimeLine => Response == null ? string.Empty : TextFormatter.HumanTime(Response.ElapsedMs);
    public string SizeLine => Response == null ? string.Empty : TextFormatter.HumanSize(Response.SizeBytes);
    public string TestSummary => TestResults.Count == 0 ? string.Empty : $"{PassedCount}/{TestResults.Count}";

    private void OnRequestChanged()
    {
        IsDirty = true;
        UpdateCounts();
    }

    private void UpdateCounts()
    {
        ParamCount = Request.QueryParams.Count(p => p.Enabled && !string.IsNullOrWhiteSpace(p.Key));
        HeaderCount = Request.Headers.Count(h => h.Enabled && !string.IsNullOrWhiteSpace(h.Key));
    }

    #region url <-> params sync

    private void SyncParamsFromUrl()
    {
        if (_syncingUrl) return;
        _syncingUrl = true;
        try
        {
            var query = UrlUtil.SplitQuery(Request.Url ?? string.Empty);
            var parsed = UrlUtil.ParseQuery(query);

            // Preserve enabled/disabled flags and descriptions for rows that survive.
            var disabled = Request.QueryParams.Where(p => !p.Enabled).Select(p => p.Clone()).ToList();

            Request.QueryParams.Clear();
            foreach (var p in parsed) Request.QueryParams.Add(p);
            foreach (var d in disabled)
                if (!Request.QueryParams.Any(p => p.Key == d.Key)) Request.QueryParams.Add(d);

            SyncPathVars();
        }
        finally { _syncingUrl = false; }
    }

    private void SyncUrlFromParams()
    {
        if (_syncingUrl) return;
        _syncingUrl = true;
        try
        {
            var basePart = UrlUtil.SplitBase(Request.Url ?? string.Empty);
            var query = UrlUtil.BuildQuery(Request.QueryParams, s => s, false);
            Request.Url = UrlUtil.ComposeUrl(basePart, query);
            SyncPathVars();
        }
        finally { _syncingUrl = false; }
    }

    private void SyncPathVars()
    {
        var names = UrlUtil.ExtractPathVariableNames(Request.Url ?? string.Empty).Distinct().ToList();
        for (int i = Request.PathVariables.Count - 1; i >= 0; i--)
            if (!names.Contains(Request.PathVariables[i].Key)) Request.PathVariables.RemoveAt(i);
        foreach (var n in names)
            if (!Request.PathVariables.Any(p => p.Key == n))
                Request.PathVariables.Add(new KeyValueItem(n, string.Empty));
    }

    #endregion

    #region sending

    [RelayCommand]
    private async Task SendAsync()
    {
        if (IsSending) { Cancel(); return; }

        _cts = new CancellationTokenSource();
        IsSending = true;
        ErrorText = string.Empty;
        TestResults.Clear();
        ConsoleEntries.Clear();

        try
        {
            var result = await _main.ExecuteAsync(Node, Request, _cts.Token);
            ApplyResult(result);
        }
        catch (Exception ex)
        {
            ErrorText = ex.Message;
        }
        finally
        {
            IsSending = false;
            _cts?.Dispose();
            _cts = null;
        }
    }

    public void Cancel()
    {
        try { _cts?.Cancel(); } catch { }
    }

    internal void ApplyResult(ExecutionResult result)
    {
        foreach (var c in result.Console) ConsoleEntries.Add(c);
        foreach (var t in result.Tests) TestResults.Add(t);
        PassedCount = TestResults.Count(t => t.Status == TestStatus.Pass);
        FailedCount = TestResults.Count(t => t.Status == TestStatus.Fail);

        var response = result.Response;
        Response = response;
        HasResponse = response != null;
        if (response == null) return;

        ErrorText = response.Error ?? string.Empty;

        ResponseHeaders.Clear();
        foreach (var h in response.Headers) ResponseHeaders.Add(h);

        ResponseCookies.Clear();
        foreach (var c in response.Cookies) ResponseCookies.Add(c);

        ResponseRaw = response.BodyText ?? string.Empty;
        ResponseLanguage = TextFormatter.DetectLanguage(response.ContentType, ResponseRaw);
        ResponseBody = ResponseView == "Raw" ? ResponseRaw : TextFormatter.Pretty(ResponseRaw, ResponseLanguage);
        ResponseImage = MimeTypes.IsImage(response.ContentType) ? MainViewModel.LoadImage(response.RawBody) : null;

        OnPropertyChanged(nameof(StatusLine));
        OnPropertyChanged(nameof(StatusBrush));
        OnPropertyChanged(nameof(TimeLine));
        OnPropertyChanged(nameof(SizeLine));
        OnPropertyChanged(nameof(TestSummary));
    }

    partial void OnResponseViewChanged(string value)
    {
        if (Response == null) return;
        ResponseBody = value == "Raw" ? ResponseRaw : TextFormatter.Pretty(ResponseRaw, ResponseLanguage);
    }

    #endregion

    [RelayCommand]
    private void BeautifyBody()
    {
        if (Request.Body.Mode != BodyMode.Raw) return;
        Request.Body.Raw = TextFormatter.Pretty(Request.Body.Raw, Request.Body.RawLanguage);
    }

    [RelayCommand]
    private void SaveResponse()
    {
        if (Response == null) return;
        var dlg = new Microsoft.Win32.SaveFileDialog
        {
            FileName = "response" + MimeTypes.ExtensionFor(Response.ContentType),
            Filter = "All files|*.*"
        };
        if (dlg.ShowDialog() == true)
            File.WriteAllBytes(dlg.FileName, Response.RawBody ?? Array.Empty<byte>());
    }

    [RelayCommand]
    private void CopyResponse()
    {
        try { System.Windows.Clipboard.SetText(ResponseBody ?? string.Empty); } catch { }
    }

    [RelayCommand]
    private void ClearResponse()
    {
        Response = null;
        HasResponse = false;
        ResponseBody = string.Empty;
        ResponseRaw = string.Empty;
        ResponseHeaders.Clear();
        ResponseCookies.Clear();
        TestResults.Clear();
        ConsoleEntries.Clear();
        ErrorText = string.Empty;
    }
}
