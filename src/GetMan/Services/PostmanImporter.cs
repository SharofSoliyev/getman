using System.IO;
using System.Text;
using System.Text.Json;
using GetMan.Models;

namespace GetMan.Services;

public class ImportResult
{
    public List<CollectionNode> Collections { get; } = new();
    public List<EnvironmentModel> Environments { get; } = new();
    public List<string> Warnings { get; } = new();
    public string Error { get; set; }
    public bool Success => string.IsNullOrEmpty(Error) && (Collections.Count > 0 || Environments.Count > 0);
}

/// <summary>
/// Reads Postman collections (schema v1, v2.0 and v2.1), Postman environments/globals and
/// full Postman data dumps.
/// </summary>
public static class PostmanImporter
{
    public static ImportResult ImportFile(string path)
    {
        var result = new ImportResult();
        try
        {
            var text = File.ReadAllText(path, Encoding.UTF8);
            return ImportText(text, Path.GetFileNameWithoutExtension(path));
        }
        catch (Exception ex)
        {
            result.Error = ex.Message;
            return result;
        }
    }

    public static ImportResult ImportText(string text, string fallbackName = "Imported")
    {
        var result = new ImportResult();
        var options = new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
            MaxDepth = 512
        };

        JsonDocument doc;
        try
        {
            doc = JsonDocument.Parse(text, options);
        }
        catch (Exception ex)
        {
            // Most OpenAPI descriptions are YAML. Converting and retrying costs one parse on a
            // file that was never going to load anyway.
            var converted = YamlToJson.Convert(text);
            if (converted == null)
            {
                result.Error = "Not valid JSON: " + ex.Message;
                return result;
            }

            try
            {
                doc = JsonDocument.Parse(converted, options);
            }
            catch (Exception yamlEx)
            {
                result.Error = "Not valid JSON or YAML: " + yamlEx.Message;
                return result;
            }
        }

        using (doc)
        {
            var root = doc.RootElement;

            // OpenAPI and Swagger are not Postman formats, but they are what people have, and the
            // check here means every entry point - the app, the paste box and the CLI - accepts them.
            if (OpenApiImporter.Looks(root)) return OpenApiImporter.Import(root, fallbackName);

            if (root.ValueKind == JsonValueKind.Array)
            {
                foreach (var el in root.EnumerateArray())
                    Dispatch(el, result, fallbackName);
                return result;
            }

            if (root.ValueKind != JsonValueKind.Object)
            {
                result.Error = "Unrecognised file format.";
                return result;
            }

            // Postman "Export data" dump
            if (root.TryGetProperty("collections", out var cols) && cols.ValueKind == JsonValueKind.Array)
            {
                foreach (var c in cols.EnumerateArray()) Dispatch(c, result, fallbackName);
                if (root.TryGetProperty("environments", out var envs) && envs.ValueKind == JsonValueKind.Array)
                    foreach (var e in envs.EnumerateArray()) Dispatch(e, result, fallbackName);
                if (root.TryGetProperty("globals", out var g))
                {
                    var globals = ReadEnvironment(g, "Globals");
                    if (globals != null) { globals.IsGlobal = true; result.Environments.Add(globals); }
                }
                return result;
            }

            Dispatch(root, result, fallbackName);
        }

