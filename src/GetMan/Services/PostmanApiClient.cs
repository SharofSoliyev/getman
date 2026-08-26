using System.Net.Http;
using System.Text.Json;

namespace GetMan.Services;

public class PostmanRemoteItem
{
    public string Uid { get; set; } = string.Empty;
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Owner { get; set; } = string.Empty;
    public string Workspace { get; set; } = string.Empty;
    /// <summary>collection | environment</summary>
    public string Kind { get; set; } = "collection";
    public bool Selected { get; set; } = true;
}

/// <summary>
/// Talks to the official Postman API (api.getpostman.com) with a personal API key.
/// Since Postman 10 the desktop app is cloud backed, so this returns exactly the
/// collections and environments the installed app shows.
/// </summary>
public class PostmanApiClient : IDisposable
{
    private readonly HttpClient _http;

    public PostmanApiClient(string apiKey)
    {
        _http = new HttpClient { BaseAddress = new Uri("https://api.getpostman.com/"), Timeout = TimeSpan.FromSeconds(60) };
        _http.DefaultRequestHeaders.TryAddWithoutValidation("X-Api-Key", apiKey?.Trim() ?? string.Empty);
        _http.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/json");
        _http.DefaultRequestHeaders.TryAddWithoutValidation("User-Agent", "GetMan/1.0");
    }

    private async Task<JsonDocument> GetAsync(string path, CancellationToken ct)
    {
        using var response = await _http.GetAsync(path, ct).ConfigureAwait(false);
        var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);

        if (!response.IsSuccessStatusCode)
        {
            string message = $"{(int)response.StatusCode} {response.ReasonPhrase}";
            try
            {
                using var err = JsonDocument.Parse(body);
                if (err.RootElement.TryGetProperty("error", out var e) && e.TryGetProperty("message", out var m))
                    message = m.GetString() ?? message;
            }
            catch { }
            throw new InvalidOperationException(message);
        }

        return JsonDocument.Parse(body);
    }

    public async Task<List<PostmanRemoteItem>> ListAsync(CancellationToken ct = default)
    {
        var result = new List<PostmanRemoteItem>();

        using (var doc = await GetAsync("collections", ct).ConfigureAwait(false))
        {
            if (doc.RootElement.TryGetProperty("collections", out var cols))
                foreach (var c in cols.EnumerateArray())
                    result.Add(new PostmanRemoteItem
                    {
                        Kind = "collection",
                        Uid = Str(c, "uid"),
                        Id = Str(c, "id"),
                        Name = Str(c, "name"),
                        Owner = Str(c, "owner")
                    });
        }

        try
        {
            using var doc = await GetAsync("environments", ct).ConfigureAwait(false);
            if (doc.RootElement.TryGetProperty("environments", out var envs))
                foreach (var e in envs.EnumerateArray())
                    result.Add(new PostmanRemoteItem
                    {
                        Kind = "environment",
                        Uid = Str(e, "uid"),
                        Id = Str(e, "id"),
                        Name = Str(e, "name"),
                        Owner = Str(e, "owner")
                    });
        }
        catch
        {
            // A key scoped to collections only still gives us the collections.
        }

        return result;
    }

    /// <summary>Returns the raw Postman JSON for one item, ready for <see cref="PostmanImporter"/>.</summary>
    public async Task<string> DownloadAsync(PostmanRemoteItem item, CancellationToken ct = default)
    {
        var path = item.Kind == "environment" ? "environments/" + item.Uid : "collections/" + item.Uid;
        using var doc = await GetAsync(path, ct).ConfigureAwait(false);

        var wrapper = item.Kind == "environment" ? "environment" : "collection";
        if (doc.RootElement.TryGetProperty(wrapper, out var inner))
            return inner.GetRawText();
        return doc.RootElement.GetRawText();
    }

    private static string Str(JsonElement el, string name) =>
        el.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? string.Empty : string.Empty;

    public void Dispose() => _http.Dispose();
}
