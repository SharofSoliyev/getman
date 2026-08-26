using System.Text;
using GetMan.Models;

namespace GetMan.Services;

public class MultipartEntry
{
    public string Name { get; set; } = string.Empty;
    public string Value { get; set; } = string.Empty;
    public string FilePath { get; set; } = string.Empty;
    public string ContentType { get; set; } = string.Empty;
    public bool IsFile { get; set; }
}

/// <summary>
/// A fully variable-resolved request, ready for the wire. Scripts mutate this object
/// (pm.request.*) between preparation and send.
/// </summary>
public class PreparedRequest
{
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = string.Empty;
    public List<KeyValuePair<string, string>> Headers { get; set; } = new();
    public BodyMode Mode { get; set; } = BodyMode.None;
    public string BodyText { get; set; } = string.Empty;
    public byte[] BodyBytes { get; set; }
    public string ContentType { get; set; } = string.Empty;
    public string BinaryPath { get; set; } = string.Empty;
    public List<MultipartEntry> Multipart { get; set; } = new();
    public AuthConfig Auth { get; set; } = new() { Type = AuthType.None };
    public RequestSettings Settings { get; set; } = new();

    public string Name { get; set; } = string.Empty;

    public bool HasHeader(string name) =>
        Headers.Any(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase));

    public string GetHeader(string name) =>
        Headers.FirstOrDefault(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase)).Value;

    public void SetHeader(string name, string value)
    {
        RemoveHeader(name);
        Headers.Add(new KeyValuePair<string, string>(name, value));
    }

    public void AddHeader(string name, string value) =>
        Headers.Add(new KeyValuePair<string, string>(name, value));

    public void RemoveHeader(string name) =>
        Headers.RemoveAll(h => string.Equals(h.Key, name, StringComparison.OrdinalIgnoreCase));

    public byte[] MaterializeBody()
    {
        if (BodyBytes != null) return BodyBytes;
        if (string.IsNullOrEmpty(BodyText)) return Array.Empty<byte>();
        return Encoding.UTF8.GetBytes(BodyText);
    }

    /// <summary>Human readable dump used by the console pane.</summary>
    public string Dump()
    {
        var sb = new StringBuilder();
        Uri uri = null;
        Uri.TryCreate(Url, UriKind.Absolute, out uri);
        sb.AppendLine($"{Method} {(uri != null ? uri.PathAndQuery : Url)} HTTP/1.1");
        if (uri != null) sb.AppendLine($"Host: {uri.Host}{(uri.IsDefaultPort ? "" : ":" + uri.Port)}");
        foreach (var h in Headers) sb.AppendLine($"{h.Key}: {h.Value}");
        if (!string.IsNullOrEmpty(ContentType) && !HasHeader("Content-Type"))
            sb.AppendLine($"Content-Type: {ContentType}");
        sb.AppendLine();
        if (Mode == BodyMode.FormData)
            foreach (var m in Multipart)
                sb.AppendLine(m.IsFile ? $"  {m.Name} = <file> {m.FilePath}" : $"  {m.Name} = {m.Value}");
        else if (Mode == BodyMode.Binary)
            sb.AppendLine($"<binary file> {BinaryPath}");
        else if (!string.IsNullOrEmpty(BodyText))
            sb.AppendLine(BodyText.Length > 20000 ? BodyText.Substring(0, 20000) + "\n... (truncated)" : BodyText);
        return sb.ToString();
    }
}
