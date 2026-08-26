using System.Windows.Controls;
using GetMan.Models;

namespace GetMan.Views;

public partial class AuthView : UserControl
{
    public AuthType[] AuthTypes { get; } =
    {
        AuthType.Inherit, AuthType.None, AuthType.Bearer, AuthType.Basic, AuthType.ApiKey,
        AuthType.OAuth2, AuthType.Digest, AuthType.NTLM, AuthType.AwsV4, AuthType.Hawk
    };

    public string[] GrantTypes { get; } = { "client_credentials", "authorization_code", "password", "refresh_token" };
    public string[] DigestAlgorithms { get; } = { "MD5", "MD5-sess", "SHA-256", "SHA-256-sess" };
    public string[] HawkAlgorithms { get; } = { "sha256", "sha1" };

    public AuthView() => InitializeComponent();
}
