using System.Text;
using System.Text.Json;
using GetMan.Models;
using GetMan.Services;

namespace GetMan.SelfTest;

public static class Program
{
    private static int _pass, _fail;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;
        bool online = !args.Contains("--offline");

        if (args.Contains("--repair-workspace"))
            return RepairWorkspace(args.SkipWhile(a => a != "--repair-workspace").Skip(1).Where(File.Exists).ToList());

        if (args.Contains("--unicode"))
        {
            Section("Unicode round trip");
            TestUnicodeRoundTrip();
            Console.WriteLine($"\npassed: {_pass}   failed: {_fail}");
            return _fail == 0 ? 0 : 1;
        }

        var files = args.SkipWhile(a => a != "--import").Skip(1).Where(File.Exists).ToList();
        if (files.Count > 0)
        {
            Section("Importing files from disk");
            foreach (var f in files) ImportFileReport(f);
        }

        Section("Postman v2.1 import");
        TestPostmanImport();

        Section("Postman v1 import");
        TestPostmanV1Import();

        Section("Real world shapes");
        TestAwkwardShapes();

        Section("Unicode round trip");
        TestUnicodeRoundTrip();

        Section("Postman discovery on this machine");
        TestPostmanDiscovery();

        Section("Environment import");
        TestEnvironmentImport();

        Section("Export round trip");
        TestExportRoundTrip();

        Section("Variable resolution");
        TestVariables();

        Section("cURL import");
        TestCurl();

        Section("Data files (runner and CLI)");
        TestDataFiles();

        Section("OpenAPI 3 import");
        TestOpenApiImport();

        Section("Swagger 2.0 import (YAML)");
        TestSwaggerImport();

        Section("Request preparation");
        TestPreparation();

        Section("Auth signing");
        TestAuth();

        Section("Code generation");
        TestCodeGen();

        Section("Script runtime (pm API)");
        TestScripts();

        if (online)
        {
            Section("Live HTTP");
            await TestHttpAsync();

            Section("End to end run (scripts + request + tests)");
            await TestEndToEndAsync();
        }

        Console.WriteLine();
        Console.WriteLine(new string('-', 60));
        Console.WriteLine($"passed: {_pass}   failed: {_fail}");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>
    /// Drops collections whose text carries a Unicode replacement character (evidence of a
    /// mis-decoded save) and re-imports them from their original export files.
    /// </summary>
    private static int RepairWorkspace(List<string> sources)
    {
        var workspace = PersistenceService.Load();

        static bool Damaged(CollectionNode node) =>
            node.Flatten().Any(n => (n.Name ?? string.Empty).Contains('�')
                                    || (n.Description ?? string.Empty).Contains('�')
                                    || n.Variables.Any(v => (v.Value ?? string.Empty).Contains('�'))
                                    || (n.Request?.Url ?? string.Empty).Contains('�')
                                    || (n.Request?.Body?.Raw ?? string.Empty).Contains('�'));

        var damaged = workspace.Collections.Where(Damaged).ToList();
        foreach (var node in damaged)
        {
            Console.WriteLine($"  dropping damaged collection: {node.Name}");
            workspace.Collections.Remove(node);
        }

        foreach (var file in sources)
        {
            var result = PostmanImporter.ImportFile(file);
            if (!result.Success)
            {
                Console.WriteLine($"  FAILED to re-import {Path.GetFileName(file)}: {result.Error}");
                _fail++;
                continue;
            }
            foreach (var collection in result.Collections)
            {
                if (workspace.Collections.Any(c => c.Name == collection.Name))
                {
                    Console.WriteLine($"  already present, skipped: {collection.Name}");
                    continue;
                }
                workspace.Collections.Add(collection);
                Console.WriteLine($"  re-imported: {collection.Name}");
            }
        }

        PersistenceService.Save(workspace);

        var reloaded = PersistenceService.Load();
        var stillDamaged = reloaded.Collections.Count(Damaged);
        Check("workspace has no replacement characters left", stillDamaged == 0, stillDamaged + " collection(s) still damaged");
        foreach (var c in reloaded.Collections) Console.WriteLine("  now: " + c.Name);

        Console.WriteLine($"\npassed: {_pass}   failed: {_fail}");
        return _fail == 0 ? 0 : 1;
    }

    /// <summary>Cyrillic, CJK and emoji must survive import, save, reload and export.</summary>
    private static void TestUnicodeRoundTrip()
    {
        const string cyrillic = "AFIN Bank SDK v1 (для банков и PLUM)";
        const string mixed = "Заголовок 名前 emoji ✓ — dash";

        var json = CollectionJson
            .Replace("Demo API", cyrillic)
            .Replace("List users", mixed);

        var imported = PostmanImporter.ImportText(json).Collections.FirstOrDefault();
        Eq("cyrillic collection name imports", imported?.Name, cyrillic);
        Eq("mixed script request name imports", imported?.Children[0].Children[0].Name, mixed);

        // Through the exporter and back.
        var exported = PostmanExporter.ExportCollection(imported);
        var back = PostmanImporter.ImportText(exported).Collections.FirstOrDefault();
        Eq("cyrillic survives export", back?.Name, cyrillic);
        Eq("mixed script survives export", back?.Children[0].Children[0].Name, mixed);

        // Through the workspace file on disk.
        var probe = Path.Combine(Path.GetTempPath(), "getman_unicode_probe.json");
        try
        {
            PersistenceService.ExportToFile(probe, exported);
            var fromDisk = PostmanImporter.ImportFile(probe).Collections.FirstOrDefault();
            Eq("cyrillic survives a disk round trip", fromDisk?.Name, cyrillic);

            var bytes = File.ReadAllBytes(probe);
            Check("export is UTF-8 without a BOM",
                bytes.Length < 3 || !(bytes[0] == 0xEF && bytes[1] == 0xBB && bytes[2] == 0xBF));
            Check("export contains no replacement characters",
                !File.ReadAllText(probe, Encoding.UTF8).Contains('�'));
        }
        finally
        {
            try { File.Delete(probe); } catch { }
        }

        // Variables and bodies too.
        var vars = new VariableResolver();
        vars.EnvironmentVars["город"] = "Ташкент";
        Eq("cyrillic variable resolves", vars.Resolve("{{город}}"), "Ташкент");
        Eq("cyrillic in a url", vars.Resolve("https://x.dev/{{город}}"), "https://x.dev/Ташкент");
    }

