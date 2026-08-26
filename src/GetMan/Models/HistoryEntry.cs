using CommunityToolkit.Mvvm.ComponentModel;

namespace GetMan.Models;

public partial class HistoryEntry : ObservableObject
{
    [ObservableProperty] private string _id = Guid.NewGuid().ToString("N");
    [ObservableProperty] private DateTime _timestamp = DateTime.Now;
    [ObservableProperty] private string _method = "GET";
    [ObservableProperty] private string _url = string.Empty;
    [ObservableProperty] private int _statusCode;
    [ObservableProperty] private double _elapsedMs;
    [ObservableProperty] private long _sizeBytes;

    public RequestModel Request { get; set; }

    public string DisplayTime => Timestamp.ToString("HH:mm:ss");
}
