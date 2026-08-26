using CommunityToolkit.Mvvm.ComponentModel;

namespace GetMan.Models;

public partial class AppSettings : ObservableObject
{
    [ObservableProperty] private bool _sslVerification = true;
    [ObservableProperty] private int _requestTimeoutMs = 0;
    [ObservableProperty] private int _maxResponseSizeMb = 50;
    [ObservableProperty] private bool _followRedirects = true;
    [ObservableProperty] private int _maxRedirects = 10;
    [ObservableProperty] private bool _sendNoCacheHeader;
    [ObservableProperty] private bool _sendPostmanTokenHeader = true;
    [ObservableProperty] private bool _autoSaveResponses = true;
    [ObservableProperty] private int _historyLimit = 500;
    [ObservableProperty] private string _theme = "Dark";

    /// <summary>Interface language: en, ru or uz. Empty means "follow Windows on first run".</summary>
    [ObservableProperty] private string _language = string.Empty;

    [ObservableProperty] private double _editorFontSize = 13;
    [ObservableProperty] private string _editorFontFamily = "Cascadia Mono, Consolas";
    [ObservableProperty] private bool _wordWrap = true;

    // Proxy
    [ObservableProperty] private bool _useSystemProxy = true;
    [ObservableProperty] private bool _useCustomProxy;
    [ObservableProperty] private string _proxyHost = string.Empty;
    [ObservableProperty] private int _proxyPort = 8080;
    [ObservableProperty] private bool _proxyAuth;
    [ObservableProperty] private string _proxyUsername = string.Empty;
    [ObservableProperty] private string _proxyPassword = string.Empty;
    [ObservableProperty] private string _proxyBypass = string.Empty;

    // Client certificate
    [ObservableProperty] private string _clientCertPath = string.Empty;
    [ObservableProperty] private string _clientCertPassword = string.Empty;

    [ObservableProperty] private string _activeEnvironmentId = string.Empty;
    [ObservableProperty] private int _scriptTimeoutMs = 5000;

    /// <summary>Personal API key used by the "Import from Postman account" flow.</summary>
    [ObservableProperty] private string _postmanApiKey = string.Empty;
}
