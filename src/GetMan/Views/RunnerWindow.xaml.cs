using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Windows;
using CommunityToolkit.Mvvm.ComponentModel;
using GetMan.Models;
using GetMan.Services;
using GetMan.ViewModels;

namespace GetMan.Views;

public partial class RunnerWindow : Window
{
    private readonly MainViewModel _vm;
    private readonly CollectionNode _target;
    private readonly ObservableCollection<RunnableItem> _items = new();
    private readonly ObservableCollection<RunResultItem> _results = new();
    private List<Dictionary<string, string>> _data = new();
    private CancellationTokenSource _cts;
    private bool _running;

    public RunnerWindow(MainViewModel vm, CollectionNode target)
    {
        InitializeComponent();
        _vm = vm;
        _target = target;
        TargetName.Text = target.PathString();

        foreach (var node in target.Flatten().Where(n => n.Kind == NodeKind.Request))
            _items.Add(new RunnableItem { Node = node, Name = node.Name, Method = node.Request?.Method ?? "GET" });

        RequestList.ItemsSource = _items;
        Results.ItemsSource = _results;
    }

    /// <summary>
    /// Fills the result list with a finished run for the documentation screenshots, so the
    /// shot shows what a run looks like without going near the network.
    /// </summary>
    internal void SeedPreview()
    {
        foreach (var (name, method, status, time, size) in new[]
                 {
                     ("GET request", "GET", "200 OK", "214 ms", "486 B"),
                     ("POST json", "POST", "200 OK", "268 ms", "731 B")
                 })
        {
            var row = new RunResultItem
            {
                Name = name,
                Method = method,
                Url = "https://postman-echo.com/" + method.ToLowerInvariant(),
                StatusCode = 200,
                StatusText = status,
                TimeText = time,
                SizeText = size
            };
            row.Tests.Add(new TestResult { Name = "Status code is 200", Status = TestStatus.Pass, DurationMs = 1 });
            row.Tests.Add(new TestResult { Name = "Response is JSON", Status = TestStatus.Pass, DurationMs = 1 });
            _results.Add(row);
        }

        TotalText.Text = "2";
        PassedText.Text = "4";
        FailedText.Text = "0";
        TimeText.Text = "482 ms";
        Progress.Value = 100;
        RunStatus.Text = Loc.T("s.finished");
        RunStatus.Foreground = MainViewModel.Brush("Ok");
    }

    private void OnSelectAll(object sender, RoutedEventArgs e)
    {
        foreach (var i in _items) i.Selected = true;
    }

    private void OnSelectNone(object sender, RoutedEventArgs e)
    {
        foreach (var i in _items) i.Selected = false;
    }

    private void OnPickData(object sender, RoutedEventArgs e)
    {
        var dlg = new Microsoft.Win32.OpenFileDialog
        {
            Filter = "Data files (*.csv;*.json)|*.csv;*.json|All files (*.*)|*.*"
        };
        if (dlg.ShowDialog() != true) return;

        try
        {
            _data = dlg.FileName.EndsWith(".json", StringComparison.OrdinalIgnoreCase)
                ? ReadJsonData(dlg.FileName)
                : ReadCsvData(dlg.FileName);
            DataInfo.Text = Loc.T("s.data_rows", Path.GetFileName(dlg.FileName), _data.Count);
            if (_data.Count > 0) IterationsBox.Text = _data.Count.ToString();
        }
        catch (Exception ex)
        {
            DataInfo.Text = Loc.T("s.data_file_unreadable", ex.Message);
            _data = new List<Dictionary<string, string>>();
        }
    }

    private static List<Dictionary<string, string>> ReadJsonData(string path)
    {
        var rows = new List<Dictionary<string, string>>();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return rows;
        foreach (var el in doc.RootElement.EnumerateArray())
        {
            if (el.ValueKind != JsonValueKind.Object) continue;
            var row = new Dictionary<string, string>();
            foreach (var p in el.EnumerateObject())
                row[p.Name] = p.Value.ValueKind == JsonValueKind.String ? p.Value.GetString() : p.Value.ToString();
            rows.Add(row);
        }
        return rows;
    }

    private static List<Dictionary<string, string>> ReadCsvData(string path)
    {
        var rows = new List<Dictionary<string, string>>();
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2) return rows;

