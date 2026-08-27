using System.IO;
using System.Text.Json;
using Microsoft.Win32;

namespace GetMan.Services;

public class PostmanInstall
{
    public bool Installed { get; set; }
    public string Version { get; set; } = string.Empty;
    public string InstallPath { get; set; } = string.Empty;
    public string DataPath { get; set; } = string.Empty;
    public bool HasLocalDatabase { get; set; }
}

public class DiscoveredFile
{
    public string Path { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    /// <summary>collection | environment | globals | dump | unknown</summary>
    public string Kind { get; set; } = "unknown";
    public string Title { get; set; } = string.Empty;
    public long Size { get; set; }
    public DateTime Modified { get; set; }
    public int RequestCount { get; set; }
    public bool Selected { get; set; } = true;

    public string SizeText => TextFormatter.HumanSize(Size);
    public string ModifiedText => Modified.ToString("yyyy-MM-dd HH:mm");
}

/// <summary>
/// Finds a local Postman installation and any Postman export files lying around on the machine.
///
/// Postman 10+ keeps its own collections inside a Chromium IndexedDB store
/// (LevelDB + snappy + V8 structured clone). That is an undocumented, version specific binary
/// format and the files are locked while Postman runs, so GetMan does not try to parse it.
/// The two dependable routes are export files on disk and the Postman cloud API.
/// </summary>
public static class PostmanDiscovery
{
    public static PostmanInstall Detect()
    {
        var info = new PostmanInstall
        {
            DataPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Postman")
        };

        // The uninstall registry is the only place that reports a version. There is no registry
        // off Windows, and the CLI runs there, so the lookup is skipped rather than attempted.
        if (OperatingSystem.IsWindows())
        {
            foreach (var root in new[] { Registry.LocalMachine, Registry.CurrentUser })
            {
                try
                {
                    using var key = root.OpenSubKey(@"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall");
                    if (key == null) continue;
                    foreach (var sub in key.GetSubKeyNames())
                    {
                        using var app = key.OpenSubKey(sub);
                        var name = app?.GetValue("DisplayName") as string;
                        if (name == null || name.IndexOf("Postman", StringComparison.OrdinalIgnoreCase) < 0) continue;
                        info.Installed = true;
                        info.Version = app.GetValue("DisplayVersion") as string ?? string.Empty;
                        info.InstallPath = app.GetValue("InstallLocation") as string ?? string.Empty;
                    }
                }
                catch { }
            }
        }

        if (!info.Installed)
        {
            var local = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Postman");
            if (File.Exists(Path.Combine(local, "Postman.exe")))
            {
                info.Installed = true;
                info.InstallPath = local;
            }
        }

        try
        {
            info.HasLocalDatabase = Directory.Exists(info.DataPath) &&
                Directory.EnumerateDirectories(info.DataPath, "*.leveldb", SearchOption.AllDirectories).Any();
        }
        catch { }

        return info;
    }

    private static readonly string[] Patterns =
    {
        "*.postman_collection.json",
        "*.postman_environment.json",
        "*.postman_globals.json",
        "*postman_dump*.json",
        "*Postman*Backup*.json"
    };

    /// <summary>Scans the usual places people drop Postman exports.</summary>
    public static List<DiscoveredFile> FindExportFiles(IEnumerable<string> extraFolders = null)
    {
        var profile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        var folders = new List<string>
        {
            Path.Combine(profile, "Downloads"),
            Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),
            Environment.GetFolderPath(Environment.SpecialFolder.Desktop),
            Path.Combine(profile, "OneDrive", "Documents"),
            Path.Combine(profile, "OneDrive", "Desktop"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "Postman"),
            Path.Combine(profile, "Postman", "files")
        };
        if (extraFolders != null) folders.AddRange(extraFolders);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var results = new List<DiscoveredFile>();

        foreach (var folder in folders.Distinct())
        {
            if (!Directory.Exists(folder)) continue;
            foreach (var pattern in Patterns)
            {
                IEnumerable<string> files;
                try
                {
                    files = Directory.EnumerateFiles(folder, pattern, new EnumerationOptions
                    {
                        RecurseSubdirectories = true,
                        MaxRecursionDepth = 3,
                        IgnoreInaccessible = true,
                        AttributesToSkip = FileAttributes.System
                    });
                }
                catch { continue; }

                foreach (var file in files)
                {
                    if (!seen.Add(file)) continue;
                    var described = Describe(file);
                    if (described != null) results.Add(described);
                    if (results.Count > 400) break;
                }
            }
        }

        return results.OrderByDescending(r => r.Modified).ToList();
    }

    /// <summary>Reads just enough of a file to name and classify it.</summary>
    public static DiscoveredFile Describe(string path)
    {
        try
        {
            var info = new FileInfo(path);
            if (!info.Exists || info.Length > 80 * 1024 * 1024) return null;

            var file = new DiscoveredFile
            {
                Path = path,
                Name = info.Name,
                Size = info.Length,
                Modified = info.LastWriteTime
            };

            using var stream = File.OpenRead(path);
            using var doc = JsonDocument.Parse(stream, new JsonDocumentOptions
            {
                AllowTrailingCommas = true,
                CommentHandling = JsonCommentHandling.Skip,
                MaxDepth = 256
            });
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;

            if (root.TryGetProperty("collections", out var cols) && cols.ValueKind == JsonValueKind.Array)
            {
                file.Kind = "dump";
                file.Title = $"Postman data dump ({cols.GetArrayLength()} collections)";
                file.RequestCount = cols.EnumerateArray().Sum(CountItems);
                return file;
            }

            var scope = root.TryGetProperty("_postman_variable_scope", out var sc) ? sc.GetString() : null;
            if (scope == "globals")
            {
                file.Kind = "globals";
                file.Title = "Globals";
                return file;
            }
            if (scope == "environment" || (root.TryGetProperty("values", out _) && !root.TryGetProperty("item", out _)))
            {
                file.Kind = "environment";
                file.Title = root.TryGetProperty("name", out var en) ? en.GetString() : info.Name;
                return file;
            }

            if (root.TryGetProperty("item", out _) || root.TryGetProperty("requests", out _))
            {
                file.Kind = "collection";
                file.Title = root.TryGetProperty("info", out var inf) && inf.TryGetProperty("name", out var n)
                    ? n.GetString()
                    : root.TryGetProperty("name", out var n2) ? n2.GetString() : info.Name;
                file.RequestCount = CountItems(root);
                return file;
            }

            return null;
        }
        catch
        {
            return null;
        }
    }

    private static int CountItems(JsonElement element)
    {
        if (element.ValueKind != JsonValueKind.Object) return 0;
        if (!element.TryGetProperty("item", out var items) || items.ValueKind != JsonValueKind.Array)
            return element.TryGetProperty("requests", out var reqs) && reqs.ValueKind == JsonValueKind.Array
                ? reqs.GetArrayLength()
                : 0;

        int total = 0;
        foreach (var item in items.EnumerateArray())
        {
            if (item.TryGetProperty("item", out _)) total += CountItems(item);
            else if (item.TryGetProperty("request", out _)) total++;
        }
        return total;
    }
}
