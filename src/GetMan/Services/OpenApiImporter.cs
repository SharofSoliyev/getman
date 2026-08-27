using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using GetMan.Models;

namespace GetMan.Services;

/// <summary>
/// Turns an OpenAPI 3.x or Swagger 2.0 description into a collection: one folder per tag, one
/// request per operation, with parameters, an example body built from the schema, and the security
/// scheme mapped onto GetMan's auth. Both JSON and YAML come in through
/// <see cref="PostmanImporter"/>, which detects the format and hands over here.
/// </summary>
public static class OpenApiImporter
{
    /// <summary>Deep enough for a realistic payload, shallow enough that a recursive schema ends.</summary>
    private const int MaxExampleDepth = 8;

    public static bool Looks(JsonElement root) =>
        root.ValueKind == JsonValueKind.Object &&
        (root.TryGetProperty("openapi", out _) || root.TryGetProperty("swagger", out _)) &&
        root.TryGetProperty("paths", out var paths) && paths.ValueKind == JsonValueKind.Object;

    public static ImportResult Import(JsonElement root, string fallbackName)
    {
        var result = new ImportResult();

        var swagger2 = root.TryGetProperty("swagger", out var version) &&
                       (version.GetRawText().Contains("2.", StringComparison.Ordinal));

        var info = root.TryGetProperty("info", out var i) ? i : default;
        var title = Str(info, "title");

        var collection = new CollectionNode
        {
            Kind = NodeKind.Collection,
            Name = string.IsNullOrWhiteSpace(title) ? fallbackName : title,
            Description = Description(info),
            IsExpanded = true
        };

        var baseUrl = ReadServer(root, swagger2, collection, result);
        collection.Variables.Insert(0, new KeyValueItem("baseUrl", baseUrl));

        // Collection-level auth, so every request inherits it the way Postman's own import does.
        var schemes = swagger2
            ? Child(root, "securityDefinitions")
            : Child(Child(root, "components"), "securitySchemes");

        collection.Auth = ReadSecurity(root, schemes, collection, result) ?? new AuthConfig { Type = AuthType.None };

        var folders = new Dictionary<string, CollectionNode>(StringComparer.OrdinalIgnoreCase);
        var paths = Child(root, "paths");
        var operations = 0;

        if (paths.ValueKind == JsonValueKind.Object)
        {
            foreach (var path in paths.EnumerateObject())
            {
                if (path.Value.ValueKind != JsonValueKind.Object) continue;

                // Parameters declared once for the whole path apply to every operation under it.
                var shared = Child(path.Value, "parameters");

                foreach (var operation in path.Value.EnumerateObject())
                {
                    var method = operation.Name.ToUpperInvariant();
                    if (!IsMethod(method)) continue;
                    if (operation.Value.ValueKind != JsonValueKind.Object) continue;

                    var node = ReadOperation(root, swagger2, path.Name, method, operation.Value, shared,
                        schemes, result);
                    if (node == null) continue;

                    var folderName = FolderFor(operation.Value, path.Name);
                    var parent = collection;
                    if (folderName != null)
                    {
                        if (!folders.TryGetValue(folderName, out parent))
                        {
                            parent = new CollectionNode
                            {
                                Kind = NodeKind.Folder,
                                Name = folderName,
                                Parent = collection,
                                IsExpanded = true,
                                Description = TagDescription(root, folderName)
                            };
                            folders[folderName] = parent;
                            collection.Children.Add(parent);
                        }
                    }

                    node.Parent = parent;
                    parent.Children.Add(node);
                    operations++;
                }
            }
        }

        if (operations == 0)
        {
            result.Error = "This looks like an OpenAPI description but it declares no operations.";
            return result;
        }

        collection.FixupParents();
        result.Collections.Add(collection);

        // The spec's server becomes an environment too, so switching between staging and production
        // is a dropdown rather than an edit.
        var environment = new EnvironmentModel { Name = collection.Name + " (from OpenAPI)" };
        environment.Variables.Add(new KeyValueItem("baseUrl", baseUrl));
        result.Environments.Add(environment);

        return result;
    }

    // ------------------------------------------------------------------ servers

