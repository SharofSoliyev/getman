using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GetMan.Models;

public class ResponseCookie
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Domain { get; set; } = string.Empty;
    public string Path { get; set; } = "/";
    public string Expires { get; set; } = string.Empty;
    public bool HttpOnly { get; set; }
    public bool Secure { get; set; }
    public string SameSite { get; set; } = string.Empty;
}

public class TimingInfo
{
    public double DnsMs { get; set; }
    public double ConnectMs { get; set; }
    public double TlsMs { get; set; }
    public double RequestSentMs { get; set; }
    public double FirstByteMs { get; set; }
    public double DownloadMs { get; set; }
    public double TotalMs { get; set; }
}

public partial class ResponseModel : ObservableObject
{
    [ObservableProperty] private int _statusCode;
    [ObservableProperty] private string _statusText = string.Empty;
    [ObservableProperty] private double _elapsedMs;
    [ObservableProperty] private long _sizeBytes;
    [ObservableProperty] private long _headerBytes;
    [ObservableProperty] private long _bodyBytes;
    [ObservableProperty] private string _bodyText = string.Empty;
    [ObservableProperty] private string _contentType = string.Empty;
    [ObservableProperty] private string _error = string.Empty;
    [ObservableProperty] private string _httpVersion = string.Empty;
    [ObservableProperty] private string _finalUrl = string.Empty;

    public byte[] RawBody { get; set; } = Array.Empty<byte>();
    public List<KeyValuePair<string, string>> Headers { get; set; } = new();
    public List<ResponseCookie> Cookies { get; set; } = new();
    public TimingInfo Timing { get; set; } = new();

    /// <summary>Exact wire request that was sent (after variable resolution) - shown in the console.</summary>
    public string RequestPreview { get; set; } = string.Empty;

    [JsonIgnore] public bool IsSuccess => StatusCode >= 200 && StatusCode < 300;
    [JsonIgnore] public bool HasError => !string.IsNullOrEmpty(Error);
}

public class TestResult
{
    public string Name { get; set; } = string.Empty;
    public TestStatus Status { get; set; }
    public string Message { get; set; } = string.Empty;
    public double DurationMs { get; set; }
}

public class ConsoleEntry
{
    public DateTime Time { get; set; } = DateTime.Now;
    public string Level { get; set; } = "log";
    public string Message { get; set; } = string.Empty;
    public string Source { get; set; } = string.Empty;
}
