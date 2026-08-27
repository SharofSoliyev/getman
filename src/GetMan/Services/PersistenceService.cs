using System.Collections.ObjectModel;
using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using GetMan.Models;

namespace GetMan.Services;

public class WorkspaceFile
{
    public int Version { get; set; } = 1;
    public List<CollectionNode> Collections { get; set; } = new();
    public List<EnvironmentModel> Environments { get; set; } = new();
    public EnvironmentModel Globals { get; set; } = new() { Name = "Globals", IsGlobal = true };
    public List<HistoryEntry> History { get; set; } = new();
    public AppSettings Settings { get; set; } = new();
    public List<OpenTabState> OpenTabs { get; set; } = new();
}

public class OpenTabState
{
    public string NodeId { get; set; }
    public RequestModel Request { get; set; }
    public string Title { get; set; }
    public bool IsActive { get; set; }
}

public static class PersistenceService
{
    /// <summary>
    /// Where the workspace lives. Settable so the self-check can run against a throwaway
    /// folder instead of writing into the user's real workspace.
    /// </summary>
    public static string RootDir { get; set; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GetMan");

    public static string WorkspacePath => Path.Combine(RootDir, "workspace.json");
    public static string BackupPath => Path.Combine(RootDir, "workspace.backup.json");

    private static readonly JsonSerializerOptions Options = new()
    {
        WriteIndented = true,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Converters = { new JsonStringEnumConverter() },
        IncludeFields = false,
        MaxDepth = 256
    };

    public static WorkspaceFile Load()
    {
        try
        {
            Directory.CreateDirectory(RootDir);
            if (!File.Exists(WorkspacePath)) return Seed();

            var ws = Read(File.ReadAllText(WorkspacePath)) ?? Seed();
            ws.Collections ??= new List<CollectionNode>();
            ws.Environments ??= new List<EnvironmentModel>();
            ws.Globals ??= new EnvironmentModel { Name = "Globals", IsGlobal = true };
            ws.History ??= new List<HistoryEntry>();
            ws.Settings ??= new AppSettings();
            ws.OpenTabs ??= new List<OpenTabState>();
            foreach (var c in ws.Collections) c.FixupParents();
            return ws;
        }
        catch
        {
            try
            {
                if (File.Exists(BackupPath))
                {
                    var ws = Read(File.ReadAllText(BackupPath));
                    if (ws != null)
                    {
                        foreach (var c in ws.Collections) c.FixupParents();
                        return ws;
                    }
                }
            }
            catch { }
            return Seed();
        }
    }

    /// <summary>
    /// Serialises the workspace and encrypts its credentials on the way out. The models keep plain
    /// values in memory, so scripts, exports and the request builder never have to know about this.
    /// </summary>
    internal static string Write(WorkspaceFile ws)
    {
        var node = JsonSerializer.SerializeToNode(ws, Options);
        if (node != null && ws.Settings?.EncryptSecrets != false) SecretVault.ProtectTree(node);
        return node?.ToJsonString(Options) ?? "{}";
    }

    /// <summary>
    /// Decryption runs whatever the setting says, so turning encryption off still reads a file
    /// that was written while it was on.
    /// </summary>
    internal static WorkspaceFile Read(string json)
    {
        var node = JsonNode.Parse(json);
        if (node == null) return null;
        SecretVault.UnprotectTree(node);
        return node.Deserialize<WorkspaceFile>(Options);
    }

    private static readonly object SaveLock = new();

    public static void Save(WorkspaceFile ws)
    {
        lock (SaveLock)
        {
            try
            {
                Directory.CreateDirectory(RootDir);
                var json = Write(ws);
                var tmp = WorkspacePath + ".tmp";
                File.WriteAllText(tmp, json);
                if (File.Exists(WorkspacePath))
                {
                    try { File.Copy(WorkspacePath, BackupPath, true); } catch { }
                    File.Delete(WorkspacePath);
                }
                File.Move(tmp, WorkspacePath);
            }
            catch
            {
                // Saving must never take the app down.
            }
        }
    }

    public static WorkspaceFile Seed()
    {
        var ws = new WorkspaceFile();

        var col = new CollectionNode
        {
            Kind = NodeKind.Collection,
            Name = "My Collection",
            IsExpanded = true,
            Description = "Sample collection created by GetMan."
        };
        col.Variables.Add(new KeyValueItem("baseUrl", "https://postman-echo.com"));

        var folder = new CollectionNode { Kind = NodeKind.Folder, Name = "Echo", IsExpanded = true, Parent = col };

        var get = new CollectionNode
        {
            Kind = NodeKind.Request,
            Name = "GET request",
            Parent = folder,
            Request = new RequestModel
            {
                Method = "GET",
                Url = "{{baseUrl}}/get?foo=bar",
                TestScript = "pm.test(\"Status code is 200\", function () {\n    pm.response.to.have.status(200);\n});\n\npm.test(\"Response is JSON\", function () {\n    pm.response.to.be.json;\n});"
            }
        };
        get.Request.QueryParams.Add(new KeyValueItem("foo", "bar"));

        var post = new CollectionNode
        {
            Kind = NodeKind.Request,
            Name = "POST json",
            Parent = folder,
            Request = new RequestModel
            {
                Method = "POST",
                Url = "{{baseUrl}}/post",
                Body = new RequestBody
                {
                    Mode = BodyMode.Raw,
                    RawLanguage = "json",
                    Raw = "{\n  \"name\": \"{{$randomFullName}}\",\n  \"id\": \"{{$guid}}\"\n}"
                },
                TestScript = "pm.test(\"Status code is 200\", () => pm.response.to.have.status(200));\n\npm.test(\"Echoed body has a name\", () => {\n    const body = pm.response.json();\n    pm.expect(body.data).to.have.property(\"name\");\n});"
            }
        };
        post.Request.Headers.Add(new KeyValueItem("Content-Type", "application/json"));

        folder.Children.Add(get);
        folder.Children.Add(post);
        col.Children.Add(folder);
        col.FixupParents();

        ws.Collections.Add(col);

        var env = new EnvironmentModel { Name = "Sample environment" };
        env.Variables.Add(new KeyValueItem("baseUrl", "https://postman-echo.com"));
        env.Variables.Add(new KeyValueItem("token", ""));
        ws.Environments.Add(env);

        return ws;
    }

    /// <summary>
    /// UTF-8 without a byte order mark. <see cref="System.Text.Encoding.UTF8"/> emits a BOM,
    /// which strict JSON parsers and several API tools choke on.
    /// </summary>
    private static readonly System.Text.UTF8Encoding Utf8NoBom = new(encoderShouldEmitUTF8Identifier: false);

    public static void ExportToFile(string path, string content) => File.WriteAllText(path, content, Utf8NoBom);
}