    /// <summary>Imports a real file, then re-imports its own export to prove the round trip.</summary>
    private static void ImportFileReport(string path)
    {
        var name = Path.GetFileName(path);
        var result = PostmanImporter.ImportFile(path);
        if (!result.Success)
        {
            Check($"import {name}", false, result.Error);
            return;
        }

        foreach (var col in result.Collections)
        {
            var requests = col.Flatten().Count(n => n.Kind == NodeKind.Request);
            var folders = col.Flatten().Count(n => n.Kind == NodeKind.Folder);
            Check($"import {name}: '{col.Name}' ({folders} folders, {requests} requests)", requests > 0, "no requests found");

            var exported = PostmanExporter.ExportCollection(col);
            var back = PostmanImporter.ImportText(exported).Collections.FirstOrDefault();
            Eq($"round trip {name}: request count", back?.Flatten().Count(n => n.Kind == NodeKind.Request), requests);

            foreach (var node in col.Flatten().Where(n => n.Kind == NodeKind.Request).Take(50))
            {
                var prepared = RequestPreparer.Prepare(node.Request, node, new VariableResolver(), new AppSettings());
                Check($"prepare '{node.Name}'", !string.IsNullOrWhiteSpace(prepared.Url) || !string.IsNullOrWhiteSpace(node.Request.Url),
                    "empty url");
            }
        }
        foreach (var env in result.Environments)
            Check($"import {name}: environment '{env.Name}' ({env.Variables.Count} vars)", true);
        foreach (var w in result.Warnings) Console.WriteLine("  note  " + w);
    }

    #region harness

    /// <summary>
    /// The runner window and the command line read iteration data through the same reader, so a
    /// quoting bug here would silently change what every data-driven run sends.
    /// </summary>
    private static void TestDataFiles()
    {
        var cells = DataFile.SplitCsv("plain,\"quoted, with comma\",\"say \"\"hi\"\"\",  padded  ");
        Check("csv splits on unquoted commas only", cells.Count == 4, $"got {cells.Count} cells");
        Check("csv keeps a comma inside quotes", cells.Count > 1 && cells[1] == "quoted, with comma",
            cells.Count > 1 ? cells[1] : "-");
        Check("csv unescapes a doubled quote", cells.Count > 2 && cells[2] == "say \"hi\"",
            cells.Count > 2 ? cells[2] : "-");
        Check("csv trims surrounding space", cells.Count > 3 && cells[3] == "padded",
            cells.Count > 3 ? cells[3] : "-");

        var dir = Path.Combine(Path.GetTempPath(), "GetMan.DataFile");
        Directory.CreateDirectory(dir);
        try
        {
            var csv = Path.Combine(dir, "rows.csv");
            File.WriteAllText(csv, "name,city\nАзиза,Toshkent\n\"Соliyev, S.\",Samarqand\n", new UTF8Encoding(false));
            var rows = DataFile.Read(csv);
            Check("csv reads one row per line, blank lines skipped", rows.Count == 2, $"got {rows.Count}");
            Check("csv keeps Cyrillic intact", rows.Count > 0 && rows[0]["name"] == "Азиза",
                rows.Count > 0 ? rows[0]["name"] : "-");
            Check("csv row with a quoted comma stays one cell",
                rows.Count > 1 && rows[1]["city"] == "Samarqand", rows.Count > 1 ? rows[1]["city"] : "-");

            var json = Path.Combine(dir, "rows.json");
            File.WriteAllText(json, "[{\"id\":7,\"name\":\"Azi\"},{\"id\":8,\"name\":\"Sha\"},\"skip me\"]");
            var jsonRows = DataFile.Read(json);
            Check("json array yields one row per object, non-objects ignored", jsonRows.Count == 2,
                $"got {jsonRows.Count}");
            Check("json numbers become strings", jsonRows.Count > 0 && jsonRows[0]["id"] == "7",
                jsonRows.Count > 0 ? jsonRows[0]["id"] : "-");

            File.WriteAllText(json, "{\"not\":\"an array\"}");
            Check("json object at the root yields no rows", DataFile.Read(json).Count == 0);
        }
        finally
        {
            try { Directory.Delete(dir, true); } catch { }
        }
    }