        var headers = SplitCsv(lines[0]);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var cells = SplitCsv(lines[i]);
            var row = new Dictionary<string, string>();
            for (int c = 0; c < headers.Count; c++)
                row[headers[c]] = c < cells.Count ? cells[c] : string.Empty;
            rows.Add(row);
        }
        return rows;
    }

    private static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false;
        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else quoted = false;
                }
                else current.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { cells.Add(current.ToString().Trim()); current.Clear(); }
            else current.Append(ch);
        }
        cells.Add(current.ToString().Trim());
        return cells;
    }

    private async void OnRun(object sender, RoutedEventArgs e)
    {
        if (_running)
        {
            _cts?.Cancel();
            return;
        }

        var selected = _items.Where(i => i.Selected).ToList();
        if (selected.Count == 0)
        {
            MessageDialog.Info(Loc.T("s.dlg_runner_empty_body"), Loc.T("s.dlg_nothing_to_run_title"), this);
            return;
        }

        if (!int.TryParse(IterationsBox.Text, out var iterations) || iterations < 1) iterations = 1;
        if (!int.TryParse(DelayBox.Text, out var delay) || delay < 0) delay = 0;

        _results.Clear();
        _running = true;
        RunButton.Content = Loc.T("s.stop");
        RunStatus.Text = Loc.T("s.running");
        RunStatus.Foreground = MainViewModel.Brush("Accent");
        _cts = new CancellationTokenSource();

        int passed = 0, failed = 0, total = 0;
        var clock = Stopwatch.StartNew();

        try
        {
            for (int iteration = 0; iteration < iterations && !_cts.IsCancellationRequested; iteration++)
            {
                int index = 0;
                while (index < selected.Count && !_cts.IsCancellationRequested)
                {
                    var item = selected[index];
                    var node = item.Node;
                    var vars = _vm.BuildResolver(node);

                    if (_data.Count > 0)
                    {
                        var row = _data[iteration % _data.Count];
                        foreach (var kv in row) vars.DataVars[kv.Key] = kv.Value;
                    }

                    var result = await _vm.Runner.ExecuteAsync(node, node.Request, vars, _vm.Settings,
                        iteration, iterations, _cts.Token);

                    if (KeepVariables.IsChecked == true)
                        _vm.ApplyVariableOps(result.VariableOps, node);

                    total++;
                    var row2 = new RunResultItem
                    {
                        Name = (iterations > 1 ? $"[{iteration + 1}] " : string.Empty) + node.Name,
                        Method = result.Request.Method,
                        Url = result.Request.Url,
                        StatusCode = result.Response?.StatusCode ?? 0,
                        StatusText = result.Response == null ? "-" :
                            result.Response.HasError ? Loc.T("s.error") : $"{result.Response.StatusCode} {result.Response.StatusText}",
                        TimeText = result.Response == null ? "-" : TextFormatter.HumanTime(result.Response.ElapsedMs),
                        SizeText = result.Response == null ? "-" : TextFormatter.HumanSize(result.Response.SizeBytes),
                        Error = result.Response?.Error ?? string.Empty
                    };
                    foreach (var t in result.Tests) row2.Tests.Add(t);
                    _results.Add(row2);

                    passed += result.Tests.Count(t => t.Status == TestStatus.Pass);
                    failed += result.Tests.Count(t => t.Status == TestStatus.Fail);

                    TotalText.Text = total.ToString();
                    PassedText.Text = passed.ToString();
                    FailedText.Text = failed.ToString();
                    TimeText.Text = TextFormatter.HumanTime(clock.Elapsed.TotalMilliseconds);
                    Progress.Value = 100.0 * (iteration * selected.Count + index + 1) / (iterations * selected.Count);
                    ResultScroller.ScrollToEnd();

                    if (StopOnFailure.IsChecked == true && result.Tests.Any(t => t.Status == TestStatus.Fail))
                    {
                        _cts.Cancel();
                        break;
                    }

                    // pm.execution.setNextRequest support
                    if (!string.IsNullOrEmpty(result.NextRequest))
                    {
                        var jump = selected.FindIndex(s => string.Equals(s.Name, result.NextRequest, StringComparison.OrdinalIgnoreCase));
                        if (jump >= 0) { index = jump; continue; }
                    }

                    index++;
                    if (delay > 0) await Task.Delay(delay, _cts.Token);
                }
            }

            RunStatus.Text = _cts.IsCancellationRequested ? Loc.T("s.stopped") : Loc.T("s.finished");
            RunStatus.Foreground = failed > 0 ? MainViewModel.Brush("Danger") : MainViewModel.Brush("Ok");
        }
        catch (OperationCanceledException)
        {
            RunStatus.Text = Loc.T("s.stopped");
            RunStatus.Foreground = MainViewModel.Brush("FgDim");
        }
        catch (Exception ex)
        {
            RunStatus.Text = Loc.T("s.error");
            RunStatus.Foreground = MainViewModel.Brush("Danger");
            MessageDialog.Error(ex.Message, Loc.T("s.dlg_runner_error_title"), this);
        }
        finally
        {
            clock.Stop();
            TimeText.Text = TextFormatter.HumanTime(clock.Elapsed.TotalMilliseconds);
            _running = false;
            RunButton.Content = Loc.T("s.run");
            _cts?.Dispose();
            _cts = null;
            _vm.SaveWorkspace();
        }
    }

    protected override void OnClosed(EventArgs e)
    {
        try { _cts?.Cancel(); } catch { }
        base.OnClosed(e);
    }
}

public partial class RunnableItem : ObservableObject
{
    [ObservableProperty] private bool _selected = true;
    public CollectionNode Node { get; set; }
    public string Name { get; set; }
    public string Method { get; set; }
}

public class RunResultItem
{
    public string Name { get; set; } = string.Empty;
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public string TimeText { get; set; } = string.Empty;
    public string SizeText { get; set; } = string.Empty;
    public string Error { get; set; } = string.Empty;
    public List<TestResult> Tests { get; } = new();
}