    private static string ReadServer(JsonElement root, bool swagger2, CollectionNode collection, ImportResult result)
    {
        if (swagger2)
        {
            var host = Str(root, "host");
            var basePath = Str(root, "basePath");
            var scheme = "https";

            var schemes = Child(root, "schemes");
            if (schemes.ValueKind == JsonValueKind.Array)
            {
                var first = schemes.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.String) scheme = first.GetString();
            }

            if (string.IsNullOrEmpty(host))
            {
                result.Warnings.Add("The description has no host; baseUrl is empty and needs filling in.");
                return string.Empty;
            }
            return $"{scheme}://{host}{basePath}".TrimEnd('/');
        }

        var servers = Child(root, "servers");
        if (servers.ValueKind != JsonValueKind.Array)
        {
            result.Warnings.Add("The description has no servers; baseUrl is empty and needs filling in.");
            return string.Empty;
        }

        var server = servers.EnumerateArray().FirstOrDefault();
        var url = Str(server, "url");

        // A templated server URL such as https://{region}.api.example.com becomes collection
        // variables seeded with the declared defaults.
        var variables = Child(server, "variables");
        if (variables.ValueKind == JsonValueKind.Object)
        {
            foreach (var variable in variables.EnumerateObject())
            {
                var fallback = Str(variable.Value, "default");
                if (string.IsNullOrEmpty(fallback))
                {
                    var enumerated = Child(variable.Value, "enum");
                    if (enumerated.ValueKind == JsonValueKind.Array)
                    {
                        var first = enumerated.EnumerateArray().FirstOrDefault();
                        if (first.ValueKind == JsonValueKind.String) fallback = first.GetString();
                    }
                }
                collection.Variables.Add(new KeyValueItem(variable.Name, fallback)
                {
                    Description = Description(variable.Value)
                });
                url = url.Replace("{" + variable.Name + "}", "{{" + variable.Name + "}}", StringComparison.Ordinal);
            }
        }

        if (servers.GetArrayLength() > 1)
            result.Warnings.Add($"The description lists {servers.GetArrayLength()} servers; the first one became baseUrl.");

