using System.Text;
using GetMan.Models;

namespace GetMan.Services;

/// <summary>Parses a cURL command line into a request (handles bash and PowerShell style quoting).</summary>
public static class CurlImporter
{
    public static CollectionNode Parse(string command)
    {
        var tokens = Tokenize(command);
        if (tokens.Count == 0 || !tokens[0].StartsWith("curl", StringComparison.OrdinalIgnoreCase))
            tokens.Insert(0, "curl");

        var req = new RequestModel();
        var node = new CollectionNode { Kind = NodeKind.Request, Name = "Imported request", Request = req };
        req.Auth.Type = AuthType.None;

        string url = null;
        var formFields = new List<KeyValueItem>();
        var urlEncodedParts = new List<string>();
        bool methodExplicit = false;
        bool isMultipart = false;

        for (int i = 1; i < tokens.Count; i++)
        {
            var t = tokens[i];
            string Next() => i + 1 < tokens.Count ? tokens[++i] : string.Empty;

            switch (t)
            {
                case "-X":
                case "--request":
                    req.Method = Next().ToUpperInvariant();
                    methodExplicit = true;
                    break;

                case "-H":
                case "--header":
                    {
                        var h = Next();
                        var idx = h.IndexOf(':');
                        if (idx > 0)
                            req.Headers.Add(new KeyValueItem(h.Substring(0, idx).Trim(), h.Substring(idx + 1).Trim()));
                        break;
                    }

                case "-A":
                case "--user-agent":
                    req.Headers.Add(new KeyValueItem("User-Agent", Next()));
                    break;

                case "-e":
                case "--referer":
                    req.Headers.Add(new KeyValueItem("Referer", Next()));
                    break;

                case "-b":
                case "--cookie":
                    req.Headers.Add(new KeyValueItem("Cookie", Next()));
                    break;

                case "-d":
                case "--data":
                case "--data-raw":
                case "--data-binary":
                case "--data-ascii":
                    {
                        var d = Next();
                        urlEncodedParts.Add(d);
                        break;
                    }

                case "--data-urlencode":
                    urlEncodedParts.Add(Next());
                    break;

                case "--json":
                    {
                        req.Body.Mode = BodyMode.Raw;
                        req.Body.RawLanguage = "json";
                        req.Body.Raw = Next();
                        if (!req.Headers.Any(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase)))
                            req.Headers.Add(new KeyValueItem("Content-Type", "application/json"));
                        break;
                    }

                case "-F":
                case "--form":
                case "--form-string":
                    {
                        isMultipart = true;
                        var f = Next();
                        var eq = f.IndexOf('=');
                        if (eq <= 0) break;
                        var key = f.Substring(0, eq);
                        var val = f.Substring(eq + 1);
                        if (val.StartsWith("@") || val.StartsWith("<"))
                            formFields.Add(new KeyValueItem { Key = key, Kind = ParamKind.File, FilePath = val.Substring(1).Split(';')[0] });
                        else
                            formFields.Add(new KeyValueItem(key, val.Split(';')[0]));
                        break;
                    }

                case "-u":
                case "--user":
                    {
                        var cred = Next();
                        var c = cred.IndexOf(':');
                        req.Auth.Type = AuthType.Basic;
                        req.Auth.Username = c < 0 ? cred : cred.Substring(0, c);
                        req.Auth.Password = c < 0 ? string.Empty : cred.Substring(c + 1);
                        break;
                    }

                case "-k":
                case "--insecure":
                    req.Settings.VerifySsl = false;
                    break;

                case "-L":
                case "--location":
                    req.Settings.FollowRedirects = true;
                    break;

                case "-I":
                case "--head":
                    req.Method = "HEAD";
                    methodExplicit = true;
                    break;

                case "-G":
                case "--get":
                    req.Method = "GET";
                    methodExplicit = true;
                    break;

                case "-x":
                case "--proxy":
                    Next();
                    break;

                case "--url":
                    url = Next();
                    break;

                case "--compressed":
                case "-s":
                case "--silent":
                case "-v":
                case "--verbose":
                case "-i":
                case "--include":
                case "-S":
                case "--show-error":
                case "-f":
                case "--fail":
                case "--no-buffer":
                    break;

                case "-m":
                case "--max-time":
                    if (double.TryParse(Next(), out var secs)) req.Settings.TimeoutMs = (int)(secs * 1000);
                    break;

                case "-o":
                case "--output":
                case "-w":
                case "--write-out":
                case "--connect-timeout":
                case "--retry":
                    Next();
                    break;

                default:
                    if (!t.StartsWith("-") && url == null) url = t;
                    break;
            }
        }

