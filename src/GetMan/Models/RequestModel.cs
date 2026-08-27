using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GetMan.Models;

public partial class RequestModel : ObservableObject
{
    [ObservableProperty] private string _method = "GET";
    [ObservableProperty] private string _url = string.Empty;

    /// <summary>Http, or a long-lived WebSocket or server-sent event stream.</summary>
    [ObservableProperty] private RequestProtocol _protocol = RequestProtocol.Http;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private string _preRequestScript = string.Empty;
    [ObservableProperty] private string _testScript = string.Empty;

    public ObservableCollection<KeyValueItem> QueryParams { get; set; } = new();
    public ObservableCollection<KeyValueItem> PathVariables { get; set; } = new();
    public ObservableCollection<KeyValueItem> Headers { get; set; } = new();
    public RequestBody Body { get; set; } = new();
    public AuthConfig Auth { get; set; } = new();
    public RequestSettings Settings { get; set; } = new();

    public RequestModel Clone()
    {
        var r = new RequestModel
        {
            Method = Method,
            Protocol = Protocol,
            Url = Url,
            Description = Description,
            PreRequestScript = PreRequestScript,
            TestScript = TestScript,
            Body = Body.Clone(),
            Auth = Auth.Clone(),
            Settings = Settings.Clone()
        };
        foreach (var p in QueryParams) r.QueryParams.Add(p.Clone());
        foreach (var p in PathVariables) r.PathVariables.Add(p.Clone());
        foreach (var h in Headers) r.Headers.Add(h.Clone());
        return r;
    }
}
