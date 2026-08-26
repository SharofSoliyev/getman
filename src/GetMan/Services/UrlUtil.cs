using System.Text;
using System.Text.RegularExpressions;
using GetMan.Models;

namespace GetMan.Services;

public static class UrlUtil
{
    private static readonly Regex PathVar = new(@"(?<=/):([A-Za-z0-9_\-]+)", RegexOptions.Compiled);

    public static string SplitBase(string url)
    {
        var i = url.IndexOf('?');
        return i < 0 ? url : url.Substring(0, i);
    }

    public static string SplitQuery(string url)
    {
        var i = url.IndexOf('?');
        if (i < 0) return string.Empty;
        var q = url.Substring(i + 1);
        var h = q.IndexOf('#');
        return h < 0 ? q : q.Substring(0, h);
    }

    /// <summary>Parse a query string into rows, preserving order, blanks and duplicates.</summary>
    public static List<KeyValueItem> ParseQuery(string query)
    {
        var list = new List<KeyValueItem>();
        if (string.IsNullOrEmpty(query)) return list;
        foreach (var part in query.Split('&'))
        {
            if (part.Length == 0) continue;
            var eq = part.IndexOf('=');
            var k = eq < 0 ? part : part.Substring(0, eq);
            var v = eq < 0 ? string.Empty : part.Substring(eq + 1);
            list.Add(new KeyValueItem(Decode(k), Decode(v)));
        }
        return list;
    }

    private static string Decode(string s)
    {
        try { return Uri.UnescapeDataString(s.Replace("+", "%20")); }
        catch { return s; }
    }

    public static string BuildQuery(IEnumerable<KeyValueItem> items, Func<string, string> resolve, bool encode)
    {
        var sb = new StringBuilder();
        foreach (var p in items)
        {
            if (!p.Enabled || string.IsNullOrWhiteSpace(p.Key)) continue;
            if (sb.Length > 0) sb.Append('&');
            var k = resolve(p.Key);
            var v = resolve(p.Value ?? string.Empty);
            sb.Append(encode ? EncodeComponent(k) : k);
            sb.Append('=');
            sb.Append(encode ? EncodeComponent(v) : v);
        }
        return sb.ToString();
    }

    /// <summary>Escapes only characters that are unsafe in a query component; leaves already-percent-encoded triplets alone.</summary>
    public static string EncodeComponent(string s)
    {
        if (string.IsNullOrEmpty(s)) return string.Empty;
        var sb = new StringBuilder(s.Length + 8);
        for (int i = 0; i < s.Length; i++)
        {
            char c = s[i];
            if (c == '%' && i + 2 < s.Length && Uri.IsHexDigit(s[i + 1]) && Uri.IsHexDigit(s[i + 2]))
            {
                sb.Append(s, i, 3);
                i += 2;
                continue;
            }
            if (char.IsLetterOrDigit(c) || c == '-' || c == '_' || c == '.' || c == '~')
                sb.Append(c);
            else
                foreach (var b in Encoding.UTF8.GetBytes(c.ToString()))
                    sb.Append('%').Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    public static string ApplyPathVariables(string url, IEnumerable<KeyValueItem> vars, Func<string, string> resolve)
    {
        if (vars == null) return url;
        var map = vars.Where(v => !string.IsNullOrWhiteSpace(v.Key))
                      .GroupBy(v => v.Key).ToDictionary(g => g.Key, g => resolve(g.First().Value ?? string.Empty));
        if (map.Count == 0) return url;
        return PathVar.Replace(url, m => map.TryGetValue(m.Groups[1].Value, out var val) && !string.IsNullOrEmpty(val)
            ? val
            : m.Value);
    }

    public static IEnumerable<string> ExtractPathVariableNames(string url)
    {
        var basePart = SplitBase(url ?? string.Empty);
        foreach (Match m in PathVar.Matches(basePart))
            yield return m.Groups[1].Value;
    }

    public static string EnsureScheme(string url)
    {
        if (string.IsNullOrWhiteSpace(url)) return url;
        url = url.Trim();
        if (url.StartsWith("//", StringComparison.Ordinal)) return "http:" + url;
        if (Regex.IsMatch(url, @"^[a-zA-Z][a-zA-Z0-9+.\-]*://")) return url;
        if (url.StartsWith("localhost", StringComparison.OrdinalIgnoreCase)) return "http://" + url;
        return "http://" + url;
    }

    public static string ComposeUrl(string baseUrl, string query)
    {
        if (string.IsNullOrEmpty(query)) return baseUrl;
        return baseUrl + "?" + query;
    }
}
