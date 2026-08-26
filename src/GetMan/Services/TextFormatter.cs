using System.Text;
using System.Text.Json;
using System.Xml;
using System.Xml.Linq;

namespace GetMan.Services;

public static class TextFormatter
{
    public static string DetectLanguage(string contentType, string body)
    {
        var ct = (contentType ?? string.Empty).ToLowerInvariant();
        if (ct.Contains("json")) return "json";
        if (ct.Contains("xml")) return "xml";
        if (ct.Contains("html")) return "html";
        if (ct.Contains("javascript")) return "javascript";
        if (ct.Contains("css")) return "css";

        var trimmed = (body ?? string.Empty).TrimStart();
        if (trimmed.StartsWith("{") || trimmed.StartsWith("[")) return "json";
        if (trimmed.StartsWith("<?xml")) return "xml";
        if (trimmed.StartsWith("<!DOCTYPE html", StringComparison.OrdinalIgnoreCase) ||
            trimmed.StartsWith("<html", StringComparison.OrdinalIgnoreCase)) return "html";
        if (trimmed.StartsWith("<")) return "xml";
        return "text";
    }

    public static string Pretty(string text, string language)
    {
        if (string.IsNullOrWhiteSpace(text)) return text ?? string.Empty;
        try
        {
            switch (language)
            {
                case "json": return PrettyJson(text);
                case "xml":
                case "html": return PrettyXml(text);
                default: return text;
            }
        }
        catch
        {
            return text;
        }
    }

    public static string PrettyJson(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions
        {
            AllowTrailingCommas = true,
            CommentHandling = JsonCommentHandling.Skip,
            MaxDepth = 512
        });
        return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
        {
            WriteIndented = true,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    public static string MinifyJson(string json)
    {
        using var doc = JsonDocument.Parse(json, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
        return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions
        {
            WriteIndented = false,
            Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
        });
    }

    public static string PrettyXml(string xml)
    {
        var settings = new XmlWriterSettings
        {
            Indent = true,
            IndentChars = "  ",
            OmitXmlDeclaration = !xml.TrimStart().StartsWith("<?xml", StringComparison.OrdinalIgnoreCase),
            NewLineOnAttributes = false
        };
        var doc = XDocument.Parse(xml, LoadOptions.PreserveWhitespace);
        var sb = new StringBuilder();
        using (var writer = XmlWriter.Create(sb, settings))
            doc.Save(writer);
        return sb.ToString();
    }

    public static bool IsValidJson(string text, out string error)
    {
        error = null;
        if (string.IsNullOrWhiteSpace(text)) return true;
        try
        {
            using var _ = JsonDocument.Parse(text, new JsonDocumentOptions { AllowTrailingCommas = true, CommentHandling = JsonCommentHandling.Skip });
            return true;
        }
        catch (JsonException ex)
        {
            error = ex.Message;
            return false;
        }
    }

    public static string HumanSize(long bytes)
    {
        if (bytes < 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        double size = bytes;
        int i = 0;
        while (size >= 1024 && i < units.Length - 1) { size /= 1024; i++; }
        return i == 0 ? $"{bytes} B" : $"{size:0.##} {units[i]}";
    }

    public static string HumanTime(double ms)
    {
        if (ms < 1000) return $"{ms:0} ms";
        if (ms < 60000) return $"{ms / 1000:0.00} s";
        return $"{ms / 60000:0.0} min";
    }
}
