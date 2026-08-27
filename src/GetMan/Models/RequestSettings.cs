using CommunityToolkit.Mvvm.ComponentModel;

namespace GetMan.Models;

public partial class RequestSettings : ObservableObject
{
    [ObservableProperty] private bool _followRedirects = true;
    [ObservableProperty] private int _maxRedirects = 10;
    [ObservableProperty] private bool _verifySsl = true;
    [ObservableProperty] private int _timeoutMs = 0;           // 0 = infinite
    [ObservableProperty] private bool _sendCookies = true;
    [ObservableProperty] private bool _storeCookies = true;
    [ObservableProperty] private bool _encodeUrl = true;
    [ObservableProperty] private bool _keepHeaderCase;
    [ObservableProperty] private bool _useServerCipherSuite;
    [ObservableProperty] private string _httpVersion = "auto";  // auto | 1.1 | 2.0 | 3.0

    /// <summary>Comma separated WebSocket subprotocols offered during the handshake.</summary>
    [ObservableProperty] private string _wsSubprotocols = string.Empty;

    public RequestSettings Clone() => (RequestSettings)MemberwiseClone();
}
