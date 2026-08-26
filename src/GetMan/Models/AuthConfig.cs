using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GetMan.Models;

public partial class AuthConfig : ObservableObject
{
    [ObservableProperty] private AuthType _type = AuthType.Inherit;

    // Bearer
    [ObservableProperty] private string _token = string.Empty;

    // Basic / NTLM / Digest
    [ObservableProperty] private string _username = string.Empty;
    [ObservableProperty] private string _password = string.Empty;
    [ObservableProperty] private string _domain = string.Empty;
    [ObservableProperty] private string _workstation = string.Empty;

    // API key
    [ObservableProperty] private string _apiKeyName = string.Empty;
    [ObservableProperty] private string _apiKeyValue = string.Empty;
    /// <summary>"header" or "query".</summary>
    [ObservableProperty] private string _apiKeyLocation = "header";

    // OAuth 2.0
    [ObservableProperty] private string _oauthGrantType = "client_credentials";
    [ObservableProperty] private string _oauthAccessTokenUrl = string.Empty;
    [ObservableProperty] private string _oauthAuthUrl = string.Empty;
    [ObservableProperty] private string _oauthClientId = string.Empty;
    [ObservableProperty] private string _oauthClientSecret = string.Empty;
    [ObservableProperty] private string _oauthScope = string.Empty;
    [ObservableProperty] private string _oauthAudience = string.Empty;
    [ObservableProperty] private string _oauthResource = string.Empty;
    [ObservableProperty] private string _oauthRedirectUri = "http://localhost:8899/callback";
    [ObservableProperty] private string _oauthUsername = string.Empty;
    [ObservableProperty] private string _oauthPassword = string.Empty;
    [ObservableProperty] private string _oauthAccessToken = string.Empty;
    [ObservableProperty] private string _oauthRefreshToken = string.Empty;
    [ObservableProperty] private string _oauthHeaderPrefix = "Bearer";
    /// <summary>"header" or "query" - where the token is attached.</summary>
    [ObservableProperty] private string _oauthAddTokenTo = "header";
    [ObservableProperty] private string _oauthClientAuth = "body";
    [ObservableProperty] private bool _oauthUsePkce = true;

    // Digest specifics
    [ObservableProperty] private string _digestRealm = string.Empty;
    [ObservableProperty] private string _digestNonce = string.Empty;
    [ObservableProperty] private string _digestAlgorithm = "MD5";
    [ObservableProperty] private string _digestQop = "auth";
    [ObservableProperty] private string _digestOpaque = string.Empty;

    // AWS Signature v4
    [ObservableProperty] private string _awsAccessKey = string.Empty;
    [ObservableProperty] private string _awsSecretKey = string.Empty;
    [ObservableProperty] private string _awsSessionToken = string.Empty;
    [ObservableProperty] private string _awsRegion = "us-east-1";
    [ObservableProperty] private string _awsService = "execute-api";

    // Hawk
    [ObservableProperty] private string _hawkAuthId = string.Empty;
    [ObservableProperty] private string _hawkAuthKey = string.Empty;
    [ObservableProperty] private string _hawkAlgorithm = "sha256";
    [ObservableProperty] private string _hawkExt = string.Empty;

    public AuthConfig Clone() => (AuthConfig)MemberwiseClone();
}
