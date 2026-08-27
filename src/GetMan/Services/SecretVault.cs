using System.Security.Cryptography;
using System.Text;
using System.Text.Json.Nodes;

namespace GetMan.Services;

/// <summary>
/// Encrypts the secret fields of the workspace with Windows DPAPI, scoped to the current user, so
/// the file on disk is not a list of tokens in plain text.
///
/// The work happens on the serialised tree rather than on the models: the app, the scripts and the
/// exporter all keep seeing plain values, and adding a secret field is one entry in
/// <see cref="SecretNames"/> rather than a new property type.
/// </summary>
public static class SecretVault
{
    /// <summary>
    /// Distinctive enough that no real password collides with it, and versioned so a future
    /// scheme can be told apart from this one.
    /// </summary>
    private const string Prefix = "getman:enc:v1:";

    /// <summary>Ties the ciphertext to this application, so another DPAPI consumer cannot read it.</summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GetMan.workspace.v1");

    /// <summary>
    /// Property names whose value is a credential. Matched by name wherever they appear, which
    /// covers requests, folders, collections and the copies inside history entries at once.
    /// </summary>
    private static readonly HashSet<string> SecretNames = new(StringComparer.Ordinal)
    {
        // AuthConfig
        "Token", "Password", "ApiKeyValue",
        "OauthClientSecret", "OauthPassword", "OauthAccessToken", "OauthRefreshToken",
        "AwsSecretKey", "AwsSessionToken", "HawkAuthKey",

        // AppSettings
        "ProxyPassword", "ClientCertPassword", "PostmanApiKey"
    };

    private static bool? _available;

    /// <summary>False off Windows, or wherever DPAPI refuses to work, in which case nothing is encrypted.</summary>
    public static bool Available
    {
        get
        {
            if (_available.HasValue) return _available.Value;

            // DPAPI is a Windows service with no equivalent the CLI can assume elsewhere. Rather
            // than invent a key of our own and imply a protection that is not there, the workspace
            // stays readable off Windows and SECURITY.md says so.
            if (!OperatingSystem.IsWindows()) return (_available = false).Value;

            try
            {
                var probe = Encoding.UTF8.GetBytes("probe");
                var sealed_ = ProtectedData.Protect(probe, Entropy, DataProtectionScope.CurrentUser);
                _available = ProtectedData.Unprotect(sealed_, Entropy, DataProtectionScope.CurrentUser)
                    .SequenceEqual(probe);
            }
            catch
            {
                _available = false;
            }
            return _available.Value;
        }
    }

    public static bool IsProtected(string value) => value != null && value.StartsWith(Prefix, StringComparison.Ordinal);

    public static string Protect(string plain)
    {
        // The IsWindows() test is redundant after Available, but the platform analyser cannot see
        // through a property and would flag the call below without it.
        if (string.IsNullOrEmpty(plain) || IsProtected(plain) || !Available || !OperatingSystem.IsWindows())
            return plain;
        try
        {
            var sealed_ = ProtectedData.Protect(Encoding.UTF8.GetBytes(plain), Entropy,
                DataProtectionScope.CurrentUser);
            return Prefix + System.Convert.ToBase64String(sealed_);
        }
        catch
        {
            // Better a readable workspace than a lost one.
            return plain;
        }
    }

    /// <summary>
    /// Returns the plain value, or the ciphertext untouched when it cannot be read - which happens
    /// when the file was written by another Windows user or on another machine. Blanking it there
    /// would look like the secret was simply gone; leaving the token visible says what happened and
    /// keeps a request from quietly authenticating as nobody.
    /// </summary>
    public static string Unprotect(string value)
    {
        if (!IsProtected(value)) return value;

        // A workspace encrypted on Windows cannot be read anywhere else. The ciphertext is handed
        // back rather than blanked, for the same reason as below.
        if (!OperatingSystem.IsWindows()) return value;

        try
        {
            var sealed_ = System.Convert.FromBase64String(value[Prefix.Length..]);
            return Encoding.UTF8.GetString(
                ProtectedData.Unprotect(sealed_, Entropy, DataProtectionScope.CurrentUser));
        }
        catch
        {
            return value;
        }
    }

    public static void ProtectTree(JsonNode node) => Walk(node, Protect);

    public static void UnprotectTree(JsonNode node) => Walk(node, Unprotect);

    private static void Walk(JsonNode node, Func<string, string> transform)
    {
        switch (node)
        {
            case JsonObject o:
                {
                    // A variable row carries its own flag rather than a telltale name, so the user
                    // decides which of their variables are secrets.
                    var secretRow = o.TryGetPropertyValue("Secret", out var flag) &&
                                    flag is JsonValue v && v.TryGetValue<bool>(out var isSecret) && isSecret;

                    foreach (var property in o.ToList())
                    {
                        if (property.Value is JsonValue value && value.TryGetValue<string>(out var text))
                        {
                            var isCredential = SecretNames.Contains(property.Key) ||
                                               (secretRow && property.Key is "Value" or "InitialValue");
                            if (isCredential && !string.IsNullOrEmpty(text))
                                o[property.Key] = transform(text);
                            continue;
                        }
                        Walk(property.Value, transform);
                    }
                    return;
                }

            case JsonArray a:
                foreach (var child in a) Walk(child, transform);
                return;
        }
    }
}
