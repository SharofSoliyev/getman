using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Xml.Linq;
using GetMan.Models;
using Jint;
using Jint.Runtime;

namespace GetMan.Services;

public enum ScriptPhase { PreRequest, Test }

public class VariableOp
{
    public string Scope { get; set; } = "environment";
    public string Key { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string Op { get; set; } = "set"; // set | unset | clear
}

/// <summary>Everything a script may read or mutate for one execution.</summary>
public class ScriptContext
{
    public VariableResolver Vars { get; set; }
    public PreparedRequest Request { get; set; }
    public ResponseModel Response { get; set; }
    public AppSettings App { get; set; } = new();
    public HttpEngine Engine { get; set; }

    public string RequestName { get; set; } = string.Empty;
    public string RequestId { get; set; } = string.Empty;
    public int Iteration { get; set; }
    public int IterationCount { get; set; } = 1;

    public List<TestResult> Tests { get; } = new();
    public List<ConsoleEntry> Console { get; } = new();
    public List<VariableOp> VariableOps { get; } = new();
    public string NextRequest { get; set; }
    public bool SkipRequest { get; set; }
    public string Error { get; set; }
}

public class ScriptRuntime
{
    public void Run(string code, ScriptPhase phase, ScriptContext ctx, CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(code)) return;

        var host = new ScriptHost(ctx, phase);
        Engine engine = null;
        try
        {
            engine = new Engine(options =>
            {
                options.LimitRecursion(120);
                options.TimeoutInterval(TimeSpan.FromMilliseconds(Math.Max(500, ctx.App.ScriptTimeoutMs)));
                options.MaxStatements(2_000_000);
                options.Strict(false);
                options.CancellationToken(ct);
            });

            engine.SetValue("__host", host);
            engine.Execute(ScriptBootstrap.Source);
            engine.Execute(code);

            if (phase == ScriptPhase.PreRequest)
                engine.Execute("pm.__flushRequest();");
        }
        catch (JavaScriptException jse)
        {
            var line = jse.Location.Start.Line;
            ctx.Error = $"{jse.Error} (line {line})";
            ctx.Console.Add(new ConsoleEntry { Level = "error", Message = ctx.Error, Source = phase.ToString() });
            if (phase == ScriptPhase.Test)
                ctx.Tests.Add(new TestResult { Name = "Script error", Status = TestStatus.Fail, Message = ctx.Error });
        }
        catch (StatementsCountOverflowException)
        {
            ctx.Error = "Script exceeded the statement limit.";
            ctx.Console.Add(new ConsoleEntry { Level = "error", Message = ctx.Error, Source = phase.ToString() });
        }
        catch (TimeoutException)
        {
            ctx.Error = "Script timed out.";
            ctx.Console.Add(new ConsoleEntry { Level = "error", Message = ctx.Error, Source = phase.ToString() });
        }
        catch (OperationCanceledException)
        {
            ctx.Error = "Script cancelled.";
        }
        catch (Exception ex)
        {
            ctx.Error = ex.Message;
            ctx.Console.Add(new ConsoleEntry { Level = "error", Message = ex.Message, Source = phase.ToString() });
        }
        finally
        {
            engine?.Dispose();
        }
    }
}

/// <summary>Bridge object exposed to JavaScript as <c>__host</c>. Member names must stay camelCase.</summary>
public class ScriptHost
{
    private readonly ScriptContext _ctx;
    private readonly ScriptPhase _phase;
    private readonly Stopwatch _clock = Stopwatch.StartNew();

    public ScriptHost(ScriptContext ctx, ScriptPhase phase)
    {
        _ctx = ctx;
        _phase = phase;
    }

    private static readonly JsonSerializerOptions JsonOpts = new()
    {
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
    };

    public double now() => _clock.Elapsed.TotalMilliseconds;

    public void log(string level, string message) =>
        _ctx.Console.Add(new ConsoleEntry { Level = level, Message = message ?? string.Empty, Source = _phase.ToString() });

    public void addTest(string name, bool passed, string message, double duration) =>
        _ctx.Tests.Add(new TestResult
        {
            Name = name,
            Status = passed ? TestStatus.Pass : TestStatus.Fail,
            Message = message ?? string.Empty,
            DurationMs = duration
        });

