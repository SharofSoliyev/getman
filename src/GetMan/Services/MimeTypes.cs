using System.IO;

namespace GetMan.Services;

public static class MimeTypes
{
    private static readonly Dictionary<string, string> Map = new(StringComparer.OrdinalIgnoreCase)
    {
        [".json"] = "application/json",
        [".xml"] = "application/xml",
        [".txt"] = "text/plain",
        [".csv"] = "text/csv",
        [".html"] = "text/html",
        [".htm"] = "text/html",
        [".js"] = "application/javascript",
        [".css"] = "text/css",
        [".pdf"] = "application/pdf",
        [".zip"] = "application/zip",
        [".gz"] = "application/gzip",
        [".png"] = "image/png",
        [".jpg"] = "image/jpeg",
        [".jpeg"] = "image/jpeg",
        [".gif"] = "image/gif",
        [".webp"] = "image/webp",
        [".svg"] = "image/svg+xml",
        [".ico"] = "image/x-icon",
        [".bmp"] = "image/bmp",
        [".mp3"] = "audio/mpeg",
        [".mp4"] = "video/mp4",
        [".wav"] = "audio/wav",
        [".woff"] = "font/woff",
        [".woff2"] = "font/woff2",
        [".yaml"] = "application/yaml",
        [".yml"] = "application/yaml",
        [".md"] = "text/markdown"
    };

    public static string Guess(string path)
    {
        try
        {
            var ext = Path.GetExtension(path);
            if (!string.IsNullOrEmpty(ext) && Map.TryGetValue(ext, out var mime)) return mime;
        }
        catch { }
        return "application/octet-stream";
    }

    public static string ExtensionFor(string contentType)
    {
        if (string.IsNullOrWhiteSpace(contentType)) return ".bin";
        var ct = contentType.Split(';')[0].Trim();
        foreach (var kv in Map)
            if (string.Equals(kv.Value, ct, StringComparison.OrdinalIgnoreCase)) return kv.Key;
        return ".bin";
    }

    public static bool IsImage(string contentType) =>
        !string.IsNullOrEmpty(contentType) && contentType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);

    public static bool IsText(string contentType)
    {
        if (string.IsNullOrEmpty(contentType)) return true;
        var ct = contentType.ToLowerInvariant();
        return ct.StartsWith("text/") || ct.Contains("json") || ct.Contains("xml") || ct.Contains("javascript")
            || ct.Contains("yaml") || ct.Contains("x-www-form-urlencoded") || ct.Contains("csv") || ct.Contains("graphql");
    }
}