        if (!result.Success && string.IsNullOrEmpty(result.Error))
            result.Error = "Nothing importable was found in this file.";
        return result;
    }

    private static void Dispatch(JsonElement root, ImportResult result, string fallbackName)
    {
        if (root.ValueKind != JsonValueKind.Object) return;

        var scope = root.TryGetProperty("_postman_variable_scope", out var sc) ? sc.GetString() : null;
        bool looksLikeEnvironment = root.TryGetProperty("values", out var vals) && vals.ValueKind == JsonValueKind.Array
                                    && !root.TryGetProperty("item", out _) && !root.TryGetProperty("requests", out _);

        if (scope == "environment" || scope == "globals" || looksLikeEnvironment)
        {
            var env = ReadEnvironment(root, fallbackName);
            if (env != null)
            {
                env.IsGlobal = scope == "globals";
                result.Environments.Add(env);
            }
            return;
        }

        var schema = root.TryGetProperty("info", out var info) && info.TryGetProperty("schema", out var sch)
            ? sch.GetString() ?? string.Empty
            : string.Empty;

        if (root.TryGetProperty("item", out _) || schema.Contains("v2"))
        {
            var col = ReadV2Collection(root, result, fallbackName);
            if (col != null) result.Collections.Add(col);
            return;
        }

        if (root.TryGetProperty("requests", out _) || root.TryGetProperty("order", out _))
        {
            var col = ReadV1Collection(root, result, fallbackName);
            if (col != null) result.Collections.Add(col);
            return;
        }

        result.Warnings.Add("Skipped an object that is neither a collection nor an environment.");
    }

    #region environments

    private static EnvironmentModel ReadEnvironment(JsonElement root, string fallbackName)
    {
        var env = new EnvironmentModel
        {
            Name = root.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() : fallbackName
        };
        if (root.TryGetProperty("id", out var id) && id.ValueKind == JsonValueKind.String)
            env.Id = id.GetString();

        if (root.TryGetProperty("values", out var values) && values.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in values.EnumerateArray())
            {
                var item = new KeyValueItem
                {
                    Key = Str(v, "key"),
                    Value = Str(v, "value"),
                    Enabled = !(v.TryGetProperty("enabled", out var en) && en.ValueKind == JsonValueKind.False),
                    Secret = Str(v, "type") == "secret"
                };
                item.InitialValue = item.Value;
                if (!string.IsNullOrWhiteSpace(item.Key)) env.Variables.Add(item);
            }
        }
        else if (root.ValueKind == JsonValueKind.Object)
        {
            // globals dumped as a plain map
            foreach (var p in root.EnumerateObject())
            {
                if (p.Name.StartsWith("_", StringComparison.Ordinal) || p.Name is "id" or "name") continue;
                if (p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array) continue;
                env.Variables.Add(new KeyValueItem(p.Name, ElementToString(p.Value)));
            }
        }

        return env.Variables.Count > 0 || !string.IsNullOrEmpty(env.Name) ? env : null;
    }

    #endregion

    #region v2 collections

    private static CollectionNode ReadV2Collection(JsonElement root, ImportResult result, string fallbackName)
    {
        var node = new CollectionNode { Kind = NodeKind.Collection, Name = fallbackName };

        if (root.TryGetProperty("info", out var info))
        {
            node.Name = Str(info, "name", fallbackName);
            node.Description = DescriptionOf(info);
            var pid = Str(info, "_postman_id");
            if (!string.IsNullOrEmpty(pid)) node.Id = pid.Replace("-", "");
        }

        ReadEvents(root, s => node.PreRequestScript = s, s => node.TestScript = s);
        if (root.TryGetProperty("auth", out var auth)) node.Auth = ReadAuth(auth);
        ReadVariables(root, node.Variables);

        if (root.TryGetProperty("item", out var items) && items.ValueKind == JsonValueKind.Array)
            foreach (var item in items.EnumerateArray())
                AddV2Item(item, node, result);

        node.IsExpanded = true;
        node.FixupParents();
        return node;
    }

    private static void AddV2Item(JsonElement item, CollectionNode parent, ImportResult result)
    {
        if (item.ValueKind != JsonValueKind.Object) return;
        var name = Str(item, "name", "Untitled");

        if (item.TryGetProperty("item", out var children) && children.ValueKind == JsonValueKind.Array)
        {
            var folder = new CollectionNode { Kind = NodeKind.Folder, Name = name, Description = DescriptionOf(item), Parent = parent };
            ReadEvents(item, s => folder.PreRequestScript = s, s => folder.TestScript = s);
            if (item.TryGetProperty("auth", out var fauth)) folder.Auth = ReadAuth(fauth);
            ReadVariables(item, folder.Variables);
            foreach (var c in children.EnumerateArray()) AddV2Item(c, folder, result);
            parent.Children.Add(folder);
            return;
        }

        if (!item.TryGetProperty("request", out var reqEl)) return;

        var node = new CollectionNode { Kind = NodeKind.Request, Name = name, Parent = parent, Request = new RequestModel() };
        var req = node.Request;

        if (reqEl.ValueKind == JsonValueKind.String)
        {
            req.Method = "GET";
            req.Url = reqEl.GetString();
        }
        else
        {
            req.Method = Str(reqEl, "method", "GET").ToUpperInvariant();
            req.Description = DescriptionOf(reqEl);
            ReadUrl(reqEl, req);
            ReadHeaders(reqEl, req);
            ReadBody(reqEl, req, result);
            if (reqEl.TryGetProperty("auth", out var rauth)) req.Auth = ReadAuth(rauth);
            else req.Auth = new AuthConfig { Type = AuthType.Inherit };
        }

        node.Description = req.Description;
        ReadEvents(item, s => req.PreRequestScript = s, s => req.TestScript = s);
        ApplyProtocolProfile(item, req);
        parent.Children.Add(node);
    }

    private static void ApplyProtocolProfile(JsonElement item, RequestModel req)
    {
        if (!item.TryGetProperty("protocolProfileBehavior", out var p) || p.ValueKind != JsonValueKind.Object) return;
        if (p.TryGetProperty("followRedirects", out var fr) && fr.ValueKind is JsonValueKind.True or JsonValueKind.False)
            req.Settings.FollowRedirects = fr.GetBoolean();
        if (p.TryGetProperty("strictSSL", out var ss) && ss.ValueKind is JsonValueKind.True or JsonValueKind.False)
            req.Settings.VerifySsl = ss.GetBoolean();
        if (p.TryGetProperty("maxRedirects", out var mr) && mr.TryGetInt32(out var mrv))
            req.Settings.MaxRedirects = mrv;
        if (p.TryGetProperty("disableUrlEncoding", out var du) && du.ValueKind is JsonValueKind.True or JsonValueKind.False)
            req.Settings.EncodeUrl = !du.GetBoolean();
        if (p.TryGetProperty("disableCookies", out var dc) && dc.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            req.Settings.SendCookies = !dc.GetBoolean();
            req.Settings.StoreCookies = !dc.GetBoolean();
        }
    }

    private static void ReadUrl(JsonElement reqEl, RequestModel req)
    {
        if (!reqEl.TryGetProperty("url", out var url)) return;

        if (url.ValueKind == JsonValueKind.String)
        {
            req.Url = url.GetString() ?? string.Empty;
            foreach (var p in UrlUtil.ParseQuery(UrlUtil.SplitQuery(req.Url)))
                req.QueryParams.Add(p);
            SyncPathVariables(req);
            return;
        }

        if (url.ValueKind != JsonValueKind.Object) return;

        var raw = Str(url, "raw");
        if (string.IsNullOrEmpty(raw))
        {
            var sb = new StringBuilder();
            var protocol = Str(url, "protocol");
            if (!string.IsNullOrEmpty(protocol)) sb.Append(protocol).Append("://");
            if (url.TryGetProperty("host", out var host))
                sb.Append(host.ValueKind == JsonValueKind.Array
                    ? string.Join(".", host.EnumerateArray().Select(h => h.GetString()))
                    : host.GetString());
            var port = Str(url, "port");
            if (!string.IsNullOrEmpty(port)) sb.Append(':').Append(port);
            if (url.TryGetProperty("path", out var path))
            {
                if (path.ValueKind == JsonValueKind.Array)
                {
                    foreach (var seg in path.EnumerateArray())
                        sb.Append('/').Append(seg.ValueKind == JsonValueKind.String ? seg.GetString() : Str(seg, "value"));
                }
                else sb.Append('/').Append(path.GetString()?.TrimStart('/'));
            }
            raw = sb.ToString();
        }

        req.Url = UrlUtil.SplitBase(raw);

        if (url.TryGetProperty("query", out var query) && query.ValueKind == JsonValueKind.Array)
        {
            foreach (var q in query.EnumerateArray())
            {
                var item = new KeyValueItem
                {
                    Key = Str(q, "key"),
                    Value = Str(q, "value"),
                    Description = DescriptionOf(q),
                    Enabled = !(q.TryGetProperty("disabled", out var d) && d.ValueKind == JsonValueKind.True)
                };
                if (!string.IsNullOrEmpty(item.Key)) req.QueryParams.Add(item);
            }
        }
        else
        {
            foreach (var p in UrlUtil.ParseQuery(UrlUtil.SplitQuery(raw)))
                req.QueryParams.Add(p);
        }

        if (url.TryGetProperty("variable", out var variables) && variables.ValueKind == JsonValueKind.Array)
        {
            foreach (var v in variables.EnumerateArray())
            {
                var key = Str(v, "key");
                if (string.IsNullOrEmpty(key)) continue;
                req.PathVariables.Add(new KeyValueItem(key, Str(v, "value")) { Description = DescriptionOf(v) });
            }
        }
        SyncPathVariables(req);

        var q2 = UrlUtil.BuildQuery(req.QueryParams, s => s, false);
        if (!string.IsNullOrEmpty(q2)) req.Url = UrlUtil.ComposeUrl(req.Url, q2);
    }

    private static void SyncPathVariables(RequestModel req)
    {
        foreach (var name in UrlUtil.ExtractPathVariableNames(req.Url))
            if (!req.PathVariables.Any(p => p.Key == name))
                req.PathVariables.Add(new KeyValueItem(name, string.Empty));
    }

    private static void ReadHeaders(JsonElement reqEl, RequestModel req)
    {
        if (!reqEl.TryGetProperty("header", out var headers)) return;

        if (headers.ValueKind == JsonValueKind.String)
        {
            foreach (var line in (headers.GetString() ?? string.Empty).Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var i = line.IndexOf(':');
                if (i <= 0) continue;
                req.Headers.Add(new KeyValueItem(line.Substring(0, i).Trim(), line.Substring(i + 1).Trim()));
            }
            return;
        }

        if (headers.ValueKind != JsonValueKind.Array) return;
        foreach (var h in headers.EnumerateArray())
        {
            var key = Str(h, "key");
            if (string.IsNullOrWhiteSpace(key)) continue;
            req.Headers.Add(new KeyValueItem
            {
                Key = key,
                Value = Str(h, "value"),
                Description = DescriptionOf(h),
                Enabled = !(h.TryGetProperty("disabled", out var d) && d.ValueKind == JsonValueKind.True)
            });
        }
    }

    private static void ReadBody(JsonElement reqEl, RequestModel req, ImportResult result)
    {
        if (!reqEl.TryGetProperty("body", out var body) || body.ValueKind != JsonValueKind.Object) return;

        var mode = Str(body, "mode", "raw");
        switch (mode)
        {
            case "raw":
                req.Body.Mode = BodyMode.Raw;
                req.Body.Raw = Str(body, "raw");
                req.Body.RawLanguage = "text";
                if (body.TryGetProperty("options", out var opt) && opt.TryGetProperty("raw", out var rawOpt))
                    req.Body.RawLanguage = Str(rawOpt, "language", "text");
                else if (req.Body.Raw.TrimStart().StartsWith("{") || req.Body.Raw.TrimStart().StartsWith("["))
                    req.Body.RawLanguage = "json";
                break;

            case "urlencoded":
                req.Body.Mode = BodyMode.UrlEncoded;
                if (body.TryGetProperty("urlencoded", out var ue) && ue.ValueKind == JsonValueKind.Array)
                    foreach (var kv in ue.EnumerateArray())
                        req.Body.UrlEncoded.Add(new KeyValueItem
                        {
                            Key = Str(kv, "key"),
                            Value = Str(kv, "value"),
                            Description = DescriptionOf(kv),
                            Enabled = !(kv.TryGetProperty("disabled", out var d) && d.ValueKind == JsonValueKind.True)
                        });
                break;

            case "formdata":
                req.Body.Mode = BodyMode.FormData;
                if (body.TryGetProperty("formdata", out var fd) && fd.ValueKind == JsonValueKind.Array)
                    foreach (var kv in fd.EnumerateArray())
                    {
                        var isFile = Str(kv, "type") == "file";
                        var src = string.Empty;
                        if (kv.TryGetProperty("src", out var s))
                            src = s.ValueKind == JsonValueKind.Array
                                ? (s.GetArrayLength() > 0 ? s[0].GetString() : string.Empty)
                                : s.GetString();
                        req.Body.FormData.Add(new KeyValueItem
                        {
                            Key = Str(kv, "key"),
                            Value = isFile ? string.Empty : Str(kv, "value"),
                            FilePath = src ?? string.Empty,
                            Kind = isFile ? ParamKind.File : ParamKind.Text,
                            Description = DescriptionOf(kv),
                            Enabled = !(kv.TryGetProperty("disabled", out var d) && d.ValueKind == JsonValueKind.True)
                        });
                    }
                break;

            case "file":
                req.Body.Mode = BodyMode.Binary;
                if (body.TryGetProperty("file", out var f))
                    req.Body.BinaryPath = Str(f, "src");
                break;

            case "graphql":
                req.Body.Mode = BodyMode.GraphQL;
                if (body.TryGetProperty("graphql", out var gq))
                {
                    req.Body.GraphQlQuery = Str(gq, "query");
                    if (gq.TryGetProperty("variables", out var gv))
                        req.Body.GraphQlVariables = gv.ValueKind == JsonValueKind.String ? gv.GetString() : gv.GetRawText();
                }
                break;

            default:
                req.Body.Mode = BodyMode.None;
                result.Warnings.Add($"Body mode '{mode}' is not supported and was dropped.");
                break;
        }
    }

    private static void ReadEvents(JsonElement el, Action<string> setPre, Action<string> setTest)
    {
        if (!el.TryGetProperty("event", out var events) || events.ValueKind != JsonValueKind.Array) return;
        foreach (var ev in events.EnumerateArray())
        {
            var listen = Str(ev, "listen");
            if (!ev.TryGetProperty("script", out var script)) continue;
            var code = ScriptText(script);
            if (string.IsNullOrWhiteSpace(code)) continue;
            if (listen == "prerequest") setPre(code);
            else if (listen == "test") setTest(code);
        }
    }

    private static string ScriptText(JsonElement script)
    {
        if (script.TryGetProperty("exec", out var exec))
        {
            if (exec.ValueKind == JsonValueKind.Array)
                return string.Join("\n", exec.EnumerateArray().Select(x => x.ValueKind == JsonValueKind.String ? x.GetString() : string.Empty));
            if (exec.ValueKind == JsonValueKind.String)
                return exec.GetString();
        }
        if (script.ValueKind == JsonValueKind.String) return script.GetString();
        return string.Empty;
    }

    private static void ReadVariables(JsonElement el, IList<KeyValueItem> target)
    {
        if (!el.TryGetProperty("variable", out var vars) || vars.ValueKind != JsonValueKind.Array) return;
        foreach (var v in vars.EnumerateArray())
        {
            var key = Str(v, "key");
            if (string.IsNullOrWhiteSpace(key)) continue;
            target.Add(new KeyValueItem
            {
                Key = key,
                Value = Str(v, "value"),
                Description = DescriptionOf(v),
                Enabled = !(v.TryGetProperty("disabled", out var d) && d.ValueKind == JsonValueKind.True)
            });
        }
    }

    private static AuthConfig ReadAuth(JsonElement auth)
    {
        var cfg = new AuthConfig { Type = AuthType.None };
        if (auth.ValueKind != JsonValueKind.Object) return cfg;

        var type = Str(auth, "type", "noauth");
        Dictionary<string, string> Bag(string key)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            if (!auth.TryGetProperty(key, out var el)) return d;
            if (el.ValueKind == JsonValueKind.Array)
                foreach (var e in el.EnumerateArray())
                    d[Str(e, "key")] = ElementToString(e.TryGetProperty("value", out var v) ? v : default);
            else if (el.ValueKind == JsonValueKind.Object)
                foreach (var p in el.EnumerateObject())
                    d[p.Name] = ElementToString(p.Value);
            return d;
        }

        switch (type)
        {
            case "bearer":
                cfg.Type = AuthType.Bearer;
                cfg.Token = Bag("bearer").GetValueOrDefault("token", string.Empty);
                break;

            case "basic":
                {
                    cfg.Type = AuthType.Basic;
                    var b = Bag("basic");
                    cfg.Username = b.GetValueOrDefault("username", string.Empty);
                    cfg.Password = b.GetValueOrDefault("password", string.Empty);
                    break;
                }

            case "apikey":
                {
                    cfg.Type = AuthType.ApiKey;
                    var b = Bag("apikey");
                    cfg.ApiKeyName = b.GetValueOrDefault("key", string.Empty);
                    cfg.ApiKeyValue = b.GetValueOrDefault("value", string.Empty);
                    cfg.ApiKeyLocation = b.GetValueOrDefault("in", "header");
                    break;
                }

            case "oauth2":
                {
                    cfg.Type = AuthType.OAuth2;
                    var b = Bag("oauth2");
                    cfg.OauthAccessToken = b.GetValueOrDefault("accessToken", string.Empty);
                    cfg.OauthGrantType = MapGrant(b.GetValueOrDefault("grant_type", "client_credentials"));
                    cfg.OauthAccessTokenUrl = b.GetValueOrDefault("accessTokenUrl", string.Empty);
                    cfg.OauthAuthUrl = b.GetValueOrDefault("authUrl", string.Empty);
                    cfg.OauthClientId = b.GetValueOrDefault("clientId", string.Empty);
                    cfg.OauthClientSecret = b.GetValueOrDefault("clientSecret", string.Empty);
                    cfg.OauthScope = b.GetValueOrDefault("scope", string.Empty);
                    cfg.OauthRedirectUri = b.GetValueOrDefault("redirect_uri", cfg.OauthRedirectUri);
                    cfg.OauthHeaderPrefix = b.GetValueOrDefault("headerPrefix", "Bearer");
                    cfg.OauthAddTokenTo = b.GetValueOrDefault("addTokenTo", "header") == "queryParams" ? "query" : "header";
                    cfg.OauthClientAuth = b.GetValueOrDefault("client_authentication", "body");
                    break;
                }

            case "digest":
                {
                    cfg.Type = AuthType.Digest;
                    var b = Bag("digest");
                    cfg.Username = b.GetValueOrDefault("username", string.Empty);
                    cfg.Password = b.GetValueOrDefault("password", string.Empty);
                    cfg.DigestRealm = b.GetValueOrDefault("realm", string.Empty);
                    cfg.DigestAlgorithm = b.GetValueOrDefault("algorithm", "MD5");
                    cfg.DigestQop = b.GetValueOrDefault("qop", "auth");
                    break;
                }

            case "ntlm":
                {
                    cfg.Type = AuthType.NTLM;
                    var b = Bag("ntlm");
                    cfg.Username = b.GetValueOrDefault("username", string.Empty);
                    cfg.Password = b.GetValueOrDefault("password", string.Empty);
                    cfg.Domain = b.GetValueOrDefault("domain", string.Empty);
                    cfg.Workstation = b.GetValueOrDefault("workstation", string.Empty);
                    break;
                }

            case "awsv4":
                {
                    cfg.Type = AuthType.AwsV4;
                    var b = Bag("awsv4");
                    cfg.AwsAccessKey = b.GetValueOrDefault("accessKey", string.Empty);
                    cfg.AwsSecretKey = b.GetValueOrDefault("secretKey", string.Empty);
                    cfg.AwsSessionToken = b.GetValueOrDefault("sessionToken", string.Empty);
                    cfg.AwsRegion = b.GetValueOrDefault("region", "us-east-1");
                    cfg.AwsService = b.GetValueOrDefault("service", "execute-api");
                    break;
                }

            case "hawk":
                {
                    cfg.Type = AuthType.Hawk;
                    var b = Bag("hawk");
                    cfg.HawkAuthId = b.GetValueOrDefault("authId", string.Empty);
                    cfg.HawkAuthKey = b.GetValueOrDefault("authKey", string.Empty);
                    cfg.HawkAlgorithm = b.GetValueOrDefault("algorithm", "sha256");
                    cfg.HawkExt = b.GetValueOrDefault("extraData", string.Empty);
                    break;
                }

            case "noauth":
                cfg.Type = AuthType.None;
                break;

            default:
                cfg.Type = AuthType.None;
                break;
        }
        return cfg;
    }

    private static string MapGrant(string g) => g switch
    {
        "authorization_code" => "authorization_code",
        "authorization_code_with_pkce" => "authorization_code",
        "password_credentials" => "password",
        "password" => "password",
        "implicit" => "authorization_code",
        _ => "client_credentials"
    };

    #endregion

    #region v1 collections

    private static CollectionNode ReadV1Collection(JsonElement root, ImportResult result, string fallbackName)
    {
        var col = new CollectionNode
        {
            Kind = NodeKind.Collection,
            Name = Str(root, "name", fallbackName),
            Description = Str(root, "description"),
            IsExpanded = true
        };

        var folders = new Dictionary<string, CollectionNode>();
        if (root.TryGetProperty("folders", out var fs) && fs.ValueKind == JsonValueKind.Array)
        {
            foreach (var f in fs.EnumerateArray())
            {
                var id = Str(f, "id");
                var folder = new CollectionNode { Kind = NodeKind.Folder, Name = Str(f, "name", "Folder"), Parent = col };
                if (!string.IsNullOrEmpty(id)) folders[id] = folder;
                col.Children.Add(folder);
            }
        }

        if (root.TryGetProperty("requests", out var reqs) && reqs.ValueKind == JsonValueKind.Array)
        {
            foreach (var r in reqs.EnumerateArray())
            {
                var node = new CollectionNode
                {
                    Kind = NodeKind.Request,
                    Name = Str(r, "name", "Untitled"),
                    Description = Str(r, "description"),
                    Request = new RequestModel
                    {
                        Method = Str(r, "method", "GET").ToUpperInvariant(),
                        Url = Str(r, "url"),
                        PreRequestScript = Str(r, "preRequestScript"),
                        TestScript = Str(r, "tests")
                    }
                };

                foreach (var line in Str(r, "headers").Split('\n', StringSplitOptions.RemoveEmptyEntries))
                {
                    var i = line.IndexOf(':');
                    if (i > 0) node.Request.Headers.Add(new KeyValueItem(line.Substring(0, i).Trim(), line.Substring(i + 1).Trim()));
                }

                var dataMode = Str(r, "dataMode", "raw");
                if (dataMode == "raw")
                {
                    node.Request.Body.Mode = BodyMode.Raw;
                    node.Request.Body.Raw = Str(r, "rawModeData");
                    node.Request.Body.RawLanguage = node.Request.Body.Raw.TrimStart().StartsWith("{") ? "json" : "text";
                }
                else if (r.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Array)
                {
                    node.Request.Body.Mode = dataMode == "urlencoded" ? BodyMode.UrlEncoded : BodyMode.FormData;
                    var target = node.Request.Body.Mode == BodyMode.UrlEncoded ? node.Request.Body.UrlEncoded : node.Request.Body.FormData;
                    foreach (var d in data.EnumerateArray())
                        target.Add(new KeyValueItem(Str(d, "key"), Str(d, "value")));
                }

                foreach (var p in UrlUtil.ParseQuery(UrlUtil.SplitQuery(node.Request.Url)))
                    node.Request.QueryParams.Add(p);

                var folderId = Str(r, "folder");
                if (!string.IsNullOrEmpty(folderId) && folders.TryGetValue(folderId, out var parentFolder))
                {
                    node.Parent = parentFolder;
                    parentFolder.Children.Add(node);
                }
                else
                {
                    node.Parent = col;
                    col.Children.Add(node);
                }
            }
        }

        col.FixupParents();
        return col;
    }

    #endregion

    #region helpers

    private static string Str(JsonElement el, string name, string fallback = "")
    {
        if (el.ValueKind != JsonValueKind.Object) return fallback;
        if (!el.TryGetProperty(name, out var v)) return fallback;
        return ElementToString(v) is { Length: > 0 } s ? s : fallback;
    }

    private static string ElementToString(JsonElement v) => v.ValueKind switch
    {
        JsonValueKind.String => v.GetString() ?? string.Empty,
        JsonValueKind.Number => v.ToString(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Undefined or JsonValueKind.Null => string.Empty,
        _ => v.GetRawText()
    };

    private static string DescriptionOf(JsonElement el)
    {
        if (el.ValueKind != JsonValueKind.Object || !el.TryGetProperty("description", out var d)) return string.Empty;
        if (d.ValueKind == JsonValueKind.String) return d.GetString() ?? string.Empty;
        if (d.ValueKind == JsonValueKind.Object && d.TryGetProperty("content", out var c)) return c.GetString() ?? string.Empty;
        return string.Empty;
    }

    #endregion
}
