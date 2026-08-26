using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using GetMan.Models;

namespace GetMan.Services;

/// <summary>Applies every supported authorization scheme onto a prepared request.</summary>
public static class AuthApplier
{
    public static void Apply(PreparedRequest req, AuthConfig auth, Action<string> log = null)
    {
        if (auth == null || auth.Type == AuthType.None || auth.Type == AuthType.Inherit) return;

        switch (auth.Type)
        {
            case AuthType.Bearer:
                if (!string.IsNullOrWhiteSpace(auth.Token))
                    req.SetHeader("Authorization", "Bearer " + auth.Token.Trim());
                break;

            case AuthType.Basic:
                {
                    var raw = Encoding.UTF8.GetBytes($"{auth.Username}:{auth.Password}");
                    req.SetHeader("Authorization", "Basic " + Convert.ToBase64String(raw));
                    break;
                }

            case AuthType.ApiKey:
                if (!string.IsNullOrWhiteSpace(auth.ApiKeyName))
                {
                    if (string.Equals(auth.ApiKeyLocation, "query", StringComparison.OrdinalIgnoreCase))
                    {
                        var sep = req.Url.Contains('?') ? "&" : "?";
                        req.Url += sep + UrlUtil.EncodeComponent(auth.ApiKeyName) + "=" + UrlUtil.EncodeComponent(auth.ApiKeyValue ?? string.Empty);
                    }
                    else
                    {
                        req.SetHeader(auth.ApiKeyName, auth.ApiKeyValue ?? string.Empty);
                    }
                }
                break;

            case AuthType.OAuth2:
                {
                    var token = auth.OauthAccessToken ?? string.Empty;
                    if (string.IsNullOrWhiteSpace(token)) break;
                    if (string.Equals(auth.OauthAddTokenTo, "query", StringComparison.OrdinalIgnoreCase))
                    {
                        var sep = req.Url.Contains('?') ? "&" : "?";
                        req.Url += sep + "access_token=" + UrlUtil.EncodeComponent(token);
                    }
                    else
                    {
                        var prefix = string.IsNullOrWhiteSpace(auth.OauthHeaderPrefix) ? "Bearer" : auth.OauthHeaderPrefix.Trim();
                        req.SetHeader("Authorization", (prefix + " " + token).Trim());
                    }
                    break;
                }

            case AuthType.AwsV4:
                SignAwsV4(req, auth);
                break;

            case AuthType.Hawk:
                SignHawk(req, auth);
                break;

            // Digest is challenge driven and NTLM is handled by the socket handler.
            case AuthType.Digest:
            case AuthType.NTLM:
                break;
        }
    }

    #region Digest