        return (url ?? string.Empty).TrimEnd('/');
    }

    // --------------------------------------------------------------- operations

    private static bool IsMethod(string method) => method is
        "GET" or "POST" or "PUT" or "PATCH" or "DELETE" or "HEAD" or "OPTIONS" or "TRACE";

    private static string FolderFor(JsonElement operation, string path)
    {
        var tags = Child(operation, "tags");
        if (tags.ValueKind == JsonValueKind.Array)
        {
            var first = tags.EnumerateArray().FirstOrDefault();
            if (first.ValueKind == JsonValueKind.String && !string.IsNullOrWhiteSpace(first.GetString()))
                return first.GetString();
        }

        // No tag: group by the first fixed path segment, which is how most APIs are laid out anyway.
        foreach (var segment in path.Split('/', StringSplitOptions.RemoveEmptyEntries))
            if (!segment.StartsWith('{')) return segment;

        return null;
    }

    private static string TagDescription(JsonElement root, string name)
    {
        var tags = Child(root, "tags");
        if (tags.ValueKind != JsonValueKind.Array) return string.Empty;

        foreach (var tag in tags.EnumerateArray())
            if (string.Equals(Str(tag, "name"), name, StringComparison.OrdinalIgnoreCase))
                return Description(tag);

        return string.Empty;
    }

    private static CollectionNode ReadOperation(JsonElement root, bool swagger2, string path, string method,
        JsonElement operation, JsonElement sharedParameters, JsonElement schemes, ImportResult result)
    {
        var request = new RequestModel
        {
            Method = method,
            Description = Description(operation),
            Auth = new AuthConfig { Type = AuthType.Inherit }
        };

        // GetMan writes path variables as :name, OpenAPI writes them as {name}.
        var template = new StringBuilder("{{baseUrl}}");
        foreach (var segment in path.Split('/'))
        {
            if (segment.Length == 0) continue;
            template.Append('/');
            template.Append(segment.StartsWith('{') && segment.EndsWith('}')
                ? ":" + segment[1..^1]
                : segment);
        }

        var query = new List<KeyValueItem>();

        void ReadParameters(JsonElement list)
        {
            if (list.ValueKind != JsonValueKind.Array) return;
            foreach (var raw in list.EnumerateArray())
            {
                var parameter = Deref(root, raw);
                var name = Str(parameter, "name");
                if (string.IsNullOrEmpty(name)) continue;

                var required = Bool(parameter, "required");
                var where = Str(parameter, "in");
                var schema = swagger2 ? parameter : Child(parameter, "schema");
                var value = ScalarExample(root, schema, name);

                var item = new KeyValueItem(name, value)
                {
                    Description = Description(parameter),
                    Enabled = required
                };

                switch (where)
                {
                    case "path":
                        item.Enabled = true;
                        if (!request.PathVariables.Any(p => p.Key == name)) request.PathVariables.Add(item);
                        break;
                    case "query":
                        if (!query.Any(p => p.Key == name)) query.Add(item);
                        break;
                    case "header":
                        if (!request.Headers.Any(h => h.Key == name)) request.Headers.Add(item);
                        break;
                    case "formData" when swagger2:
                        request.Body.Mode = BodyMode.UrlEncoded;
                        item.Enabled = required;
                        request.Body.UrlEncoded.Add(item);
                        break;
                    case "body" when swagger2:
                        WriteJsonBody(request, root, Child(parameter, "schema"));
                        break;
                }
            }
        }

        ReadParameters(sharedParameters);
        ReadParameters(Child(operation, "parameters"));

        if (!swagger2) ReadRequestBody(request, root, Child(operation, "requestBody"), result, path, method);

        foreach (var item in query) request.QueryParams.Add(item);

        // The URL carries the enabled parameters; disabled rows survive in the table, which is
        // exactly how an imported Postman collection behaves.
        var enabled = UrlUtil.BuildQuery(query, s => s, false);
        request.Url = enabled.Length > 0 ? template + "?" + enabled : template.ToString();

        // A per-operation security requirement overrides the collection default.
        var operationSecurity = ReadSecurityFor(root, Child(operation, "security"), schemes, null, result);
        if (operationSecurity != null) request.Auth = operationSecurity;

        var name = Str(operation, "summary");
        if (string.IsNullOrWhiteSpace(name)) name = Str(operation, "operationId");
        if (string.IsNullOrWhiteSpace(name)) name = $"{method} {path}";

        return new CollectionNode
        {
            Kind = NodeKind.Request,
            Name = name.Trim(),
            Description = request.Description,
            Request = request
        };
    }

    private static void ReadRequestBody(RequestModel request, JsonElement root, JsonElement body,
        ImportResult result, string path, string method)
    {
        body = Deref(root, body);
        var content = Child(body, "content");
        if (content.ValueKind != JsonValueKind.Object) return;

        // Preference order matches what an API actually expects most often.
        foreach (var type in new[] { "application/json", "application/x-www-form-urlencoded", "multipart/form-data" })
        {
            foreach (var media in content.EnumerateObject())
            {
                if (!media.Name.StartsWith(type, StringComparison.OrdinalIgnoreCase)) continue;
                var schema = Child(media.Value, "schema");

                switch (type)
                {
                    case "application/json":
                        request.Headers.Add(new KeyValueItem("Content-Type", "application/json"));
                        WriteJsonBody(request, root, schema, Child(media.Value, "example"));
                        return;

                    case "application/x-www-form-urlencoded":
                        request.Body.Mode = BodyMode.UrlEncoded;
                        WriteFormRows(request.Body.UrlEncoded, root, schema);
                        return;

                    case "multipart/form-data":
                        request.Body.Mode = BodyMode.FormData;
                        WriteFormRows(request.Body.FormData, root, schema, allowFiles: true);
                        return;
                }
            }
        }

        var first = content.EnumerateObject().FirstOrDefault();
        if (first.Value.ValueKind == JsonValueKind.Object)
            result.Warnings.Add($"{method} {path}: body type '{first.Name}' is not one GetMan builds, so the body was left empty.");
    }

    private static void WriteJsonBody(RequestModel request, JsonElement root, JsonElement schema,
        JsonElement example = default)
    {
        JsonNode node = example.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null
            ? Example(root, schema, 0, new HashSet<string>(StringComparer.Ordinal))
            : JsonNode.Parse(example.GetRawText());

        if (node == null) return;

        request.Body.Mode = BodyMode.Raw;
        request.Body.RawLanguage = "json";
        request.Body.Raw = node.ToJsonString(new JsonSerializerOptions { WriteIndented = true });

        if (!request.Headers.Any(h => string.Equals(h.Key, "Content-Type", StringComparison.OrdinalIgnoreCase)))
            request.Headers.Add(new KeyValueItem("Content-Type", "application/json"));
    }

    private static void WriteFormRows(IList<KeyValueItem> rows, JsonElement root, JsonElement schema,
        bool allowFiles = false)
    {
        schema = Deref(root, schema);
        var properties = Child(schema, "properties");
        if (properties.ValueKind != JsonValueKind.Object) return;

        var required = RequiredNames(schema);

        foreach (var property in properties.EnumerateObject())
        {
            var field = Deref(root, property.Value);
            var isFile = allowFiles && Str(field, "format") == "binary";

            rows.Add(new KeyValueItem(property.Name, isFile ? string.Empty : ScalarExample(root, field, property.Name))
            {
                Description = Description(field),
                Enabled = required.Count == 0 || required.Contains(property.Name),
                Kind = isFile ? ParamKind.File : ParamKind.Text
            });
        }
    }

    // ------------------------------------------------------------------ security

    private static AuthConfig ReadSecurity(JsonElement root, JsonElement schemes, CollectionNode collection,
        ImportResult result) =>
        ReadSecurityFor(root, Child(root, "security"), schemes, collection, result);

    /// <summary>
    /// Maps the first requirement that GetMan can actually send. An OAuth2 scheme brings its URLs
    /// across but not a client secret, which is not in the description and never should be.
    /// </summary>
    private static AuthConfig ReadSecurityFor(JsonElement root, JsonElement security, JsonElement schemes,
        CollectionNode collection, ImportResult result)
    {
        if (security.ValueKind != JsonValueKind.Array || schemes.ValueKind != JsonValueKind.Object) return null;

        foreach (var requirement in security.EnumerateArray())
        {
            if (requirement.ValueKind != JsonValueKind.Object) continue;

            foreach (var entry in requirement.EnumerateObject())
            {
                if (!schemes.TryGetProperty(entry.Name, out var raw)) continue;
                var scheme = Deref(root, raw);

                var kind = Str(scheme, "type").ToLowerInvariant();
                var name = Str(scheme, "scheme").ToLowerInvariant();

                switch (kind)
                {
                    case "http" when name == "bearer":
                        Variable(collection, "bearerToken");
                        return new AuthConfig { Type = AuthType.Bearer, Token = "{{bearerToken}}" };

                    case "http" when name == "basic":
                    case "basic":
                        Variable(collection, "username");
                        Variable(collection, "password");
                        return new AuthConfig
                        {
                            Type = AuthType.Basic,
                            Username = "{{username}}",
                            Password = "{{password}}"
                        };

                    case "apikey":
                        {
                            var header = Str(scheme, "name");
                            var where = Str(scheme, "in");
                            if (where == "cookie")
                            {
                                result?.Warnings.Add($"Security scheme '{entry.Name}' puts its key in a cookie, which GetMan does not send as auth.");
                                continue;
                            }
                            Variable(collection, "apiKey");
                            return new AuthConfig
                            {
                                Type = AuthType.ApiKey,
                                ApiKeyName = string.IsNullOrEmpty(header) ? "X-API-Key" : header,
                                ApiKeyValue = "{{apiKey}}",
                                ApiKeyLocation = where == "query" ? "query" : "header"
                            };
                        }

                    case "oauth2":
                        {
                            var flows = Child(scheme, "flows");
                            var auth = new AuthConfig { Type = AuthType.OAuth2 };

                            // OpenAPI 3 nests the flows; Swagger 2 puts them on the scheme itself.
                            var (flowName, flow) = FirstFlow(flows, scheme);
                            auth.OauthGrantType = flowName switch
                            {
                                "clientCredentials" or "application" => "client_credentials",
                                "password" => "password",
                                _ => "authorization_code"
                            };
                            auth.OauthAccessTokenUrl = Str(flow, "tokenUrl");
                            auth.OauthAuthUrl = Str(flow, "authorizationUrl");
                            auth.OauthScope = string.Join(' ', Scopes(entry.Value, flow));
                            Variable(collection, "clientId");
                            Variable(collection, "clientSecret");
                            auth.OauthClientId = "{{clientId}}";
                            auth.OauthClientSecret = "{{clientSecret}}";
                            return auth;
                        }

                    default:
                        result?.Warnings.Add($"Security scheme '{entry.Name}' is of type '{kind}', which GetMan has no equivalent for.");
                        continue;
                }
            }
        }

        return null;
    }

    private static (string Name, JsonElement Flow) FirstFlow(JsonElement flows, JsonElement scheme)
    {
        if (flows.ValueKind == JsonValueKind.Object)
        {
            foreach (var flow in flows.EnumerateObject())
                if (flow.Value.ValueKind == JsonValueKind.Object)
                    return (flow.Name, flow.Value);
        }
        return (Str(scheme, "flow"), scheme);
    }

    private static IEnumerable<string> Scopes(JsonElement requested, JsonElement flow)
    {
        if (requested.ValueKind == JsonValueKind.Array)
        {
            var listed = requested.EnumerateArray()
                .Where(s => s.ValueKind == JsonValueKind.String)
                .Select(s => s.GetString())
                .ToList();
            if (listed.Count > 0) return listed;
        }

        var declared = Child(flow, "scopes");
        return declared.ValueKind == JsonValueKind.Object
            ? declared.EnumerateObject().Select(p => p.Name)
            : Array.Empty<string>();
    }

    /// <summary>Adds an empty collection variable to fill in, rather than inventing a credential.</summary>
    private static void Variable(CollectionNode collection, string name)
    {
        if (collection == null) return;
        if (!collection.Variables.Any(v => v.Key == name))
            collection.Variables.Add(new KeyValueItem(name, string.Empty));
    }

    // -------------------------------------------------------------- schema to example

    private static JsonNode Example(JsonElement root, JsonElement schema, int depth, HashSet<string> seen)
    {
        if (depth > MaxExampleDepth) return null;

        // $ref is followed once per branch; a schema that refers to itself would otherwise not end.
        if (schema.ValueKind == JsonValueKind.Object && schema.TryGetProperty("$ref", out var reference)
            && reference.ValueKind == JsonValueKind.String)
        {
            var pointer = reference.GetString();
            if (!seen.Add(pointer)) return null;
            var resolved = Resolve(root, pointer);
            var node = Example(root, resolved, depth + 1, seen);
            seen.Remove(pointer);
            return node;
        }

        if (schema.ValueKind != JsonValueKind.Object) return null;

        if (schema.TryGetProperty("example", out var example) && example.ValueKind != JsonValueKind.Null)
            return JsonNode.Parse(example.GetRawText());

        // allOf composes; oneOf and anyOf are a choice, and the first branch is the useful default.
        var allOf = Child(schema, "allOf");
        if (allOf.ValueKind == JsonValueKind.Array)
        {
            var merged = new JsonObject();
            foreach (var part in allOf.EnumerateArray())
            {
                if (Example(root, part, depth + 1, seen) is not JsonObject piece) continue;
                foreach (var property in piece.ToList())
                {
                    piece.Remove(property.Key);
                    merged[property.Key] = property.Value;
                }
            }
            return merged;
        }

        foreach (var choice in new[] { "oneOf", "anyOf" })
        {
            var branch = Child(schema, choice);
            if (branch.ValueKind == JsonValueKind.Array)
            {
                var first = branch.EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object) return Example(root, first, depth + 1, seen);
            }
        }

        var type = Str(schema, "type");
        if (string.IsNullOrEmpty(type))
            type = schema.TryGetProperty("properties", out _) ? "object"
                 : schema.TryGetProperty("items", out _) ? "array"
                 : "string";

        switch (type)
        {
            case "object":
                {
                    var node = new JsonObject();
                    var properties = Child(schema, "properties");
                    if (properties.ValueKind == JsonValueKind.Object)
                        foreach (var property in properties.EnumerateObject())
                            node[property.Name] = Example(root, property.Value, depth + 1, seen) ?? JsonValue.Create((string)null);
                    return node;
                }

            case "array":
                {
                    var items = Child(schema, "items");
                    var element = Example(root, items, depth + 1, seen);
                    return element == null ? new JsonArray() : new JsonArray(element);
                }

            case "integer":
            case "number":
                return Scalar(schema) is { } number && double.TryParse(number, out var parsed)
                    ? JsonValue.Create(parsed)
                    : JsonValue.Create(0);

            case "boolean":
                return JsonValue.Create(Scalar(schema) is not { } flag || flag != "false");

            default:
                return JsonValue.Create(Scalar(schema) ?? FormatSample(Str(schema, "format")));
        }
    }

    /// <summary>The value the description itself suggests: an example, a default, or the first enum member.</summary>
    private static string Scalar(JsonElement schema)
    {
        if (schema.TryGetProperty("default", out var fallback) && fallback.ValueKind != JsonValueKind.Null)
            return Raw(fallback);

        var enumerated = Child(schema, "enum");
        if (enumerated.ValueKind == JsonValueKind.Array)
        {
            var first = enumerated.EnumerateArray().FirstOrDefault();
            if (first.ValueKind != JsonValueKind.Undefined) return Raw(first);
        }

        return null;
    }

    private static string ScalarExample(JsonElement root, JsonElement schema, string name)
    {
        schema = Deref(root, schema);
        if (schema.ValueKind != JsonValueKind.Object) return string.Empty;

        if (schema.TryGetProperty("example", out var example) && example.ValueKind != JsonValueKind.Null)
            return Raw(example);

        if (Scalar(schema) is { } suggested) return suggested;

        var format = FormatSample(Str(schema, "format"));
        if (format != null) return format;

        return Str(schema, "type") switch
        {
            "integer" or "number" => "0",
            "boolean" => "true",
            "array" => string.Empty,
            _ => string.Empty
        };
    }

    private static string FormatSample(string format) => format switch
    {
        "date" => "2026-01-31",
        "date-time" => "2026-01-31T09:00:00Z",
        "uuid" => "{{$guid}}",
        "email" => "user@example.com",
        "uri" or "url" => "https://example.com",
        "password" => string.Empty,
        "byte" => string.Empty,
        "binary" => string.Empty,
        _ => format == null ? "string" : "string"
    };

    // -------------------------------------------------------------------- helpers

    /// <summary>
    /// Walks a local JSON pointer such as <c>#/components/schemas/Pet</c>. External refs are not
    /// followed: they would mean fetching another file, which an import should not do quietly.
    /// </summary>
    private static JsonElement Resolve(JsonElement root, string pointer)
    {
        if (string.IsNullOrEmpty(pointer) || !pointer.StartsWith('#')) return default;

        var current = root;
        foreach (var rawSegment in pointer[1..].Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            var segment = rawSegment.Replace("~1", "/", StringComparison.Ordinal)
                                    .Replace("~0", "~", StringComparison.Ordinal);
            if (current.ValueKind != JsonValueKind.Object || !current.TryGetProperty(segment, out current))
                return default;
        }
        return current;
    }

    private static JsonElement Deref(JsonElement root, JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Object && element.TryGetProperty("$ref", out var reference)
            && reference.ValueKind == JsonValueKind.String)
            return Resolve(root, reference.GetString());
        return element;
    }

    private static HashSet<string> RequiredNames(JsonElement schema)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        var required = Child(schema, "required");
        if (required.ValueKind == JsonValueKind.Array)
            foreach (var name in required.EnumerateArray())
                if (name.ValueKind == JsonValueKind.String) names.Add(name.GetString());
        return names;
    }

    private static JsonElement Child(JsonElement parent, string name) =>
        parent.ValueKind == JsonValueKind.Object && parent.TryGetProperty(name, out var value) ? value : default;

    private static string Str(JsonElement parent, string name)
    {
        var value = Child(parent, name);
        return value.ValueKind == JsonValueKind.String ? value.GetString() : string.Empty;
    }

    private static bool Bool(JsonElement parent, string name) =>
        Child(parent, name).ValueKind == JsonValueKind.True;

    private static string Raw(JsonElement value) =>
        value.ValueKind == JsonValueKind.String ? value.GetString() : value.GetRawText();

    private static string Description(JsonElement element)
    {
        var text = Str(element, "description");
        if (!string.IsNullOrEmpty(text)) return text;
        return Str(element, "summary");
    }
}