    /// <summary>Fixtures are found by walking up from the binary, so the suite runs from anywhere.</summary>
    private static string Fixture(string name)
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory != null)
        {
            var candidate = Path.Combine(directory.FullName, "tools", "fixtures", name);
            if (File.Exists(candidate)) return candidate;
            directory = directory.Parent;
        }
        return Path.Combine("tools", "fixtures", name);
    }

    private static CollectionNode FindRequest(CollectionNode collection, string name) =>
        collection?.Flatten().FirstOrDefault(n =>
            n.Kind == NodeKind.Request && string.Equals(n.Name, name, StringComparison.OrdinalIgnoreCase));

    private static string VariableValue(CollectionNode collection, string key) =>
        collection?.Variables.FirstOrDefault(v => v.Key == key)?.Value;

    private static void TestOpenApiImport()
    {
        var result = PostmanImporter.ImportFile(Fixture("petstore.openapi.json"));
        Check("openapi 3 import succeeds", string.IsNullOrEmpty(result.Error), result.Error);

        var collection = result.Collections.FirstOrDefault();
        if (collection == null) { Check("openapi 3 produced a collection", false); return; }

        Check("collection takes its name from info.title", collection.Name == "Pet store", collection.Name);
        Check("server url becomes baseUrl with its template turned into a variable",
            VariableValue(collection, "baseUrl") == "https://{{region}}.api.petstore.test/v1",
            VariableValue(collection, "baseUrl"));
        Check("server variable keeps its default", VariableValue(collection, "region") == "eu",
            VariableValue(collection, "region"));
        Check("a second server is reported rather than silently dropped",
            result.Warnings.Any(w => w.Contains("2 servers")), string.Join("; ", result.Warnings));

        Check("collection auth comes from the top-level security requirement",
            collection.Auth.Type == AuthType.Bearer && collection.Auth.Token == "{{bearerToken}}",
            $"{collection.Auth.Type} / {collection.Auth.Token}");
        Check("the bearer token is left as an empty variable to fill in",
            VariableValue(collection, "bearerToken") == string.Empty);

        var folders = collection.Children.Where(c => c.Kind == NodeKind.Folder).Select(c => c.Name).ToList();
        Check("operations are grouped by tag", folders.Contains("pets") && folders.Contains("owners"),
            string.Join(", ", folders));
        Check("an untagged operation falls back to its first path segment", folders.Contains("health"),
            string.Join(", ", folders));
        Check("the tag description reaches the folder",
            collection.Children.FirstOrDefault(c => c.Name == "pets")?.Description == "Everything about pets");

        var list = FindRequest(collection, "List pets");
        if (list == null) { Check("summary becomes the request name", false, "'List pets' not found"); return; }

        Check("required query parameters land in the url",
            list.Request.Url == "{{baseUrl}}/pets?limit=20", list.Request.Url);
        Check("an optional query parameter is a disabled row rather than a url entry",
            list.Request.QueryParams.Any(p => p.Key == "status" && !p.Enabled) &&
            list.Request.QueryParams.Any(p => p.Key == "limit" && p.Enabled),
            string.Join(", ", list.Request.QueryParams.Select(p => $"{p.Key}={p.Enabled}")));
        Check("a query parameter keeps its description",
            list.Request.QueryParams.FirstOrDefault(p => p.Key == "limit")?.Description == "How many to return");
        Check("a header parameter becomes a header row, seeded from its format",
            list.Request.Headers.FirstOrDefault(p => p.Key == "X-Request-Id")?.Value == "{{$guid}}",
            list.Request.Headers.FirstOrDefault(p => p.Key == "X-Request-Id")?.Value);

        var delete = FindRequest(collection, "Delete a pet");
        Check("path templates become :variables",
            delete?.Request.Url == "{{baseUrl}}/pets/:petId", delete?.Request.Url);
        Check("a path parameter declared on the path applies to the operation under it",
            delete?.Request.PathVariables.Any(p => p.Key == "petId" && p.Description == "The pet id") == true);
        Check("an operation-level security requirement overrides the collection default",
            delete?.Request.Auth.Type == AuthType.ApiKey &&
            delete.Request.Auth.ApiKeyName == "X-API-Key" &&
            delete.Request.Auth.ApiKeyLocation == "header",
            $"{delete?.Request.Auth.Type} / {delete?.Request.Auth.ApiKeyName}");

        var create = FindRequest(collection, "Create a pet");
        var body = create?.Request.Body.Raw ?? string.Empty;
        Check("a json request body is generated from the schema",
            create?.Request.Body.Mode == BodyMode.Raw && body.Contains("\"name\""), body);
        Check("the schema's own example wins over a placeholder", body.Contains("\"Rex\"") && body.Contains("42"), body);
        Check("a date format becomes a plausible date", body.Contains("2026-01-31"), body);
        Check("a $ref is followed", body.Contains("\"email\"") && body.Contains("user@example.com"), body);
        Check("an array becomes a one-element array", body.Contains("\"tags\": ["), body);
        Check("a self-referencing schema terminates", body.Contains("\"friend\": null"), body);
        Check("a json body sets its content type",
            create?.Request.Headers.Any(h => h.Key == "Content-Type" && h.Value == "application/json") == true);

        var upload = FindRequest(collection, "Upload an avatar");
        Check("multipart becomes form-data", upload?.Request.Body.Mode == BodyMode.FormData);
        Check("a binary property becomes a file row",
            upload?.Request.Body.FormData.FirstOrDefault(f => f.Key == "file")?.Kind == ParamKind.File);
        Check("an optional form field is disabled",
            upload?.Request.Body.FormData.FirstOrDefault(f => f.Key == "caption")?.Enabled == false);

        Check("the server also becomes an environment",
            result.Environments.Any(e => e.Variables.Any(v => v.Key == "baseUrl")));
    }

    private static void TestSwaggerImport()
    {
        var result = PostmanImporter.ImportFile(Fixture("billing.swagger.yaml"));
        Check("yaml swagger 2.0 import succeeds", string.IsNullOrEmpty(result.Error), result.Error);

        var collection = result.Collections.FirstOrDefault();
        if (collection == null) { Check("swagger produced a collection", false); return; }

        Check("collection name", collection.Name == "Billing", collection.Name);
        Check("scheme, host and basePath compose baseUrl",
            VariableValue(collection, "baseUrl") == "https://api.billing.test/v2",
            VariableValue(collection, "baseUrl"));
        Check("securityDefinitions map onto auth",
            collection.Auth.Type == AuthType.ApiKey && collection.Auth.ApiKeyName == "X-Billing-Key",
            $"{collection.Auth.Type} / {collection.Auth.ApiKeyName}");

        var list = FindRequest(collection, "List invoices");
        Check("a swagger query parameter is typed from the parameter itself, not a schema",
            list?.Request.Url == "{{baseUrl}}/invoices?page=1", list?.Request.Url);
        Check("an optional swagger query parameter is a disabled row",
            list?.Request.QueryParams.Any(p => p.Key == "customer" && !p.Enabled) == true);

        var raise = FindRequest(collection, "Raise an invoice");
        var body = raise?.Request.Body.Raw ?? string.Empty;
        Check("an 'in: body' parameter becomes the json body",
            raise?.Request.Body.Mode == BodyMode.Raw && body.Contains("\"amount\""), body);
        Check("yaml numbers survive the conversion", body.Contains("149.5"), body);
        Check("a default value is used when there is no example", body.Contains("\"UZS\""), body);
        Check("a nested array of objects is generated", body.Contains("\"sku\""), body);

        var pay = FindRequest(collection, "Pay an invoice");
        Check("swagger path parameters become :variables",
            pay?.Request.Url == "{{baseUrl}}/invoices/:id/pay", pay?.Request.Url);
        Check("'in: formData' becomes a urlencoded body",
            pay?.Request.Body.Mode == BodyMode.UrlEncoded &&
            pay.Request.Body.UrlEncoded.Any(f => f.Key == "method" && f.Value == "card"),
            string.Join(", ", pay?.Request.Body.UrlEncoded.Select(f => $"{f.Key}={f.Value}") ?? Array.Empty<string>()));
    }

    private static void Section(string name)
    {
        Console.WriteLine();
        Console.WriteLine("== " + name + " " + new string('=', Math.Max(0, 50 - name.Length)));
    }

    private static void Check(string label, bool ok, string detail = null)
    {
        if (ok) { _pass++; Console.WriteLine("  PASS  " + label); }
        else
        {
            _fail++;
            Console.WriteLine("  FAIL  " + label + (detail == null ? "" : "  -> " + detail));
        }
    }

    private static void Eq<T>(string label, T actual, T expected)
        => Check(label, Equals(actual, expected), $"expected [{expected}] got [{actual}]");

    #endregion

    private const string CollectionJson = """
    {
      "info": {
        "_postman_id": "aaaa-bbbb",
        "name": "Demo API",
        "description": "Imported sample",
        "schema": "https://schema.getpostman.com/json/collection/v2.1.0/collection.json"
      },
      "auth": { "type": "bearer", "bearer": [{ "key": "token", "value": "{{authToken}}", "type": "string" }] },
      "event": [
        { "listen": "prerequest", "script": { "type": "text/javascript", "exec": ["pm.collectionVariables.set('runId', '42');"] } }
      ],
      "variable": [ { "key": "baseUrl", "value": "https://api.example.com" } ],
      "item": [
        {
          "name": "Users",
          "item": [
            {
              "name": "List users",
              "request": {
                "method": "GET",
                "header": [ { "key": "Accept", "value": "application/json" },
                            { "key": "X-Debug", "value": "1", "disabled": true } ],
                "url": {
                  "raw": "{{baseUrl}}/users?page=2&limit=10",
                  "host": ["{{baseUrl}}"],
                  "path": ["users"],
                  "query": [ { "key": "page", "value": "2" }, { "key": "limit", "value": "10" } ]
                }
              },
              "event": [
                { "listen": "test", "script": { "exec": ["pm.test(\"ok\", () => pm.response.to.have.status(200));"] } }
              ]
            },
            {
              "name": "Create user",
              "request": {
                "method": "POST",
                "header": [ { "key": "Content-Type", "value": "application/json" } ],
                "body": {
                  "mode": "raw",
                  "raw": "{\"name\":\"ada\"}",
                  "options": { "raw": { "language": "json" } }
                },
                "url": { "raw": "{{baseUrl}}/users" },
                "auth": { "type": "basic", "basic": [ { "key": "username", "value": "u" }, { "key": "password", "value": "p" } ] }
              }
            },
            {
              "name": "Upload avatar",
              "request": {
                "method": "POST",
                "body": {
                  "mode": "formdata",
                  "formdata": [
                    { "key": "file", "type": "file", "src": "C:/tmp/a.png" },
                    { "key": "caption", "type": "text", "value": "hi" }
                  ]
                },
                "url": { "raw": "{{baseUrl}}/avatar" }
              }
            },
            {
              "name": "Get by id",
              "request": {
                "method": "GET",
                "url": {
                  "raw": "{{baseUrl}}/users/:userId",
                  "variable": [ { "key": "userId", "value": "7" } ]
                }
              }
            }
          ]
        }
      ]
    }
    """;

    private static CollectionNode ImportDemo()
    {
        var result = PostmanImporter.ImportText(CollectionJson, "fallback");
        return result.Collections.FirstOrDefault();
    }

    private static void TestPostmanImport()
    {
        var result = PostmanImporter.ImportText(CollectionJson, "fallback");
        Check("import succeeds", result.Success, result.Error);
        var col = result.Collections.FirstOrDefault();
        Check("one collection", col != null);
        if (col == null) return;

        Eq("collection name", col.Name, "Demo API");
        Eq("collection auth is bearer", col.Auth.Type, AuthType.Bearer);
        Eq("collection variable", col.Variables.FirstOrDefault()?.Value, "https://api.example.com");
        Check("collection prerequest script imported", col.PreRequestScript.Contains("collectionVariables"));

        var folder = col.Children.FirstOrDefault();
        Eq("folder name", folder?.Name, "Users");
        Eq("folder has 4 requests", folder?.Children.Count, 4);

        var list = folder.Children[0];
        Eq("request method", list.Request.Method, "GET");
        Check("url raw preserved", list.Request.Url.StartsWith("{{baseUrl}}/users"));
        Eq("query params", list.Request.QueryParams.Count, 2);
        Eq("query param value", list.Request.QueryParams[1].Value, "10");
        Eq("headers count", list.Request.Headers.Count, 2);
        Eq("disabled header", list.Request.Headers[1].Enabled, false);
        Check("test script imported", list.Request.TestScript.Contains("pm.test"));

        var create = folder.Children[1];
        Eq("body mode raw", create.Request.Body.Mode, BodyMode.Raw);
        Eq("body language", create.Request.Body.RawLanguage, "json");
        Eq("request level basic auth", create.Request.Auth.Type, AuthType.Basic);
        Eq("basic username", create.Request.Auth.Username, "u");

        var upload = folder.Children[2];
        Eq("formdata mode", upload.Request.Body.Mode, BodyMode.FormData);
        Eq("file field", upload.Request.Body.FormData[0].Kind, ParamKind.File);
        Eq("file src", upload.Request.Body.FormData[0].FilePath, "C:/tmp/a.png");

        var byId = folder.Children[3];
        Eq("path variable captured", byId.Request.PathVariables.FirstOrDefault()?.Key, "userId");
        Eq("path variable value", byId.Request.PathVariables.FirstOrDefault()?.Value, "7");

        Check("parents wired", list.Parent == folder && folder.Parent == col);
    }

    private static void TestPostmanV1Import()
    {
        const string v1 = """
        {
          "id": "col1",
          "name": "Legacy",
          "order": ["r1"],
          "folders": [ { "id": "f1", "name": "Folder A" } ],
          "requests": [
            { "id": "r1", "name": "Ping", "method": "POST", "url": "https://x.dev/ping?a=1",
              "headers": "Accept: application/json\nX-Key: 9",
              "dataMode": "raw", "rawModeData": "{\"a\":1}",
              "tests": "tests['ok'] = responseCode.code === 200;", "folder": "f1" }
          ]
        }
        """;
        var result = PostmanImporter.ImportText(v1);
        Check("v1 import succeeds", result.Success, result.Error);
        var col = result.Collections.FirstOrDefault();
        Eq("v1 name", col?.Name, "Legacy");
        var folder = col?.Children.FirstOrDefault();
        Eq("v1 folder", folder?.Name, "Folder A");
        var req = folder?.Children.FirstOrDefault();
        Eq("v1 request method", req?.Request.Method, "POST");
        Eq("v1 headers parsed", req?.Request.Headers.Count, 2);
        Eq("v1 raw body", req?.Request.Body.Raw, "{\"a\":1}");
        Eq("v1 query parsed", req?.Request.QueryParams.Count, 1);
        Check("v1 tests imported", req!.Request.TestScript.Contains("tests["));
    }

    /// <summary>Shapes real exports contain that the naive reader would trip over.</summary>
    private static void TestAwkwardShapes()
    {
        const string json = """
        {
          "info": {
            "name": "Quirks",
            "description": { "content": "rich description", "type": "text/markdown" },
            "schema": "https://schema.getpostman.com/json/collection/v2.0.0/collection.json"
          },
          "item": [
            { "name": "url as bare string", "request": "https://plain.example.com/ping" },
            {
              "name": "host and path as arrays",
              "request": {
                "method": "GET",
                "url": {
                  "protocol": "https",
                  "host": ["api", "example", "com"],
                  "port": "8443",
                  "path": ["v1", "things"],
                  "query": [{ "key": "q", "value": "a b", "disabled": true }]
                }
              }
            },
            {
              "name": "headers as a newline string",
              "request": { "method": "GET", "header": "Accept: application/json\nX-A: 1", "url": "https://x.dev/h" }
            },
            {
              "name": "script exec as one string",
              "request": { "method": "GET", "url": "https://x.dev/s" },
              "event": [{ "listen": "test", "script": { "exec": "pm.test('x', () => {});" } }]
            },
            {
              "name": "nested",
              "item": [
                { "name": "deeper", "item": [
                    { "name": "leaf", "request": { "method": "DELETE", "url": "https://x.dev/leaf" } }
                ]}
              ]
            },
            {
              "name": "graphql",
              "request": {
                "method": "POST",
                "url": "https://x.dev/graphql",
                "body": { "mode": "graphql", "graphql": { "query": "query { me { id } }", "variables": "{\"a\":1}" } }
              }
            },
            {
              "name": "binary file body",
              "request": { "method": "PUT", "url": "https://x.dev/f", "body": { "mode": "file", "file": { "src": "C:/tmp/x.bin" } } }
            },
            {
              "name": "profile behaviour",
              "request": { "method": "GET", "url": "https://x.dev/p" },
              "protocolProfileBehavior": { "followRedirects": false, "strictSSL": false, "disableUrlEncoding": true }
            },
            {
              "name": "numeric header value",
              "request": { "method": "GET", "url": "https://x.dev/n", "header": [{ "key": "X-Num", "value": 42 }] }
            },
            {
              "name": "formdata src as array",
              "request": { "method": "POST", "url": "https://x.dev/u",
                "body": { "mode": "formdata", "formdata": [{ "key": "f", "type": "file", "src": ["C:/tmp/one.png"] }] } }
            }
          ]
        }
        """;

        var result = PostmanImporter.ImportText(json);
        Check("quirky collection imports", result.Success, result.Error);
        var col = result.Collections.FirstOrDefault();
        if (col == null) return;

        Eq("description object flattened", col.Description, "rich description");
        Eq("top level items", col.Children.Count, 10);

        Eq("bare string url", col.Children[0].Request.Url, "https://plain.example.com/ping");
        Eq("bare string method defaults to GET", col.Children[0].Request.Method, "GET");

        var composed = col.Children[1].Request;
        Eq("host array composed", composed.Url, "https://api.example.com:8443/v1/things");
        Eq("disabled query kept but off", composed.QueryParams[0].Enabled, false);

        Eq("string headers split", col.Children[2].Request.Headers.Count, 2);
        Eq("string header value", col.Children[2].Request.Headers[0].Value, "application/json");

        Check("string exec imported", col.Children[3].Request.TestScript.Contains("pm.test"));

        var deep = col.Children[4].Children[0].Children[0];
        Eq("three level nesting", deep.Name, "leaf");
        Eq("nested method", deep.Request.Method, "DELETE");

        var gql = col.Children[5].Request;
        Eq("graphql mode", gql.Body.Mode, BodyMode.GraphQL);
        Eq("graphql query", gql.Body.GraphQlQuery, "query { me { id } }");
        Eq("graphql variables", gql.Body.GraphQlVariables, "{\"a\":1}");

        Eq("binary body", col.Children[6].Request.Body.Mode, BodyMode.Binary);
        Eq("binary path", col.Children[6].Request.Body.BinaryPath, "C:/tmp/x.bin");

        var prof = col.Children[7].Request.Settings;
        Eq("followRedirects honoured", prof.FollowRedirects, false);
        Eq("strictSSL honoured", prof.VerifySsl, false);
        Eq("disableUrlEncoding honoured", prof.EncodeUrl, false);

        Eq("numeric header stringified", col.Children[8].Request.Headers[0].Value, "42");
        Eq("formdata src array", col.Children[9].Request.Body.FormData[0].FilePath, "C:/tmp/one.png");

        // GraphQL is turned into a proper JSON payload on the wire.
        var prepared = RequestPreparer.Prepare(col.Children[5].Request, col.Children[5], new VariableResolver(), new AppSettings());
        Eq("graphql content type", prepared.ContentType, "application/json");
        Check("graphql payload has query and variables",
            prepared.BodyText.Contains("\"query\"") && prepared.BodyText.Contains("\"variables\""), prepared.BodyText);
    }

    /// <summary>Reports what the local-Postman discovery actually finds here.</summary>
    private static void TestPostmanDiscovery()
    {
        var install = PostmanDiscovery.Detect();
        Console.WriteLine($"  info  Postman installed: {install.Installed}"
                          + (install.Installed ? $" (version {install.Version})" : string.Empty));
        Console.WriteLine($"  info  data folder: {install.DataPath} (local database: {install.HasLocalDatabase})");

        var files = PostmanDiscovery.FindExportFiles();
        Console.WriteLine($"  info  export files found: {files.Count}");
        foreach (var f in files.Take(10))
            Console.WriteLine($"        [{f.Kind}] {f.Title} - {f.RequestCount} request(s), {f.SizeText}, {f.Path}");

        Check("discovery never throws", true);
        Check("every discovered file classifies", files.All(f => f.Kind != "unknown"),
            string.Join(", ", files.Where(f => f.Kind == "unknown").Select(f => f.Name)));

        // Round trip whatever we found so the discovery result is proven importable.
        foreach (var f in files.Take(5))
        {
            var result = PostmanImporter.ImportFile(f.Path);
            Check($"discovered file imports: {f.Name}", result.Success, result.Error);
        }

        // A file we write ourselves must always be found and classified.
        var temp = Path.Combine(Path.GetTempPath(), "getman_probe.postman_collection.json");
        try
        {
            File.WriteAllText(temp, CollectionJson);
            var described = PostmanDiscovery.Describe(temp);
            Eq("describes a collection file", described?.Kind, "collection");
            Eq("reads the collection name", described?.Title, "Demo API");
            Eq("counts requests recursively", described?.RequestCount, 4);
        }
        finally
        {
            try { File.Delete(temp); } catch { }
        }
    }

    private static void TestEnvironmentImport()
    {
        const string env = """
        {
          "id": "e1",
          "name": "Staging",
          "values": [
            { "key": "baseUrl", "value": "https://staging.example.com", "enabled": true },
            { "key": "token", "value": "abc", "enabled": false },
            { "key": "secret", "value": "s3cr3t", "type": "secret", "enabled": true }
          ],
          "_postman_variable_scope": "environment"
        }
        """;
        var result = PostmanImporter.ImportText(env);
        Check("environment import succeeds", result.Success, result.Error);
        var e = result.Environments.FirstOrDefault();
        Eq("env name", e?.Name, "Staging");
        Eq("env var count", e?.Variables.Count, 3);
        Eq("disabled flag", e?.Variables[1].Enabled, false);
        Eq("secret flag", e?.Variables[2].Secret, true);

        var resolver = new VariableResolver();
        resolver.LoadEnvironment(e);
        Eq("disabled vars excluded", resolver.EnvironmentVars.ContainsKey("token"), false);
        Eq("enabled var loaded", resolver.EnvironmentVars["baseUrl"], "https://staging.example.com");
    }

    private static void TestExportRoundTrip()
    {
        var col = ImportDemo();
        var json = PostmanExporter.ExportCollection(col);
        Check("export is valid json", TextFormatter.IsValidJson(json, out var err), err);

        var back = PostmanImporter.ImportText(json).Collections.FirstOrDefault();
        Eq("round trip name", back?.Name, "Demo API");
        Eq("round trip auth", back?.Auth.Type, AuthType.Bearer);
        Eq("round trip folder count", back?.Children.Count, 1);
        Eq("round trip request count", back?.Children[0].Children.Count, 4);
        Eq("round trip body", back?.Children[0].Children[1].Request.Body.Raw, "{\"name\":\"ada\"}");
        Eq("round trip formdata file",
            back?.Children[0].Children[2].Request.Body.FormData[0].Kind, ParamKind.File);
        Check("round trip scripts", back!.PreRequestScript.Contains("collectionVariables"));

        using var doc = JsonDocument.Parse(json);
        Eq("schema is v2.1",
            doc.RootElement.GetProperty("info").GetProperty("schema").GetString(),
            "https://schema.getpostman.com/json/collection/v2.1.0/collection.json");
    }

    private static void TestVariables()
    {
        var r = new VariableResolver();
        r.Globals["host"] = "global.example";
        r.CollectionVars["host"] = "collection.example";
        r.EnvironmentVars["host"] = "env.example";
        Eq("environment beats collection and globals", r.Resolve("{{host}}"), "env.example");

        r.LocalVars["host"] = "local.example";
        Eq("locals win", r.Resolve("{{host}}"), "local.example");

        r.EnvironmentVars["a"] = "{{b}}";
        r.EnvironmentVars["b"] = "deep";
        Eq("nested resolution", r.Resolve("x/{{a}}"), "x/deep");

        Eq("unknown token untouched", r.Resolve("{{nope}}"), "{{nope}}");
        Check("unresolved detection", r.FindUnresolved("{{nope}}/{{host}}").SequenceEqual(new[] { "nope" }));

        Check("guid dynamic", Guid.TryParse(r.Resolve("{{$guid}}"), out _));
        Check("timestamp dynamic", long.TryParse(r.Resolve("{{$timestamp}}"), out _));
        var n = int.Parse(r.Resolve("{{$randomInt(5,7)}}"));
        Check("parameterised randomInt in range", n >= 5 && n <= 7, n.ToString());
        Check("randomEmail looks like an email", r.Resolve("{{$randomEmail}}").Contains("@"));
        Eq("spaces inside braces", r.Resolve("{{  host  }}"), "local.example");
    }

    private static void TestCurl()
    {
        const string cmd = """
        curl -X POST 'https://api.example.com/v1/items?q=1' \
          -H 'Content-Type: application/json' \
          -H "Authorization: Bearer xyz" \
          -u admin:secret \
          -k \
          -d '{"name":"test","n":2}'
        """;
        var node = CurlImporter.Parse(cmd);
        Eq("curl method", node.Request.Method, "POST");
        Eq("curl url", node.Request.Url, "https://api.example.com/v1/items?q=1");
        Eq("curl headers", node.Request.Headers.Count, 2);
        Eq("curl auth header value", node.Request.Headers[1].Value, "Bearer xyz");
        Eq("curl basic auth", node.Request.Auth.Type, AuthType.Basic);
        Eq("curl basic user", node.Request.Auth.Username, "admin");
        Eq("curl insecure", node.Request.Settings.VerifySsl, false);
        Eq("curl body", node.Request.Body.Raw, "{\"name\":\"test\",\"n\":2}");
        Eq("curl body language", node.Request.Body.RawLanguage, "json");
        Eq("curl query param", node.Request.QueryParams.FirstOrDefault()?.Key, "q");

        var form = CurlImporter.Parse("curl https://x.dev/u -F name=ada -F avatar=@C:/tmp/a.png");
        Eq("curl multipart mode", form.Request.Body.Mode, BodyMode.FormData);
        Eq("curl multipart file", form.Request.Body.FormData[1].Kind, ParamKind.File);
        Eq("curl implicit POST", form.Request.Method, "POST");
    }

    private static void TestPreparation()
    {
        var col = ImportDemo();
        var folder = col.Children[0];
        var list = folder.Children[0];

        var vars = new VariableResolver();
        vars.LoadCollectionChain(folder);
        vars.EnvironmentVars["authToken"] = "T0K3N";

        var prepared = RequestPreparer.Prepare(list.Request, list, vars, new AppSettings());
        Eq("url resolved", prepared.Url, "https://api.example.com/users?page=2&limit=10");
        Eq("enabled headers only", prepared.Headers.Count, 1);
        Eq("effective auth inherited from collection", prepared.Auth.Type, AuthType.Bearer);

        var resolvedAuth = RequestPreparer.ResolveAuthVariables(prepared.Auth, vars);
        Eq("auth token resolved", resolvedAuth.Token, "T0K3N");
        AuthApplier.Apply(prepared, resolvedAuth);
        Eq("bearer header applied", prepared.GetHeader("Authorization"), "Bearer T0K3N");

        var byId = folder.Children[3];
        var prepared2 = RequestPreparer.Prepare(byId.Request, byId, vars, new AppSettings());
        Eq("path variable substituted", prepared2.Url, "https://api.example.com/users/7");

        var create = folder.Children[1];
        var prepared3 = RequestPreparer.Prepare(create.Request, create, vars, new AppSettings());
        Eq("request auth overrides collection", prepared3.Auth.Type, AuthType.Basic);
        AuthApplier.Apply(prepared3, prepared3.Auth);
        Eq("basic header", prepared3.GetHeader("Authorization"),
            "Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("u:p")));
        Eq("json body carried", prepared3.BodyText, "{\"name\":\"ada\"}");
    }

    private static void TestAuth()
    {
        var req = new PreparedRequest { Method = "GET", Url = "https://s3.amazonaws.com/bucket/key" };
        var aws = new AuthConfig
        {
            Type = AuthType.AwsV4,
            AwsAccessKey = "AKIDEXAMPLE",
            AwsSecretKey = "wJalrXUtnFEMI/K7MDENG+bPxRfiCYEXAMPLEKEY",
            AwsRegion = "us-east-1",
            AwsService = "s3"
        };
        AuthApplier.Apply(req, aws);
        var header = req.GetHeader("Authorization");
        Check("aws v4 header shape", header != null && header.StartsWith("AWS4-HMAC-SHA256 Credential=AKIDEXAMPLE/"), header);
        Check("aws signed headers present", header != null && header.Contains("SignedHeaders=") && header.Contains("Signature="));
        Check("aws content sha header", req.HasHeader("X-Amz-Content-Sha256"));

        var apiKeyQuery = new PreparedRequest { Method = "GET", Url = "https://x.dev/a?b=1" };
        AuthApplier.Apply(apiKeyQuery, new AuthConfig { Type = AuthType.ApiKey, ApiKeyName = "k", ApiKeyValue = "v", ApiKeyLocation = "query" });
        Eq("apikey in query", apiKeyQuery.Url, "https://x.dev/a?b=1&k=v");

        var digest = AuthApplier.BuildDigestHeader(
            new AuthConfig { Username = "Mufasa", Password = "Circle Of Life" },
            "Digest realm=\"testrealm@host.com\", qop=\"auth,auth-int\", nonce=\"dcd98b7102dd2f0e8b11d0f600bfb0c093\", opaque=\"5ccc069c403ebaf9f0171e9517f40e41\"",
            "GET", new Uri("http://host.com/dir/index.html"), Array.Empty<byte>());
        Check("digest header shape", digest.StartsWith("Digest username=\"Mufasa\"") && digest.Contains("qop=auth"), digest);

        var hawk = new PreparedRequest { Method = "GET", Url = "https://x.dev/res" };
        AuthApplier.Apply(hawk, new AuthConfig { Type = AuthType.Hawk, HawkAuthId = "id1", HawkAuthKey = "key1" });
        Check("hawk header", hawk.GetHeader("Authorization")?.StartsWith("Hawk id=\"id1\"") == true);
    }

    private static void TestCodeGen()
    {
        var req = new PreparedRequest
        {
            Method = "POST",
            Url = "https://api.example.com/users",
            Mode = BodyMode.Raw,
            BodyText = "{\"a\":1}",
            ContentType = "application/json"
        };
        req.AddHeader("Authorization", "Bearer t");

        var curl = CodeGenerator.Generate(req, "cURL (bash)");
        Check("curl snippet has method", curl.Contains("curl -X POST"));
        Check("curl snippet has header", curl.Contains("-H 'Authorization: Bearer t'"));
        Check("curl snippet has body", curl.Contains("-d '{\"a\":1}'"));

        var py = CodeGenerator.Generate(req, "Python requests");
        Check("python snippet imports requests", py.Contains("import requests"));
        Check("python snippet sends payload", py.Contains("data=payload"));

        var cs = CodeGenerator.Generate(req, "C# HttpClient");
        Check("c# snippet builds request", cs.Contains("new HttpRequestMessage"));

        Eq("every target generates something",
            CodeGenerator.Targets.All(t => !string.IsNullOrWhiteSpace(CodeGenerator.Generate(req, t))), true);
    }

    private static void TestScripts()
    {
        var vars = new VariableResolver();
        vars.EnvironmentVars["seed"] = "5";

        var response = new ResponseModel
        {
            StatusCode = 200,
            StatusText = "OK",
            ElapsedMs = 42,
            SizeBytes = 123,
            BodyText = "{\"items\":[1,2,3],\"user\":{\"name\":\"ada\",\"age\":36},\"ok\":true}",
            ContentType = "application/json"
        };
        response.Headers.Add(new KeyValuePair<string, string>("Content-Type", "application/json"));
        response.Cookies.Add(new ResponseCookie { Name = "sid", Value = "xyz" });

        var ctx = new ScriptContext
        {
            Vars = vars,
            Request = new PreparedRequest { Method = "GET", Url = "https://api.example.com/x" },
            Response = response,
            RequestName = "Sample",
            App = new AppSettings { ScriptTimeoutMs = 8000 }
        };

        const string script = """
        pm.test("status is 200", function () {
            pm.response.to.have.status(200);
        });
        pm.test("is json", () => pm.response.to.be.json);
        pm.test("response is ok", () => pm.response.to.be.ok);
        pm.test("body parses", function () {
            const b = pm.response.json();
            pm.expect(b.items).to.be.an("array");
            pm.expect(b.items).to.have.lengthOf(3);
            pm.expect(b.items).to.include(2);
            pm.expect(b.user).to.have.property("name", "ada");
            pm.expect(b.user.age).to.be.above(30).and.below(40);
            pm.expect(b.ok).to.be.true;
            pm.expect(b.user).to.eql({ name: "ada", age: 36 });
            pm.expect("hello world").to.match(/world/);
            pm.expect(b.user.name).to.be.oneOf(["ada", "grace"]);
            pm.expect(b.missing).to.be.undefined;
            pm.expect(b.items).to.not.be.empty;
        });
        pm.test("deliberate failure", function () {
            pm.expect(1).to.equal(2);
        });
        pm.test("header helper", () => pm.response.to.have.header("Content-Type"));
        pm.test("response time", () => pm.expect(pm.response.responseTime).to.be.below(1000));
        pm.test("cookies", () => pm.expect(pm.cookies.get("sid")).to.equal("xyz"));
        pm.test("chai assert", () => { pm.assert.strictEqual(1, 1); });

        pm.environment.set("newVar", "hello");
        pm.globals.set("gVar", "world");
        pm.collectionVariables.set("cVar", "c");
        console.log("seed is", pm.environment.get("seed"));
        console.warn("a warning");

        tests["legacy assignment"] = pm.response.code === 200;

        pm.test("xml2Json", function () {
            const parsed = xml2Json("<root><a>1</a><a>2</a></root>");
            pm.expect(parsed.root.a).to.have.lengthOf(2);
        });
        pm.test("base64 helpers", function () {
            pm.expect(atob(btoa("abc"))).to.equal("abc");
        });
        pm.execution.setNextRequest("Next one");
        """;

        new ScriptRuntime().Run(script, ScriptPhase.Test, ctx);

        Check("no script error", string.IsNullOrEmpty(ctx.Error), ctx.Error);
        var byName = ctx.Tests.ToDictionary(t => t.Name, t => t.Status);
        Eq("status test passes", byName.GetValueOrDefault("status is 200"), TestStatus.Pass);
        Eq("json test passes", byName.GetValueOrDefault("is json"), TestStatus.Pass);
        Eq("ok test passes", byName.GetValueOrDefault("response is ok"), TestStatus.Pass);
        Eq("body assertions pass", byName.GetValueOrDefault("body parses"), TestStatus.Pass);
        Eq("failing test reported as fail", byName.GetValueOrDefault("deliberate failure"), TestStatus.Fail);
        Eq("header helper passes", byName.GetValueOrDefault("header helper"), TestStatus.Pass);
        Eq("response time passes", byName.GetValueOrDefault("response time"), TestStatus.Pass);
        Eq("cookies pass", byName.GetValueOrDefault("cookies"), TestStatus.Pass);
        Eq("assert module works", byName.GetValueOrDefault("chai assert"), TestStatus.Pass);
        Eq("legacy tests[] works", byName.GetValueOrDefault("legacy assignment"), TestStatus.Pass);
        Eq("xml2Json works", byName.GetValueOrDefault("xml2Json"), TestStatus.Pass);
        Eq("base64 helpers work", byName.GetValueOrDefault("base64 helpers"), TestStatus.Pass);

        var failure = ctx.Tests.First(t => t.Name == "deliberate failure");
        Check("failure carries a message", failure.Message.Contains("expected 1 to equal 2"), failure.Message);

        Eq("environment var set", vars.EnvironmentVars.GetValueOrDefault("newVar"), "hello");
        Eq("global var set", vars.Globals.GetValueOrDefault("gVar"), "world");
        Eq("collection var set", vars.CollectionVars.GetValueOrDefault("cVar"), "c");
        Eq("variable ops recorded", ctx.VariableOps.Count, 3);
        Eq("setNextRequest captured", ctx.NextRequest, "Next one");
        Check("console captured", ctx.Console.Any(c => c.Message.Contains("seed is 5")));
        Check("warn level captured", ctx.Console.Any(c => c.Level == "warn"));

        // pre-request mutation
        var ctx2 = new ScriptContext
        {
            Vars = new VariableResolver(),
            Request = new PreparedRequest { Method = "GET", Url = "https://api.example.com/x" },
            App = new AppSettings()
        };
        new ScriptRuntime().Run("""
            pm.request.headers.add({ key: 'X-Trace', value: 'abc' });
            pm.request.headers.upsert({ key: 'X-Trace', value: 'def' });
            pm.request.method = 'POST';
            pm.request.body = { raw: '{"x":1}' };
            pm.variables.set('local1', 'v1');
            """, ScriptPhase.PreRequest, ctx2);

        Check("no pre-request error", string.IsNullOrEmpty(ctx2.Error), ctx2.Error);
        Eq("method mutated", ctx2.Request.Method, "POST");
        Eq("header upserted once", ctx2.Request.Headers.Count(h => h.Key == "X-Trace"), 1);
        Eq("header value", ctx2.Request.GetHeader("X-Trace"), "def");
        Eq("body mutated", ctx2.Request.BodyText, "{\"x\":1}");
        Eq("local variable", ctx2.Vars.LocalVars.GetValueOrDefault("local1"), "v1");

        // error handling
        var ctx3 = new ScriptContext { Vars = new VariableResolver(), Request = new PreparedRequest(), App = new AppSettings() };
        new ScriptRuntime().Run("this is not valid javascript ***", ScriptPhase.Test, ctx3);
        Check("syntax error captured, no crash", !string.IsNullOrEmpty(ctx3.Error), "no error reported");
    }

    private static async Task TestHttpAsync()
    {
        using var engine = new HttpEngine();
        var app = new AppSettings { RequestTimeoutMs = 30000 };

        var get = new PreparedRequest { Method = "GET", Url = "https://postman-echo.com/get?hello=world" };
        var r1 = await engine.SendAsync(get, app, null, CancellationToken.None);
        if (!string.IsNullOrEmpty(r1.Error))
        {
            Console.WriteLine("  SKIP  live HTTP unavailable: " + r1.Error);
            return;
        }
        Eq("GET status", r1.StatusCode, 200);
        Check("GET body echoes the query", r1.BodyText.Contains("\"hello\""), r1.BodyText);
        Check("timings recorded", r1.ElapsedMs > 0);
        Check("headers captured", r1.Headers.Count > 0);

        var post = new PreparedRequest
        {
            Method = "POST",
            Url = "https://postman-echo.com/post",
            Mode = BodyMode.Raw,
            BodyText = "{\"a\":1}",
            ContentType = "application/json"
        };
        var r2 = await engine.SendAsync(post, app, null, CancellationToken.None);
        Eq("POST status", r2.StatusCode, 200);
        Check("POST body echoed", r2.BodyText.Contains("\"a\""), r2.BodyText);

        var form = new PreparedRequest { Method = "POST", Url = "https://postman-echo.com/post", Mode = BodyMode.UrlEncoded, BodyText = "x=1&y=2", ContentType = "application/x-www-form-urlencoded" };
        var r3 = await engine.SendAsync(form, app, null, CancellationToken.None);
        Check("urlencoded form echoed", r3.BodyText.Contains("\"x\""), r3.BodyText);

        var multipart = new PreparedRequest { Method = "POST", Url = "https://postman-echo.com/post", Mode = BodyMode.FormData };
        multipart.Multipart.Add(new MultipartEntry { Name = "field", Value = "value" });
        var r4 = await engine.SendAsync(multipart, app, null, CancellationToken.None);
        Check("multipart accepted", r4.StatusCode == 200, r4.Error);

        var auth = new PreparedRequest { Method = "GET", Url = "https://postman-echo.com/basic-auth" };
        AuthApplier.Apply(auth, new AuthConfig { Type = AuthType.Basic, Username = "postman", Password = "password" });
        var r5 = await engine.SendAsync(auth, app, null, CancellationToken.None);
        Eq("basic auth accepted", r5.StatusCode, 200);

        var cookieReq = new PreparedRequest { Method = "GET", Url = "https://postman-echo.com/cookies/set?token=abc123" };
        await engine.SendAsync(cookieReq, app, null, CancellationToken.None);
        Check("cookie stored in the jar", engine.AllCookies().Any(c => c.Name == "token"));

        var notFound = new PreparedRequest { Method = "GET", Url = "https://postman-echo.com/status/404" };
        var r6 = await engine.SendAsync(notFound, app, null, CancellationToken.None);
        Eq("404 surfaced", r6.StatusCode, 404);

        var bad = new PreparedRequest { Method = "GET", Url = "https://this-host-does-not-exist-getman.invalid/" };
        var r7 = await engine.SendAsync(bad, app, null, CancellationToken.None);
        Check("dns failure reported as an error not a crash", r7.HasError, "no error captured");
    }

    private static async Task TestEndToEndAsync()
    {
        using var engine = new HttpEngine();
        var runner = new RequestRunner(engine);

        var collection = new CollectionNode { Kind = NodeKind.Collection, Name = "E2E" };
        collection.Variables.Add(new KeyValueItem("baseUrl", "https://postman-echo.com"));
        collection.PreRequestScript = "pm.variables.set('traceId', 'trace-' + pm.environment.get('seed'));";

        var node = new CollectionNode
        {
            Kind = NodeKind.Request,
            Name = "Echo",
            Parent = collection,
            Request = new RequestModel
            {
                Method = "POST",
                Url = "{{baseUrl}}/post",
                PreRequestScript = "pm.environment.set('generated', 'gen-' + pm.variables.get('traceId'));",
                TestScript = """
                    pm.test("status ok", () => pm.response.to.have.status(200));
                    pm.test("trace header echoed", function () {
                        const b = pm.response.json();
                        pm.expect(b.headers['x-trace']).to.equal(pm.variables.get('traceId'));
                    });
                    pm.test("body echoed", function () {
                        const b = pm.response.json();
                        pm.expect(b.data.token).to.equal('gen-' + pm.variables.get('traceId'));
                    });
                    """,
                Body = new RequestBody { Mode = BodyMode.Raw, RawLanguage = "json", Raw = "{\"token\":\"{{generated}}\"}" }
            }
        };
        node.Request.Headers.Add(new KeyValueItem("Content-Type", "application/json"));
        node.Request.Headers.Add(new KeyValueItem("X-Trace", "{{traceId}}"));
        collection.Children.Add(node);

        var vars = new VariableResolver();
        vars.EnvironmentVars["seed"] = "99";
        vars.LoadCollectionChain(collection);

        var result = await runner.ExecuteAsync(node, node.Request, vars, new AppSettings { RequestTimeoutMs = 30000 });

        if (result.Response == null || result.Response.HasError)
        {
            Console.WriteLine("  SKIP  end to end needs network: " + result.Response?.Error);
            return;
        }

        Eq("e2e status", result.Response.StatusCode, 200);
        Eq("collection prerequest ran", vars.LocalVars.GetValueOrDefault("traceId"), "trace-99");
        Eq("request prerequest ran", vars.EnvironmentVars.GetValueOrDefault("generated"), "gen-trace-99");
        Check("body variable resolved after scripts",
            result.Request.BodyText.Contains("gen-trace-99"), result.Request.BodyText);
        Check("header variable resolved after scripts",
            result.Request.GetHeader("X-Trace") == "trace-99", result.Request.GetHeader("X-Trace"));

        foreach (var t in result.Tests)
            Check("e2e test: " + t.Name, t.Status == TestStatus.Pass, t.Message);

        Check("console has the http line", result.Console.Any(c => c.Source == "HTTP"));
    }
}
