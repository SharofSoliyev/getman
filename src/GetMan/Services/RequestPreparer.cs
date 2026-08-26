using System.Text;
using System.Text.Json;
using GetMan.Models;

namespace GetMan.Services;

/// <summary>Turns an editable <see cref="RequestModel"/> into a wire-ready <see cref="PreparedRequest"/>.</summary>
public static class RequestPreparer
{
    public static PreparedRequest Prepare(RequestModel req, CollectionNode owner, VariableResolver vars, AppSettings app)
    {
        string R(string s) => vars.Resolve(s ?? string.Empty);

        var settings = req.Settings.Clone();
        if (!app.SslVerification) settings.VerifySsl = false;
        if (!app.FollowRedirects) settings.FollowRedirects = false;
        if (settings.TimeoutMs <= 0 && app.RequestTimeoutMs > 0) settings.TimeoutMs = app.RequestTimeoutMs;

        var prepared = new PreparedRequest
        {
            Method = string.IsNullOrWhiteSpace(req.Method) ? "GET" : req.Method.Trim().ToUpperInvariant(),
            Mode = req.Body.Mode,
            Settings = settings,
            Auth = ResolveEffectiveAuth(req, owner),
            Name = owner?.Name ?? string.Empty
        };

        // ---- URL -------------------------------------------------------
        var rawUrl = R(req.Url).Trim();
        var basePart = UrlUtil.SplitBase(rawUrl);
        var queryPart = UrlUtil.SplitQuery(rawUrl);

        basePart = UrlUtil.ApplyPathVariables(basePart, req.PathVariables, R);

        if (req.QueryParams.Count > 0)
            queryPart = UrlUtil.BuildQuery(req.QueryParams, R, settings.EncodeUrl);

        var url = UrlUtil.EnsureScheme(UrlUtil.ComposeUrl(basePart, queryPart));
        prepared.Url = url;

        // ---- Headers ---------------------------------------------------
        foreach (var h in req.Headers)
        {
            if (!h.Enabled || string.IsNullOrWhiteSpace(h.Key)) continue;
            prepared.AddHeader(R(h.Key).Trim(), R(h.Value ?? string.Empty));
        }

        // ---- Body ------------------------------------------------------
        switch (req.Body.Mode)
        {
            case BodyMode.Raw:
                prepared.BodyText = R(req.Body.Raw);
                prepared.ContentType = RawContentType(req.Body.RawLanguage);
                break;

            case BodyMode.GraphQL:
                {
                    object varsObj = null;
                    var gv = R(req.Body.GraphQlVariables);
                    if (!string.IsNullOrWhiteSpace(gv))
                    {
                        try { varsObj = JsonSerializer.Deserialize<JsonElement>(gv); }
                        catch { varsObj = null; }
                    }
                    var payload = new Dictionary<string, object>
                    {
                        ["query"] = R(req.Body.GraphQlQuery)
                    };
                    if (varsObj != null) payload["variables"] = varsObj;
                    prepared.BodyText = JsonSerializer.Serialize(payload, new JsonSerializerOptions { WriteIndented = false });
                    prepared.ContentType = "application/json";
                    break;
                }

            case BodyMode.UrlEncoded:
                prepared.BodyText = UrlUtil.BuildQuery(req.Body.UrlEncoded, R, true);
                prepared.ContentType = "application/x-www-form-urlencoded";
                break;

            case BodyMode.FormData:
                foreach (var f in req.Body.FormData)
                {
                    if (!f.Enabled || string.IsNullOrWhiteSpace(f.Key)) continue;
                    prepared.Multipart.Add(new MultipartEntry
                    {
                        Name = R(f.Key),
                        IsFile = f.Kind == ParamKind.File,
                        FilePath = R(f.FilePath ?? string.Empty),
                        Value = R(f.Value ?? string.Empty),
                        ContentType = R(f.Description ?? string.Empty).StartsWith("content-type:", StringComparison.OrdinalIgnoreCase)
                            ? R(f.Description).Substring(13).Trim()
                            : string.Empty
                    });
                }
                break;

            case BodyMode.Binary:
                prepared.BinaryPath = R(req.Body.BinaryPath);
                break;
        }

        if (app.SendNoCacheHeader && !prepared.HasHeader("Cache-Control"))
            prepared.AddHeader("Cache-Control", "no-cache");

        return prepared;
    }

    public static string RawContentType(string language) => (language ?? "text").ToLowerInvariant() switch
    {
        "json" => "application/json",
        "javascript" => "application/javascript",
        "html" => "text/html",
        "xml" => "application/xml",
        _ => "text/plain"
    };

    /// <summary>Walks up the folder chain for AuthType.Inherit.</summary>
    public static AuthConfig ResolveEffectiveAuth(RequestModel req, CollectionNode owner)
    {
        if (req.Auth != null && req.Auth.Type != AuthType.Inherit)
            return req.Auth.Clone();

        var node = owner?.Parent;
        while (node != null)
        {
            if (node.Auth != null && node.Auth.Type != AuthType.Inherit)
                return node.Auth.Clone();
            node = node.Parent;
        }
        return new AuthConfig { Type = AuthType.None };
    }

    /// <summary>Resolve every {{var}} inside an auth config right before signing.</summary>
    public static AuthConfig ResolveAuthVariables(AuthConfig a, VariableResolver vars)
    {
        if (a == null) return new AuthConfig { Type = AuthType.None };
        var c = a.Clone();
        string R(string s) => vars.Resolve(s ?? string.Empty);
        c.Token = R(c.Token);
        c.Username = R(c.Username);
        c.Password = R(c.Password);
        c.Domain = R(c.Domain);
        c.Workstation = R(c.Workstation);
        c.ApiKeyName = R(c.ApiKeyName);
        c.ApiKeyValue = R(c.ApiKeyValue);
        c.OauthAccessTokenUrl = R(c.OauthAccessTokenUrl);
        c.OauthAuthUrl = R(c.OauthAuthUrl);
        c.OauthClientId = R(c.OauthClientId);
        c.OauthClientSecret = R(c.OauthClientSecret);
        c.OauthScope = R(c.OauthScope);
        c.OauthAudience = R(c.OauthAudience);
        c.OauthResource = R(c.OauthResource);
        c.OauthUsername = R(c.OauthUsername);
        c.OauthPassword = R(c.OauthPassword);
        c.OauthAccessToken = R(c.OauthAccessToken);
        c.OauthRefreshToken = R(c.OauthRefreshToken);
        c.AwsAccessKey = R(c.AwsAccessKey);
        c.AwsSecretKey = R(c.AwsSecretKey);
        c.AwsSessionToken = R(c.AwsSessionToken);
        c.AwsRegion = R(c.AwsRegion);
        c.AwsService = R(c.AwsService);
        c.HawkAuthId = R(c.HawkAuthId);
        c.HawkAuthKey = R(c.HawkAuthKey);
        c.HawkExt = R(c.HawkExt);
        return c;
    }
}
