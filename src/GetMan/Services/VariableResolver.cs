using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using GetMan.Models;

namespace GetMan.Services;

/// <summary>
/// Layered {{variable}} substitution with Postman-compatible precedence:
/// local (data/iteration) &gt; environment &gt; collection &gt; global, plus {{$dynamic}} generators.
/// </summary>
public class VariableResolver
{
    private static readonly Regex Token = new(@"\{\{\s*([^{}]+?)\s*\}\}", RegexOptions.Compiled);
    private const int MaxDepth = 12;

    public Dictionary<string, string> Globals { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> CollectionVars { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> EnvironmentVars { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> LocalVars { get; } = new(StringComparer.Ordinal);
    public Dictionary<string, string> DataVars { get; } = new(StringComparer.Ordinal);

    private readonly Random _rnd = new();

    public void LoadEnvironment(EnvironmentModel env)
    {
        EnvironmentVars.Clear();
        if (env == null) return;
        foreach (var v in env.Variables.Where(v => v.Enabled && !string.IsNullOrWhiteSpace(v.Key)))
            EnvironmentVars[v.Key] = v.Value;
    }

    public void LoadGlobals(EnvironmentModel env)
    {
        Globals.Clear();
        if (env == null) return;
        foreach (var v in env.Variables.Where(v => v.Enabled && !string.IsNullOrWhiteSpace(v.Key)))
            Globals[v.Key] = v.Value;
    }

    public void LoadCollectionChain(CollectionNode node)
    {
        CollectionVars.Clear();
        if (node == null) return;
        // Outermost first so nearer scopes win.
        foreach (var n in node.AncestorsAndSelf().Reverse())
            foreach (var v in n.Variables.Where(v => v.Enabled && !string.IsNullOrWhiteSpace(v.Key)))
                CollectionVars[v.Key] = v.Value;
    }

    public bool TryGetRaw(string name, out string value)
    {
        if (LocalVars.TryGetValue(name, out value)) return true;
        if (DataVars.TryGetValue(name, out value)) return true;
        if (EnvironmentVars.TryGetValue(name, out value)) return true;
        if (CollectionVars.TryGetValue(name, out value)) return true;
        if (Globals.TryGetValue(name, out value)) return true;
        value = null;
        return false;
    }

    public string Resolve(string input) => Resolve(input, 0);

    private string Resolve(string input, int depth)
    {
        if (string.IsNullOrEmpty(input) || depth > MaxDepth) return input ?? string.Empty;
        if (input.IndexOf("{{", StringComparison.Ordinal) < 0) return input;

        var result = Token.Replace(input, m =>
        {
            var name = m.Groups[1].Value;
            if (name.StartsWith("$", StringComparison.Ordinal))
                return Dynamic(name);
            if (TryGetRaw(name, out var v))
                return v ?? string.Empty;
            return m.Value; // leave unresolved token untouched, like Postman
        });

        return result == input ? result : Resolve(result, depth + 1);
    }

    /// <summary>Names of tokens present in the text that resolve to nothing.</summary>
    public IEnumerable<string> FindUnresolved(string input)
    {
        if (string.IsNullOrEmpty(input)) yield break;
        foreach (Match m in Token.Matches(input))
        {
            var name = m.Groups[1].Value;
            if (name.StartsWith("$", StringComparison.Ordinal)) continue;
            if (!TryGetRaw(name, out _)) yield return name;
        }
    }

    public Dictionary<string, string> Snapshot()
    {
        var d = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var kv in Globals) d[kv.Key] = kv.Value;
        foreach (var kv in CollectionVars) d[kv.Key] = kv.Value;
        foreach (var kv in EnvironmentVars) d[kv.Key] = kv.Value;
        foreach (var kv in DataVars) d[kv.Key] = kv.Value;
        foreach (var kv in LocalVars) d[kv.Key] = kv.Value;
        return d;
    }

    #region dynamic variables

    private static readonly string[] FirstNames = { "Ada", "Liam", "Olivia", "Noah", "Emma", "Oliver", "Ava", "Elijah", "Sophia", "Lucas", "Isabella", "Mason", "Mia", "Ethan", "Amelia", "Aziz", "Dilnoza", "Jasur", "Malika", "Timur" };
    private static readonly string[] LastNames = { "Lovelace", "Smith", "Johnson", "Williams", "Brown", "Jones", "Garcia", "Miller", "Davis", "Rodriguez", "Karimov", "Yusupova", "Alimov", "Nazarov" };
    private static readonly string[] Cities = { "Tashkent", "Samarkand", "Berlin", "Tokyo", "Lisbon", "Toronto", "Nairobi", "Bogota", "Oslo", "Seoul", "Austin", "Dublin" };
    private static readonly string[] Countries = { "Uzbekistan", "Germany", "Japan", "Portugal", "Canada", "Kenya", "Colombia", "Norway", "South Korea", "Ireland" };
    private static readonly string[] Words = { "lorem", "ipsum", "dolor", "sit", "amet", "consectetur", "adipiscing", "elit", "sed", "tempor", "magna", "aliqua", "veniam", "nostrud", "commodo" };
    private static readonly string[] Colors = { "red", "green", "blue", "cyan", "magenta", "yellow", "black", "white", "teal", "indigo", "olive" };
    private static readonly string[] Domains = { "example.com", "mail.dev", "corp.io", "fastmail.net", "testing.org" };
    private static readonly string[] UserAgents =
    {
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/125.0 Safari/537.36",
        "Mozilla/5.0 (Macintosh; Intel Mac OS X 14_5) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.5 Safari/605.1.15",
        "Mozilla/5.0 (X11; Linux x86_64; rv:127.0) Gecko/20100101 Firefox/127.0"
    };
    private static readonly string[] Companies = { "Contoso", "Globex", "Initech", "Umbrella", "Soylent", "Hooli", "Vehement", "Acme" };
    private static readonly string[] JobTitles = { "Backend Engineer", "Product Manager", "QA Lead", "Designer", "Data Analyst", "DevOps Engineer", "CTO" };
    private static readonly string[] Months = { "January", "February", "March", "April", "May", "June", "July", "August", "September", "October", "November", "December" };
    private static readonly string[] Weekdays = { "Monday", "Tuesday", "Wednesday", "Thursday", "Friday", "Saturday", "Sunday" };
    private static readonly string[] CountryCodes = { "UZ", "DE", "JP", "PT", "CA", "KE", "CO", "NO", "KR", "IE" };
    private static readonly string[] FileExts = { "json", "xml", "csv", "png", "pdf", "txt" };
    private static readonly string[] MimeTypes = { "application/json", "text/plain", "image/png", "application/pdf" };

    /// <summary>
    /// The generators the variable picker offers, in the spelling Postman uses. Every name here
    /// has to be one <see cref="Dynamic"/> answers - the self-test proves it, because a name that
    /// falls through returns the token itself and would look like a working choice in the list.
    /// </summary>
    public static readonly string[] DynamicNames =
    {
        "$guid", "$uuid", "$randomUUID", "$timestamp", "$epoch", "$isoTimestamp",
        "$randomInt", "$randomBoolean", "$randomAlphaNumeric", "$randomPassword",
        "$randomFirstName", "$randomLastName", "$randomFullName", "$randomUserName",
        "$randomEmail", "$randomExampleEmail", "$randomCity", "$randomCountry",
        "$randomCountryCode", "$randomStreetAddress", "$randomPhoneNumber", "$randomIP",
        "$randomIPV6", "$randomUserAgent", "$randomUrl", "$randomDomainName",
        "$randomProtocol", "$randomColor", "$randomHexColor", "$randomWord", "$randomWords",
        "$randomLoremSentence", "$randomLoremParagraph", "$randomCompanyName",
        "$randomJobTitle", "$randomMonth", "$randomWeekday", "$randomPrice",
        "$randomBankAccount", "$randomDatePast", "$randomDateFuture", "$randomDateRecent",
        "$randomFileExt", "$randomMimeType"
    };

    private string Pick(string[] arr) => arr[_rnd.Next(arr.Length)];

    public string Dynamic(string token)
    {
        // Supports {{$randomInt}} and the parameterised {{$randomInt(1,100)}} form.
        string name = token;
        string[] args = Array.Empty<string>();
        var p = token.IndexOf('(');
        if (p > 0 && token.EndsWith(")", StringComparison.Ordinal))
        {
            name = token.Substring(0, p);
            var inner = token.Substring(p + 1, token.Length - p - 2);
            args = inner.Length == 0
                ? Array.Empty<string>()
                : inner.Split(',').Select(s => s.Trim().Trim('\'', '"')).ToArray();
        }

        switch (name.ToLowerInvariant())
        {
            case "$guid":
            case "$uuid":
            case "$randomuuid": return Guid.NewGuid().ToString();
            case "$timestamp": return DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture);
            case "$epoch": return DateTimeOffset.UtcNow.ToUnixTimeMilliseconds().ToString(CultureInfo.InvariantCulture);
            case "$isotimestamp": return DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            case "$randomint":
                {
                    int lo = args.Length > 0 && int.TryParse(args[0], out var a) ? a : 0;
                    int hi = args.Length > 1 && int.TryParse(args[1], out var b) ? b : 1000;
                    if (hi < lo) { var t = lo; lo = hi; hi = t; }
                    return _rnd.Next(lo, hi + 1).ToString(CultureInfo.InvariantCulture);
                }
            case "$randomboolean": return _rnd.Next(2) == 1 ? "true" : "false";
            case "$randomalphanumeric": return RandomString(1, "abcdefghijklmnopqrstuvwxyz0123456789");
            case "$randompassword": return RandomString(12, "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789!@#%");
            case "$randomfirstname": return Pick(FirstNames);
            case "$randomlastname": return Pick(LastNames);
            case "$randomfullname": return Pick(FirstNames) + " " + Pick(LastNames);
            case "$randomusername": return Pick(FirstNames).ToLowerInvariant() + _rnd.Next(10, 9999);
            case "$randomemail": return Pick(FirstNames).ToLowerInvariant() + "." + Pick(LastNames).ToLowerInvariant() + "@" + Pick(Domains);
            case "$randomexampleemail": return Pick(FirstNames).ToLowerInvariant() + "@example.com";
            case "$randomcity": return Pick(Cities);
            case "$randomcountry": return Pick(Countries);
            case "$randomcountrycode": return Pick(CountryCodes);
            case "$randomstreetaddress": return _rnd.Next(1, 999) + " " + Pick(LastNames) + " St";
            case "$randomphonenumber": return "+" + _rnd.Next(1, 99) + "-" + _rnd.Next(100, 999) + "-" + _rnd.Next(100, 999) + "-" + _rnd.Next(1000, 9999);
            case "$randomip": return _rnd.Next(1, 255) + "." + _rnd.Next(0, 255) + "." + _rnd.Next(0, 255) + "." + _rnd.Next(1, 254);
            case "$randomipv6": return string.Join(":", Enumerable.Range(0, 8).Select(_ => _rnd.Next(0, 65535).ToString("x4")));
            case "$randomuseragent": return Pick(UserAgents);
            case "$randomurl": return "https://" + Pick(Domains) + "/" + Pick(Words);
            case "$randomdomainname": return Pick(Domains);
            case "$randomprotocol": return _rnd.Next(2) == 0 ? "http" : "https";
            case "$randomcolor": return Pick(Colors);
            case "$randomhexcolor": return "#" + _rnd.Next(0, 0xFFFFFF).ToString("x6");
            case "$randomword": return Pick(Words);
            case "$randomwords": return string.Join(" ", Enumerable.Range(0, _rnd.Next(2, 6)).Select(_ => Pick(Words)));
            case "$randomloremsentence":
                {
                    var w = Enumerable.Range(0, _rnd.Next(6, 12)).Select(_ => Pick(Words)).ToList();
                    var s = string.Join(" ", w);
                    return char.ToUpperInvariant(s[0]) + s.Substring(1) + ".";
                }
            case "$randomloremparagraph":
                return string.Join(" ", Enumerable.Range(0, 4).Select(_ => Dynamic("$randomLoremSentence")));
            case "$randomcompanyname": return Pick(Companies);
            case "$randomjobtitle": return Pick(JobTitles);
            case "$randommonth": return Pick(Months);
            case "$randomweekday": return Pick(Weekdays);
            case "$randomprice": return (_rnd.Next(100, 100000) / 100.0).ToString("F2", CultureInfo.InvariantCulture);
            case "$randombankaccount": return _rnd.NextInt64(10000000, 99999999).ToString(CultureInfo.InvariantCulture);
            case "$randomdatepast": return DateTime.UtcNow.AddDays(-_rnd.Next(1, 3650)).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            case "$randomdatefuture": return DateTime.UtcNow.AddDays(_rnd.Next(1, 3650)).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            case "$randomdaterecent": return DateTime.UtcNow.AddHours(-_rnd.Next(1, 72)).ToString("yyyy-MM-ddTHH:mm:ss.fffZ", CultureInfo.InvariantCulture);
            case "$randomfileext": return Pick(FileExts);
            case "$randommimetype": return Pick(MimeTypes);
            default: return "{{" + token + "}}";
        }
    }

    private string RandomString(int len, string alphabet)
    {
        var sb = new StringBuilder(len);
        for (int i = 0; i < len; i++) sb.Append(alphabet[_rnd.Next(alphabet.Length)]);
        return sb.ToString();
    }

    #endregion
}