    public static string BuildDigestHeader(AuthConfig auth, string challenge, string method, Uri uri, byte[] body)
    {
        var p = ParseChallenge(challenge);
        string realm = Get(p, "realm");
        string nonce = Get(p, "nonce");
        string qop = Get(p, "qop");
        string opaque = Get(p, "opaque");
        string algorithm = string.IsNullOrEmpty(Get(p, "algorithm")) ? "MD5" : Get(p, "algorithm");
        string cnonce = Guid.NewGuid().ToString("N").Substring(0, 16);
        string nc = "00000001";
        string digestUri = uri.PathAndQuery;

        if (qop.Contains(','))
            qop = qop.Split(',').Select(s => s.Trim()).FirstOrDefault(s => s == "auth") ?? qop.Split(',')[0].Trim();

        Func<string, string> H = algorithm.StartsWith("SHA-256", StringComparison.OrdinalIgnoreCase)
            ? (s => ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(s))))
            : (s => ToHex(MD5.HashData(Encoding.UTF8.GetBytes(s))));

        string ha1 = H($"{auth.Username}:{realm}:{auth.Password}");
        if (algorithm.EndsWith("-sess", StringComparison.OrdinalIgnoreCase))
            ha1 = H($"{ha1}:{nonce}:{cnonce}");

        string ha2 = qop == "auth-int"
            ? H($"{method}:{digestUri}:{H(Encoding.UTF8.GetString(body ?? Array.Empty<byte>()))}")
            : H($"{method}:{digestUri}");

        string response = string.IsNullOrEmpty(qop)
            ? H($"{ha1}:{nonce}:{ha2}")
            : H($"{ha1}:{nonce}:{nc}:{cnonce}:{qop}:{ha2}");

        var sb = new StringBuilder("Digest ");
        sb.Append($"username=\"{auth.Username}\", realm=\"{realm}\", nonce=\"{nonce}\", uri=\"{digestUri}\", response=\"{response}\"");
        if (!string.IsNullOrEmpty(opaque)) sb.Append($", opaque=\"{opaque}\"");
        if (!string.IsNullOrEmpty(algorithm)) sb.Append($", algorithm={algorithm}");
        if (!string.IsNullOrEmpty(qop)) sb.Append($", qop={qop}, nc={nc}, cnonce=\"{cnonce}\"");
        return sb.ToString();
    }

    private static string Get(Dictionary<string, string> d, string k) => d.TryGetValue(k, out var v) ? v : string.Empty;

    private static Dictionary<string, string> ParseChallenge(string challenge)
    {
        var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (string.IsNullOrEmpty(challenge)) return d;
        var idx = challenge.IndexOf(' ');
        var rest = idx > 0 ? challenge.Substring(idx + 1) : challenge;

        int i = 0;
        while (i < rest.Length)
        {
            while (i < rest.Length && (rest[i] == ' ' || rest[i] == ',')) i++;
            int keyStart = i;
            while (i < rest.Length && rest[i] != '=') i++;
            if (i >= rest.Length) break;
            var key = rest.Substring(keyStart, i - keyStart).Trim();
            i++; // skip =
            string val;
            if (i < rest.Length && rest[i] == '"')
            {
                i++;
                int vs = i;
                while (i < rest.Length && rest[i] != '"') i++;
                val = rest.Substring(vs, i - vs);
                i++;
            }
            else
            {
                int vs = i;
                while (i < rest.Length && rest[i] != ',') i++;
                val = rest.Substring(vs, i - vs).Trim();
            }
            d[key] = val;
        }
        return d;
    }

    #endregion

    #region AWS Signature V4

    private static void SignAwsV4(PreparedRequest req, AuthConfig auth)
    {
        if (string.IsNullOrWhiteSpace(auth.AwsAccessKey) || string.IsNullOrWhiteSpace(auth.AwsSecretKey)) return;
        if (!Uri.TryCreate(UrlUtil.EnsureScheme(req.Url), UriKind.Absolute, out var uri)) return;

        var now = DateTime.UtcNow;
        var amzDate = now.ToString("yyyyMMddTHHmmssZ", CultureInfo.InvariantCulture);
        var dateStamp = now.ToString("yyyyMMdd", CultureInfo.InvariantCulture);
        var region = string.IsNullOrWhiteSpace(auth.AwsRegion) ? "us-east-1" : auth.AwsRegion;
        var service = string.IsNullOrWhiteSpace(auth.AwsService) ? "execute-api" : auth.AwsService;

        var payload = req.MaterializeBody();
        var payloadHash = ToHex(SHA256.HashData(payload));

        req.SetHeader("X-Amz-Date", amzDate);
        req.SetHeader("X-Amz-Content-Sha256", payloadHash);
        if (!string.IsNullOrWhiteSpace(auth.AwsSessionToken))
            req.SetHeader("X-Amz-Security-Token", auth.AwsSessionToken);

        var signedHeaderPairs = req.Headers
            .Where(h => !string.Equals(h.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
            .Select(h => new KeyValuePair<string, string>(h.Key.ToLowerInvariant(), (h.Value ?? string.Empty).Trim()))
            .ToList();
        signedHeaderPairs.Add(new KeyValuePair<string, string>("host", uri.Host + (uri.IsDefaultPort ? "" : ":" + uri.Port)));
        signedHeaderPairs = signedHeaderPairs
            .GroupBy(h => h.Key)
            .Select(g => new KeyValuePair<string, string>(g.Key, string.Join(",", g.Select(x => x.Value))))
            .OrderBy(h => h.Key, StringComparer.Ordinal)
            .ToList();

        var canonicalHeaders = string.Concat(signedHeaderPairs.Select(h => h.Key + ":" + h.Value + "\n"));
        var signedHeaders = string.Join(";", signedHeaderPairs.Select(h => h.Key));

        var canonicalQuery = string.Join("&", UrlUtil.ParseQuery(uri.Query.TrimStart('?'))
            .Select(kv => new { K = UrlUtil.EncodeComponent(kv.Key), V = UrlUtil.EncodeComponent(kv.Value) })
            .OrderBy(x => x.K, StringComparer.Ordinal).ThenBy(x => x.V, StringComparer.Ordinal)
            .Select(x => x.K + "=" + x.V));

        var canonicalRequest = string.Join("\n",
            req.Method.ToUpperInvariant(),
            string.IsNullOrEmpty(uri.AbsolutePath) ? "/" : uri.AbsolutePath,
            canonicalQuery,
            canonicalHeaders,
            signedHeaders,
            payloadHash);

        var scope = $"{dateStamp}/{region}/{service}/aws4_request";
        var stringToSign = string.Join("\n", "AWS4-HMAC-SHA256", amzDate, scope, ToHex(SHA256.HashData(Encoding.UTF8.GetBytes(canonicalRequest))));

        var kDate = HmacSha256(Encoding.UTF8.GetBytes("AWS4" + auth.AwsSecretKey), dateStamp);
        var kRegion = HmacSha256(kDate, region);
        var kService = HmacSha256(kRegion, service);
        var kSigning = HmacSha256(kService, "aws4_request");
        var signature = ToHex(HmacSha256(kSigning, stringToSign));

        req.SetHeader("Authorization",
            $"AWS4-HMAC-SHA256 Credential={auth.AwsAccessKey}/{scope}, SignedHeaders={signedHeaders}, Signature={signature}");
    }

    private static byte[] HmacSha256(byte[] key, string data)
    {
        using var h = new HMACSHA256(key);
        return h.ComputeHash(Encoding.UTF8.GetBytes(data));
    }

    #endregion

    #region Hawk

    private static void SignHawk(PreparedRequest req, AuthConfig auth)
    {
        if (string.IsNullOrWhiteSpace(auth.HawkAuthId)) return;
        if (!Uri.TryCreate(UrlUtil.EnsureScheme(req.Url), UriKind.Absolute, out var uri)) return;

        var ts = DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
        var nonce = Guid.NewGuid().ToString("N").Substring(0, 6);
        var payload = string.Join("\n", "hawk.1.header", ts, nonce, req.Method.ToUpperInvariant(),
            uri.PathAndQuery, uri.Host.ToLowerInvariant(), uri.Port.ToString(CultureInfo.InvariantCulture),
            string.Empty, auth.HawkExt ?? string.Empty) + "\n";

        byte[] mac;
        var key = Encoding.UTF8.GetBytes(auth.HawkAuthKey ?? string.Empty);
        if (string.Equals(auth.HawkAlgorithm, "sha1", StringComparison.OrdinalIgnoreCase))
        {
            using var h = new HMACSHA1(key);
            mac = h.ComputeHash(Encoding.UTF8.GetBytes(payload));
        }
        else
        {
            using var h = new HMACSHA256(key);
            mac = h.ComputeHash(Encoding.UTF8.GetBytes(payload));
        }

        var sb = new StringBuilder($"Hawk id=\"{auth.HawkAuthId}\", ts=\"{ts}\", nonce=\"{nonce}\"");
        if (!string.IsNullOrEmpty(auth.HawkExt)) sb.Append($", ext=\"{auth.HawkExt}\"");
        sb.Append($", mac=\"{Convert.ToBase64String(mac)}\"");
        req.SetHeader("Authorization", sb.ToString());
    }

    #endregion

    #region OAuth 2.0

    public static async Task<(string AccessToken, string RefreshToken, string Raw)> FetchOAuthTokenAsync(
        AuthConfig auth, HttpClient client, Action<string> log, CancellationToken ct)
    {
        var grant = (auth.OauthGrantType ?? "client_credentials").ToLowerInvariant();
        var form = new Dictionary<string, string>();

        if (grant == "authorization_code")
        {
            var code = await RunAuthorizationCodeFlowAsync(auth, log, ct).ConfigureAwait(false);
            if (string.IsNullOrEmpty(code.Code)) return (null, null, "authorization cancelled");
            form["grant_type"] = "authorization_code";
            form["code"] = code.Code;
            form["redirect_uri"] = auth.OauthRedirectUri;
            if (auth.OauthUsePkce && !string.IsNullOrEmpty(code.Verifier)) form["code_verifier"] = code.Verifier;
        }
        else if (grant == "password")
        {
            form["grant_type"] = "password";
            form["username"] = auth.OauthUsername ?? string.Empty;
            form["password"] = auth.OauthPassword ?? string.Empty;
        }
        else if (grant == "refresh_token")
        {
            form["grant_type"] = "refresh_token";
            form["refresh_token"] = auth.OauthRefreshToken ?? string.Empty;
        }
        else
        {
            form["grant_type"] = "client_credentials";
        }

        if (!string.IsNullOrWhiteSpace(auth.OauthScope)) form["scope"] = auth.OauthScope;
        if (!string.IsNullOrWhiteSpace(auth.OauthAudience)) form["audience"] = auth.OauthAudience;
        if (!string.IsNullOrWhiteSpace(auth.OauthResource)) form["resource"] = auth.OauthResource;

        var msg = new HttpRequestMessage(HttpMethod.Post, auth.OauthAccessTokenUrl);
        if (string.Equals(auth.OauthClientAuth, "header", StringComparison.OrdinalIgnoreCase))
        {
            var basic = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{auth.OauthClientId}:{auth.OauthClientSecret}"));
            msg.Headers.TryAddWithoutValidation("Authorization", "Basic " + basic);
        }
        else
        {
            form["client_id"] = auth.OauthClientId ?? string.Empty;
            if (!string.IsNullOrEmpty(auth.OauthClientSecret)) form["client_secret"] = auth.OauthClientSecret;
        }

        msg.Content = new FormUrlEncodedContent(form);
        msg.Headers.TryAddWithoutValidation("Accept", "application/json");

        log?.Invoke($"OAuth2: POST {auth.OauthAccessTokenUrl} ({grant})");
        using var resp = await client.SendAsync(msg, ct).ConfigureAwait(false);
        var text = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
        {
            log?.Invoke($"OAuth2 failed: {(int)resp.StatusCode} {text}");
            return (null, null, text);
        }

        try
        {
            using var doc = JsonDocument.Parse(text);
            var at = doc.RootElement.TryGetProperty("access_token", out var a) ? a.GetString() : null;
            var rt = doc.RootElement.TryGetProperty("refresh_token", out var r) ? r.GetString() : null;
            return (at, rt, text);
        }
        catch
        {
            return (null, null, text);
        }
    }

    private static async Task<(string Code, string Verifier)> RunAuthorizationCodeFlowAsync(
        AuthConfig auth, Action<string> log, CancellationToken ct)
    {
        string verifier = null, challenge = null;
        if (auth.OauthUsePkce)
        {
            var bytes = RandomNumberGenerator.GetBytes(32);
            verifier = Base64Url(bytes);
            challenge = Base64Url(SHA256.HashData(Encoding.ASCII.GetBytes(verifier)));
        }

        var state = Guid.NewGuid().ToString("N");
        var redirect = string.IsNullOrWhiteSpace(auth.OauthRedirectUri) ? "http://localhost:8899/callback" : auth.OauthRedirectUri;
        if (!redirect.EndsWith("/", StringComparison.Ordinal)) redirect += "/";

        var url = new StringBuilder(auth.OauthAuthUrl);
        url.Append(auth.OauthAuthUrl.Contains('?') ? '&' : '?');
        url.Append("response_type=code");
        url.Append("&client_id=").Append(UrlUtil.EncodeComponent(auth.OauthClientId ?? string.Empty));
        url.Append("&redirect_uri=").Append(UrlUtil.EncodeComponent(redirect.TrimEnd('/')));
        url.Append("&state=").Append(state);
        if (!string.IsNullOrWhiteSpace(auth.OauthScope)) url.Append("&scope=").Append(UrlUtil.EncodeComponent(auth.OauthScope));
        if (!string.IsNullOrWhiteSpace(auth.OauthAudience)) url.Append("&audience=").Append(UrlUtil.EncodeComponent(auth.OauthAudience));
        if (challenge != null) url.Append("&code_challenge=").Append(challenge).Append("&code_challenge_method=S256");

        using var listener = new HttpListener();
        listener.Prefixes.Add(redirect);
        try { listener.Start(); }
        catch (Exception ex)
        {
            log?.Invoke("OAuth2 listener failed: " + ex.Message);
            return (null, null);
        }

        log?.Invoke("OAuth2: opening browser for authorization...");
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo(url.ToString()) { UseShellExecute = true });
        }
        catch (Exception ex)
        {
            log?.Invoke("Could not launch browser: " + ex.Message);
        }

        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeout.CancelAfter(TimeSpan.FromMinutes(5));

        try
        {
            var contextTask = listener.GetContextAsync();
            var finished = await Task.WhenAny(contextTask, Task.Delay(Timeout.Infinite, timeout.Token)).ConfigureAwait(false);
            if (finished != contextTask) return (null, null);

            var context = contextTask.Result;
            var code = context.Request.QueryString["code"];
            var html = "<html><body style='font-family:Segoe UI;background:#1e1e1e;color:#eee;text-align:center;padding-top:60px'>" +
                       (string.IsNullOrEmpty(code) ? "<h2>Authorization failed</h2>" : "<h2>GetMan received the authorization code</h2><p>You can close this tab.</p>") +
                       "</body></html>";
            var buf = Encoding.UTF8.GetBytes(html);
            context.Response.ContentType = "text/html; charset=utf-8";
            context.Response.ContentLength64 = buf.Length;
            await context.Response.OutputStream.WriteAsync(buf, ct).ConfigureAwait(false);
            context.Response.Close();
            return (code, verifier);
        }
        finally
        {
            try { listener.Stop(); } catch { }
        }
    }

    private static string Base64Url(byte[] data) =>
        Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    #endregion

    private static string ToHex(byte[] bytes)
    {
        var sb = new StringBuilder(bytes.Length * 2);
        foreach (var b in bytes) sb.Append(b.ToString("x2"));
        return sb.ToString();
    }
}
