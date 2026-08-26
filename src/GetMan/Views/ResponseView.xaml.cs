using System.ComponentModel;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using GetMan.Models;
using GetMan.Services;
using GetMan.ViewModels;

namespace GetMan.Views;

public partial class ResponseView : UserControl
{
    private RequestTabViewModel _vm;

    public ResponseView()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
    }

    private void OnDataContextChanged(object sender, DependencyPropertyChangedEventArgs e)
    {
        if (_vm != null) _vm.PropertyChanged -= OnVmPropertyChanged;
        _vm = DataContext as RequestTabViewModel;
        if (_vm != null) _vm.PropertyChanged += OnVmPropertyChanged;
        UpdateBodyHost();
    }

    private void OnVmPropertyChanged(object sender, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(RequestTabViewModel.ResponseView)
            or nameof(RequestTabViewModel.Response)
            or nameof(RequestTabViewModel.ResponseImage)
            or nameof(RequestTabViewModel.HasResponse))
        {
            Dispatcher.BeginInvoke(new Action(UpdateBodyHost));
        }

        if (e.PropertyName == nameof(RequestTabViewModel.Response) && _vm?.Response != null)
            Dispatcher.BeginInvoke(new Action(PlayArrival));
    }

    /// <summary>Rises the response pane into view so a new result reads as new.</summary>
    private void PlayArrival()
    {
        if (TryFindResource("ResponseArrived") is Storyboard storyboard)
            storyboard.Begin(this);
        BuildTimingBar();
    }

    private static readonly (string Label, string Token, Func<TimingInfo, double> Value)[] TimingSegments =
    {
        ("DNS", "TimingDns", t => t.DnsMs),
        ("TCP", "TimingConnect", t => t.ConnectMs),
        ("TLS", "TimingTls", t => t.TlsMs),
        ("Waiting", "TimingWait", t => Math.Max(0, t.FirstByteMs - (t.DnsMs + t.ConnectMs + t.TlsMs))),
        ("Download", "TimingDownload", t => t.DownloadMs)
    };

    /// <summary>
    /// Lays the timing breakdown out as a stacked bar. Column widths are star sized from the
    /// measured milliseconds, so the bar stays proportional at any pane width.
    /// </summary>
    private void BuildTimingBar()
    {
        TimingBar.ColumnDefinitions.Clear();
        TimingBar.Children.Clear();
        TimingLegend.Children.Clear();

        var timing = _vm?.Response?.Timing;
        if (timing == null) return;

        var values = TimingSegments.Select(s => (s.Label, s.Token, Ms: Math.Max(0, s.Value(timing)))).ToList();
        var total = values.Sum(v => v.Ms);
        if (total <= 0) return;

        int column = 0;
        foreach (var (label, token, ms) in values)
        {
            if (ms <= 0) continue;

            TimingBar.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(ms, GridUnitType.Star) });

            var brush = TryFindResource(token) as Brush ?? Brushes.Gray;
            var segment = new Border
            {
                Background = brush,
                ToolTip = $"{label}: {TextFormatter.HumanTime(ms)} ({ms / total:P0})"
            };
            Grid.SetColumn(segment, column++);
            TimingBar.Children.Add(segment);

            TimingLegend.Children.Add(BuildLegendEntry(label, brush, ms, total));
        }
    }

    private UIElement BuildLegendEntry(string label, Brush brush, double ms, double total)
    {
        var row = new StackPanel { Orientation = Orientation.Horizontal, Margin = new Thickness(0, 0, 20, 6) };
        row.Children.Add(new Border
        {
            Width = 10,
            Height = 10,
            CornerRadius = new CornerRadius(3),
            Background = brush,
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(new TextBlock
        {
            Text = $"{label}  {TextFormatter.HumanTime(ms)}",
            Margin = new Thickness(7, 0, 0, 0),
            FontSize = 11.5,
            Foreground = TryFindResource("FgDim") as Brush,
            VerticalAlignment = VerticalAlignment.Center
        });
        row.Children.Add(new TextBlock
        {
            Text = $"({ms / total:P0})",
            Margin = new Thickness(5, 0, 0, 0),
            FontSize = 11,
            Foreground = TryFindResource("FgMuted") as Brush,
            VerticalAlignment = VerticalAlignment.Center
        });
        return row;
    }

    private void UpdateBodyHost()
    {
        if (_vm?.Response == null)
        {
            BodyEditor.Visibility = Visibility.Visible;
            ImageHost.Visibility = Visibility.Collapsed;
            PreviewHost.Visibility = Visibility.Collapsed;
            return;
        }

        var isImage = MimeTypes.IsImage(_vm.Response.ContentType) && _vm.ResponseImage != null;
        var wantsPreview = _vm.ResponseView == "Preview";

        if (isImage)
        {
            BodyEditor.Visibility = Visibility.Collapsed;
            PreviewHost.Visibility = Visibility.Collapsed;
            ImageHost.Visibility = Visibility.Visible;
            return;
        }

        ImageHost.Visibility = Visibility.Collapsed;

        if (wantsPreview)
        {
            BodyEditor.Visibility = Visibility.Collapsed;
            PreviewHost.Visibility = Visibility.Visible;
            try
            {
                var html = _vm.ResponseRaw ?? string.Empty;
                if (_vm.ResponseLanguage != "html")
                    html = "<html><body style='font-family:Consolas,monospace;white-space:pre-wrap;padding:12px'>"
                           + System.Net.WebUtility.HtmlEncode(html) + "</body></html>";
                Browser.NavigateToString(string.IsNullOrWhiteSpace(html) ? "<html><body></body></html>" : html);
            }
            catch
            {
                PreviewHost.Visibility = Visibility.Collapsed;
                BodyEditor.Visibility = Visibility.Visible;
            }
            return;
        }

        PreviewHost.Visibility = Visibility.Collapsed;
        BodyEditor.Visibility = Visibility.Visible;
    }
}
