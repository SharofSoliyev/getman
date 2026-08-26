using System.Text.Json;
using System.Text.Json.Nodes;
using GetMan.Models;

namespace GetMan.Services;

/// <summary>Writes collections and environments back out in Postman Collection v2.1 format.</summary>
public static class PostmanExporter
{
    private static readonly JsonSerializerOptions Opts = new()
    {
        WriteIndented = true,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public static string ExportCollection(CollectionNode collection)
    {
        var root = new JsonObject
        {
            ["info"] = new JsonObject
            {
                ["_postman_id"] = collection.Id,
                ["name"] = collection.Name,
                ["description"] = collection.Description ?? string.Empty,
                ["schema"] = "https://schema.getpostman.com/json/collection/v2.1.0/collection.json",
                ["_exporter_id"] = "GetMan"
            },
            ["item"] = ItemsOf(collection)
        };

        var events = EventsOf(collection.PreRequestScript, collection.TestScript);
        if (events != null) root["event"] = events;
        if (collection.Auth != null && collection.Auth.Type != AuthType.None && collection.Auth.Type != AuthType.Inherit)
            root["auth"] = AuthOf(collection.Auth);
        if (collection.Variables.Count > 0)
            root["variable"] = VariablesOf(collection.Variables);

        return root.ToJsonString(Opts);
    }

    public static string ExportEnvironment(EnvironmentModel env)
    {
        var values = new JsonArray();
        foreach (var v in env.Variables)
            values.Add(new JsonObject
            {
                ["key"] = v.Key,
                ["value"] = v.Value,
                ["type"] = v.Secret ? "secret" : "default",
                ["enabled"] = v.Enabled
            });

        return new JsonObject
        {
            ["id"] = env.Id,
            ["name"] = env.Name,
            ["values"] = values,
            ["_postman_variable_scope"] = env.IsGlobal ? "globals" : "environment",
            ["_postman_exported_at"] = DateTime.UtcNow.ToString("o"),
            ["_postman_exported_using"] = "GetMan/1.0"
        }.ToJsonString(Opts);
    }

    private static JsonArray ItemsOf(CollectionNode node)
    {
        var arr = new JsonArray();
        foreach (var child in node.Children)
            arr.Add(child.Kind == NodeKind.Request ? RequestItemOf(child) : FolderItemOf(child));
        return arr;
    }

    private static JsonObject FolderItemOf(CollectionNode folder)
    {
        var obj = new JsonObject
        {
            ["name"] = folder.Name,
            ["item"] = ItemsOf(folder)
        };
        if (!string.IsNullOrEmpty(folder.Description)) obj["description"] = folder.Description;
        var events = EventsOf(folder.PreRequestScript, folder.TestScript);
        if (events != null) obj["event"] = events;
        if (folder.Auth != null && folder.Auth.Type != AuthType.None && folder.Auth.Type != AuthType.Inherit)
            obj["auth"] = AuthOf(folder.Auth);
        if (folder.Variables.Count > 0) obj["variable"] = VariablesOf(folder.Variables);
        return obj;
    }

    private static JsonObject RequestItemOf(CollectionNode node)
    {
        var r = node.Request ?? new RequestModel();
        var request = new JsonObject
        {
            ["method"] = r.Method,
            ["header"] = HeadersOf(r.Headers),
            ["url"] = UrlOf(r)
        };
        if (!string.IsNullOrEmpty(r.Description)) request["description"] = r.Description;

        var body = BodyOf(r.Body);
        if (body != null) request["body"] = body;

        if (r.Auth != null && r.Auth.Type != AuthType.Inherit)
            request["auth"] = AuthOf(r.Auth);

        var item = new JsonObject
        {
            ["name"] = node.Name,
            ["request"] = request,
            ["response"] = new JsonArray()
        };

        var events = EventsOf(r.PreRequestScript, r.TestScript);
        if (events != null) item["event"] = events;

        item["protocolProfileBehavior"] = new JsonObject
        {
            ["followRedirects"] = r.Settings.FollowRedirects,
            ["strictSSL"] = r.Settings.VerifySsl,
            ["maxRedirects"] = r.Settings.MaxRedirects,
            ["disableUrlEncoding"] = !r.Settings.EncodeUrl
        };

        return item;
    }

    private static JsonArray HeadersOf(IEnumerable<KeyValueItem> headers)
    {
        var arr = new JsonArray();
        foreach (var h in headers)
        {
            if (string.IsNullOrWhiteSpace(h.Key)) continue;
            var o = new JsonObject { ["key"] = h.Key, ["value"] = h.Value, ["type"] = "text" };
            if (!h.Enabled) o["disabled"] = true;
            if (!string.IsNullOrEmpty(h.Description)) o["description"] = h.Description;
            arr.Add(o);
        }
        return arr;
    }

    private static JsonObject UrlOf(RequestModel r)
    {
        var raw = r.Url ?? string.Empty;
        var basePart = UrlUtil.SplitBase(raw);
        var query = UrlUtil.BuildQuery(r.QueryParams, s => s, false);
        var full = UrlUtil.ComposeUrl(basePart, query);

        var obj = new JsonObject { ["raw"] = full };

        var withScheme = UrlUtil.EnsureScheme(basePart);
        try
        {
            var uri = new Uri(withScheme);
            obj["protocol"] = uri.Scheme;
            var hostArr = new JsonArray();
            foreach (var part in uri.Host.Split('.')) hostArr.Add(part);
            obj["host"] = hostArr;
            if (!uri.IsDefaultPort) obj["port"] = uri.Port.ToString();
            var pathArr = new JsonArray();
            foreach (var seg in uri.AbsolutePath.Split('/', StringSplitOptions.RemoveEmptyEntries)) pathArr.Add(seg);
            if (pathArr.Count > 0) obj["path"] = pathArr;
        }
        catch { }

        if (r.QueryParams.Count > 0)
        {
            var qArr = new JsonArray();
            foreach (var p in r.QueryParams)
            {
                var o = new JsonObject { ["key"] = p.Key, ["value"] = p.Value };
                if (!p.Enabled) o["disabled"] = true;
                if (!string.IsNullOrEmpty(p.Description)) o["description"] = p.Description;
                qArr.Add(o);
            }
            obj["query"] = qArr;
        }

        if (r.PathVariables.Count > 0)
        {
            var vArr = new JsonArray();
            foreach (var p in r.PathVariables)
                vArr.Add(new JsonObject { ["key"] = p.Key, ["value"] = p.Value });
            obj["variable"] = vArr;
        }

        return obj;
    }

    private static JsonObject BodyOf(RequestBody b)
    {
        switch (b.Mode)
        {
            case BodyMode.None:
                return null;

            case BodyMode.Raw:
                return new JsonObject
                {
                    ["mode"] = "raw",
                    ["raw"] = b.Raw,
                    ["options"] = new JsonObject { ["raw"] = new JsonObject { ["language"] = b.RawLanguage } }
                };

            case BodyMode.UrlEncoded:
                {
                    var arr = new JsonArray();
                    foreach (var kv in b.UrlEncoded)
                    {
                        var o = new JsonObject { ["key"] = kv.Key, ["value"] = kv.Value, ["type"] = "text" };
                        if (!kv.Enabled) o["disabled"] = true;
                        arr.Add(o);
                    }
                    return new JsonObject { ["mode"] = "urlencoded", ["urlencoded"] = arr };
                }

            case BodyMode.FormData:
                {
                    var arr = new JsonArray();
                    foreach (var kv in b.FormData)
                    {
                        var o = new JsonObject { ["key"] = kv.Key, ["type"] = kv.Kind == ParamKind.File ? "file" : "text" };
                        if (kv.Kind == ParamKind.File) o["src"] = kv.FilePath;
                        else o["value"] = kv.Value;
                        if (!kv.Enabled) o["disabled"] = true;
                        arr.Add(o);
                    }
                    return new JsonObject { ["mode"] = "formdata", ["formdata"] = arr };
                }

            case BodyMode.Binary:
                return new JsonObject { ["mode"] = "file", ["file"] = new JsonObject { ["src"] = b.BinaryPath } };

            case BodyMode.GraphQL:
                return new JsonObject
                {
                    ["mode"] = "graphql",
                    ["graphql"] = new JsonObject { ["query"] = b.GraphQlQuery, ["variables"] = b.GraphQlVariables }
                };
        }
        return null;
    }

    private static JsonArray EventsOf(string pre, string test)
    {
        if (string.IsNullOrWhiteSpace(pre) && string.IsNullOrWhiteSpace(test)) return null;
        var arr = new JsonArray();
        if (!string.IsNullOrWhiteSpace(pre)) arr.Add(EventOf("prerequest", pre));
        if (!string.IsNullOrWhiteSpace(test)) arr.Add(EventOf("test", test));
        return arr;
    }

    private static JsonObject EventOf(string listen, string code)
    {
        var exec = new JsonArray();
        foreach (var line in code.Replace("\r\n", "\n").Split('\n')) exec.Add(line);
        return new JsonObject
        {
            ["listen"] = listen,
            ["script"] = new JsonObject { ["type"] = "text/javascript", ["exec"] = exec }
        };
    }

    private static JsonArray VariablesOf(IEnumerable<KeyValueItem> vars)
    {
        var arr = new JsonArray();
        foreach (var v in vars)
        {
            var o = new JsonObject { ["key"] = v.Key, ["value"] = v.Value, ["type"] = "string" };
            if (!v.Enabled) o["disabled"] = true;
            arr.Add(o);
        }
        return arr;
    }

    private static JsonObject AuthOf(AuthConfig a)
    {
        JsonArray Pairs(params (string Key, string Value)[] items)
        {
            var arr = new JsonArray();
            foreach (var (k, v) in items)
                arr.Add(new JsonObject { ["key"] = k, ["value"] = v, ["type"] = "string" });
            return arr;
        }

        return a.Type switch
        {
            AuthType.Bearer => new JsonObject { ["type"] = "bearer", ["bearer"] = Pairs(("token", a.Token)) },
            AuthType.Basic => new JsonObject { ["type"] = "basic", ["basic"] = Pairs(("username", a.Username), ("password", a.Password)) },
            AuthType.ApiKey => new JsonObject { ["type"] = "apikey", ["apikey"] = Pairs(("key", a.ApiKeyName), ("value", a.ApiKeyValue), ("in", a.ApiKeyLocation)) },
            AuthType.Digest => new JsonObject { ["type"] = "digest", ["digest"] = Pairs(("username", a.Username), ("password", a.Password), ("algorithm", a.DigestAlgorithm)) },
            AuthType.NTLM => new JsonObject { ["type"] = "ntlm", ["ntlm"] = Pairs(("username", a.Username), ("password", a.Password), ("domain", a.Domain), ("workstation", a.Workstation)) },
            AuthType.AwsV4 => new JsonObject { ["type"] = "awsv4", ["awsv4"] = Pairs(("accessKey", a.AwsAccessKey), ("secretKey", a.AwsSecretKey), ("sessionToken", a.AwsSessionToken), ("region", a.AwsRegion), ("service", a.AwsService)) },
            AuthType.Hawk => new JsonObject { ["type"] = "hawk", ["hawk"] = Pairs(("authId", a.HawkAuthId), ("authKey", a.HawkAuthKey), ("algorithm", a.HawkAlgorithm), ("extraData", a.HawkExt)) },
            AuthType.OAuth2 => new JsonObject
            {
                ["type"] = "oauth2",
                ["oauth2"] = Pairs(
                    ("accessToken", a.OauthAccessToken),
                    ("grant_type", a.OauthGrantType),
                    ("accessTokenUrl", a.OauthAccessTokenUrl),
                    ("authUrl", a.OauthAuthUrl),
                    ("clientId", a.OauthClientId),
                    ("clientSecret", a.OauthClientSecret),
                    ("scope", a.OauthScope),
                    ("redirect_uri", a.OauthRedirectUri),
                    ("headerPrefix", a.OauthHeaderPrefix),
                    ("addTokenTo", a.OauthAddTokenTo == "query" ? "queryParams" : "header"))
            },
            _ => new JsonObject { ["type"] = "noauth" }
        };
    }
}