        if (isMultipart)
        {
            req.Body.Mode = BodyMode.FormData;
            foreach (var f in formFields) req.Body.FormData.Add(f);
        }
        else if (urlEncodedParts.Count > 0)
        {
            var joined = string.Join("&", urlEncodedParts);
            var ct = req.Headers.FirstOrDefault(h => h.Key.Equals("Content-Type", StringComparison.OrdinalIgnoreCase))?.Value ?? string.Empty;
            if (ct.Contains("x-www-form-urlencoded", StringComparison.OrdinalIgnoreCase))
            {
                req.Body.Mode = BodyMode.UrlEncoded;
                foreach (var kv in UrlUtil.ParseQuery(joined)) req.Body.UrlEncoded.Add(kv);
            }
            else if (req.Body.Mode != BodyMode.Raw)
            {
                req.Body.Mode = BodyMode.Raw;
                req.Body.Raw = joined;
                req.Body.RawLanguage = joined.TrimStart().StartsWith("{") || joined.TrimStart().StartsWith("[") ? "json" : "text";
            }
        }

        if (!methodExplicit && (req.Body.Mode != BodyMode.None))
            req.Method = "POST";

        req.Url = url ?? string.Empty;
        foreach (var p in UrlUtil.ParseQuery(UrlUtil.SplitQuery(req.Url)))
            req.QueryParams.Add(p);

        try
        {
            var uri = new Uri(UrlUtil.EnsureScheme(UrlUtil.SplitBase(req.Url)));
            node.Name = string.IsNullOrEmpty(uri.AbsolutePath.Trim('/')) ? uri.Host : uri.AbsolutePath.Trim('/');
        }
        catch { }

        return node;
    }

    private static List<string> Tokenize(string input)
    {
        var tokens = new List<string>();
        if (string.IsNullOrWhiteSpace(input)) return tokens;

        // Strip line continuations used when copying multi-line curl commands.
        input = input.Replace("\\\r\n", " ").Replace("\\\n", " ").Replace("^\r\n", " ").Replace("`\r\n", " ").Replace("`\n", " ");

        var sb = new StringBuilder();
        char quote = '\0';
        bool has = false;

        for (int i = 0; i < input.Length; i++)
        {
            char c = input[i];

            if (quote != '\0')
            {
                if (c == '\\' && quote == '"' && i + 1 < input.Length)
                {
                    var n = input[i + 1];
                    if (n == '"' || n == '\\' || n == '$' || n == '`') { sb.Append(n); i++; continue; }
                    sb.Append(c);
                    continue;
                }
                if (c == quote) { quote = '\0'; continue; }
                sb.Append(c);
                continue;
            }

            if (c == '\'' || c == '"') { quote = c; has = true; continue; }
            if (char.IsWhiteSpace(c))
            {
                if (sb.Length > 0 || has) { tokens.Add(sb.ToString()); sb.Clear(); has = false; }
                continue;
            }
            if (c == '\\' && i + 1 < input.Length && (input[i + 1] == '\n' || input[i + 1] == '\r')) continue;
            sb.Append(c);
        }
        if (sb.Length > 0 || has) tokens.Add(sb.ToString());

        return tokens.Where(t => t.Length > 0 || true).ToList();
    }
}