    public void skipTest(string name) =>
        _ctx.Tests.Add(new TestResult { Name = name, Status = TestStatus.Skip });

    public void setNextRequest(string name) => _ctx.NextRequest = name;

    public void skipRequest() => _ctx.SkipRequest = true;

    public bool hasResponse() => _ctx.Response != null;

    public string replaceIn(string s) => _ctx.Vars.Resolve(s ?? string.Empty);

    public string base64Encode(string s) => Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? string.Empty));

    public string base64Decode(string s)
    {
        try { return Encoding.UTF8.GetString(Convert.FromBase64String(s ?? string.Empty)); }
        catch { return string.Empty; }
    }

    public string xmlToJson(string xml)
    {
        try
        {
            var doc = XDocument.Parse(xml);
            var node = new JsonObject { [doc.Root.Name.LocalName] = XmlToNode(doc.Root) };
            return node.ToJsonString(JsonOpts);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOpts);
        }
    }

    private static JsonNode XmlToNode(XElement el)
    {
        if (!el.HasElements && !el.HasAttributes)
            return JsonValue.Create(el.Value);

        var obj = new JsonObject();
        foreach (var a in el.Attributes())
            obj["$" + a.Name.LocalName] = JsonValue.Create(a.Value);

        foreach (var group in el.Elements().GroupBy(e => e.Name.LocalName))
        {
            var items = group.ToList();
            if (items.Count == 1)
                obj[group.Key] = XmlToNode(items[0]);
            else
            {
                var arr = new JsonArray();
                foreach (var i in items) arr.Add(XmlToNode(i));
                obj[group.Key] = arr;
            }
        }
        if (!el.HasElements && el.HasAttributes && !string.IsNullOrWhiteSpace(el.Value))
            obj["_"] = JsonValue.Create(el.Value);
        return obj;
    }

    #region variables

    private Dictionary<string, string> Bag(string kind) => kind switch
    {
        "environment" => _ctx.Vars.EnvironmentVars,
        "globals" => _ctx.Vars.Globals,
        "collection" => _ctx.Vars.CollectionVars,
        "data" => _ctx.Vars.DataVars,
        _ => null
    };

    public string varGet(string kind, string key)
    {
        if (kind == "any")
            return _ctx.Vars.TryGetRaw(key, out var any) ? any : null;
        var bag = Bag(kind);
        return bag != null && bag.TryGetValue(key, out var v) ? v : null;
    }

    public bool varHas(string kind, string key)
    {
        if (kind == "any") return _ctx.Vars.TryGetRaw(key, out _);
        var bag = Bag(kind);
        return bag != null && bag.ContainsKey(key);
    }

    public void varSet(string kind, string key, string value)
    {
        if (string.IsNullOrEmpty(key)) return;
        if (kind == "any") { _ctx.Vars.LocalVars[key] = value; return; }
        var bag = Bag(kind);
        if (bag == null) return;
        bag[key] = value;
        if (kind != "data")
            _ctx.VariableOps.Add(new VariableOp { Scope = kind, Key = key, Value = value, Op = "set" });
    }

    public void varUnset(string kind, string key)
    {
        if (kind == "any") { _ctx.Vars.LocalVars.Remove(key); return; }
        var bag = Bag(kind);
        if (bag == null) return;
        bag.Remove(key);
        if (kind != "data")
            _ctx.VariableOps.Add(new VariableOp { Scope = kind, Key = key, Op = "unset" });
    }

    public void varClear(string kind)
    {
        if (kind == "any") { _ctx.Vars.LocalVars.Clear(); return; }
        var bag = Bag(kind);
        if (bag == null) return;
        bag.Clear();
        if (kind != "data")
            _ctx.VariableOps.Add(new VariableOp { Scope = kind, Op = "clear" });
    }

    public string varToObject(string kind)
    {
        var bag = kind == "any" ? _ctx.Vars.Snapshot() : Bag(kind) ?? new Dictionary<string, string>();
        return JsonSerializer.Serialize(bag, JsonOpts);
    }

    #endregion

    #region request / response bridges

    public string infoJson() => JsonSerializer.Serialize(new
    {
        eventName = _phase == ScriptPhase.PreRequest ? "prerequest" : "test",
        iteration = _ctx.Iteration,
        iterationCount = _ctx.IterationCount,
        requestName = _ctx.RequestName,
        requestId = _ctx.RequestId
    }, JsonOpts);

    public string requestJson()
    {
        var r = _ctx.Request;
        return JsonSerializer.Serialize(new
        {
            method = r.Method,
            url = r.Url,
            headers = r.Headers.Select(h => new { key = h.Key, value = h.Value }).ToList(),
            body = new
            {
                mode = r.Mode.ToString().ToLowerInvariant(),
                raw = r.BodyText ?? string.Empty
            },
            auth = new { type = r.Auth?.Type.ToString().ToLowerInvariant() ?? "none" }
        }, JsonOpts);
    }

    public void applyRequest(string json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            var r = _ctx.Request;

            if (root.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String)
                r.Method = m.GetString().ToUpperInvariant();
            if (root.TryGetProperty("url", out var u) && u.ValueKind == JsonValueKind.String)
                r.Url = u.GetString();

            if (root.TryGetProperty("headers", out var hs) && hs.ValueKind == JsonValueKind.Array)
            {
                r.Headers.Clear();
                foreach (var h in hs.EnumerateArray())
                {
                    var k = h.TryGetProperty("key", out var kk) ? kk.GetString() : null;
                    var v = h.TryGetProperty("value", out var vv) ? ValueToString(vv) : string.Empty;
                    if (!string.IsNullOrWhiteSpace(k)) r.Headers.Add(new KeyValuePair<string, string>(k, v));
                }
            }

            if (root.TryGetProperty("body", out var b) && b.ValueKind == JsonValueKind.Object &&
                b.TryGetProperty("raw", out var raw) && raw.ValueKind == JsonValueKind.String)
            {
                var text = raw.GetString();
                if (text != (r.BodyText ?? string.Empty))
                {
                    r.BodyText = text;
                    r.BodyBytes = null;
                    if (r.Mode == BodyMode.None) r.Mode = BodyMode.Raw;
                }
            }
        }
        catch (Exception ex)
        {
            log("error", "Failed to apply pm.request changes: " + ex.Message);
        }
    }

    private static string ValueToString(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString(),
        JsonValueKind.Null or JsonValueKind.Undefined => string.Empty,
        _ => e.ToString()
    };

    public string responseJson()
    {
        var r = _ctx.Response;
        return JsonSerializer.Serialize(new
        {
            code = r.StatusCode,
            status = r.StatusText,
            responseTime = (int)r.ElapsedMs,
            responseSize = r.SizeBytes,
            headers = r.Headers.Select(h => new { key = h.Key, value = h.Value }).ToList(),
            body = r.BodyText ?? string.Empty
        }, JsonOpts);
    }

    public string cookieGet(string name)
    {
        var c = _ctx.Response?.Cookies?.FirstOrDefault(x => string.Equals(x.Name, name, StringComparison.OrdinalIgnoreCase));
        return c?.Value;
    }

    public string cookiesJson()
    {
        var d = new Dictionary<string, string>();
        if (_ctx.Response?.Cookies != null)
            foreach (var c in _ctx.Response.Cookies) d[c.Name] = c.Value;
        return JsonSerializer.Serialize(d, JsonOpts);
    }

    /// <summary>pm.sendRequest - executed synchronously so the callback style keeps working.</summary>
    public string sendRequest(string optionsJson)
    {
        try
        {
            using var doc = JsonDocument.Parse(optionsJson);
            var root = doc.RootElement;

            var prepared = new PreparedRequest
            {
                Method = root.TryGetProperty("method", out var m) && m.ValueKind == JsonValueKind.String
                    ? m.GetString().ToUpperInvariant() : "GET",
                Url = root.TryGetProperty("url", out var u) ? UrlFromElement(u) : string.Empty,
                Settings = new RequestSettings { VerifySsl = _ctx.App.SslVerification, TimeoutMs = 30000 }
            };

            if (root.TryGetProperty("header", out var hdr))
                ReadHeaders(hdr, prepared);
            if (root.TryGetProperty("headers", out var hdr2))
                ReadHeaders(hdr2, prepared);

            if (root.TryGetProperty("body", out var body) && body.ValueKind == JsonValueKind.Object)
                ReadBody(body, prepared);

            var resp = _ctx.Engine.SendAsync(prepared, _ctx.App, s => log("info", s), CancellationToken.None)
                                  .ConfigureAwait(false).GetAwaiter().GetResult();

            if (!string.IsNullOrEmpty(resp.Error))
                return JsonSerializer.Serialize(new { error = resp.Error }, JsonOpts);

            return JsonSerializer.Serialize(new
            {
                code = resp.StatusCode,
                status = resp.StatusText,
                responseTime = (int)resp.ElapsedMs,
                responseSize = resp.SizeBytes,
                headers = resp.Headers.Select(h => new { key = h.Key, value = h.Value }).ToList(),
                body = resp.BodyText ?? string.Empty
            }, JsonOpts);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message }, JsonOpts);
        }
    }

    private static string UrlFromElement(JsonElement u)
    {
        if (u.ValueKind == JsonValueKind.String) return u.GetString();
        if (u.ValueKind == JsonValueKind.Object && u.TryGetProperty("raw", out var raw)) return raw.GetString();
        return string.Empty;
    }

    private static void ReadHeaders(JsonElement hdr, PreparedRequest prepared)
    {
        if (hdr.ValueKind == JsonValueKind.Object)
        {
            foreach (var p in hdr.EnumerateObject())
                prepared.AddHeader(p.Name, ValueToString(p.Value));
        }
        else if (hdr.ValueKind == JsonValueKind.Array)
        {
            foreach (var h in hdr.EnumerateArray())
            {
                if (h.ValueKind != JsonValueKind.Object) continue;
                var k = h.TryGetProperty("key", out var kk) ? kk.GetString() : null;
                var v = h.TryGetProperty("value", out var vv) ? ValueToString(vv) : string.Empty;
                if (!string.IsNullOrWhiteSpace(k)) prepared.AddHeader(k, v);
            }
        }
    }

    private static void ReadBody(JsonElement body, PreparedRequest prepared)
    {
        var mode = body.TryGetProperty("mode", out var m) ? m.GetString() : "raw";
        switch (mode)
        {
            case "urlencoded":
                prepared.Mode = BodyMode.UrlEncoded;
                if (body.TryGetProperty("urlencoded", out var ue) && ue.ValueKind == JsonValueKind.Array)
                {
                    var sb = new StringBuilder();
                    foreach (var kv in ue.EnumerateArray())
                    {
                        if (sb.Length > 0) sb.Append('&');
                        sb.Append(UrlUtil.EncodeComponent(kv.GetProperty("key").GetString()));
                        sb.Append('=');
                        sb.Append(UrlUtil.EncodeComponent(kv.TryGetProperty("value", out var v) ? ValueToString(v) : string.Empty));
                    }
                    prepared.BodyText = sb.ToString();
                }
                prepared.ContentType = "application/x-www-form-urlencoded";
                break;

            case "formdata":
                prepared.Mode = BodyMode.FormData;
                if (body.TryGetProperty("formdata", out var fd) && fd.ValueKind == JsonValueKind.Array)
                    foreach (var kv in fd.EnumerateArray())
                        prepared.Multipart.Add(new MultipartEntry
                        {
                            Name = kv.GetProperty("key").GetString(),
                            Value = kv.TryGetProperty("value", out var v) ? ValueToString(v) : string.Empty,
                            IsFile = kv.TryGetProperty("type", out var t) && t.GetString() == "file",
                            FilePath = kv.TryGetProperty("src", out var s) ? s.GetString() : string.Empty
                        });
                break;

            case "graphql":
                prepared.Mode = BodyMode.Raw;
                prepared.BodyText = body.TryGetProperty("graphql", out var g) ? g.GetRawText() : "{}";
                prepared.ContentType = "application/json";
                break;

            default:
                prepared.Mode = BodyMode.Raw;
                if (body.TryGetProperty("raw", out var r))
                    prepared.BodyText = r.ValueKind == JsonValueKind.String ? r.GetString() : r.GetRawText();
                if (!prepared.HasHeader("Content-Type"))
                    prepared.ContentType = "application/json";
                break;
        }
    }

    #endregion
}
