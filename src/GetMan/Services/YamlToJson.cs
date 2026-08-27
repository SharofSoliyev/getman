using System.IO;
using System.Text.Json;
using System.Text.Json.Nodes;
using YamlDotNet.RepresentationModel;

namespace GetMan.Services;

/// <summary>
/// Converts a YAML document to JSON so the rest of the import path only ever deals with
/// <see cref="JsonElement"/>. Most OpenAPI descriptions in the wild are YAML, and rewriting the
/// importer against a second document model to support them would be two things to keep in step.
/// </summary>
public static class YamlToJson
{
    /// <summary>Returns null when the text is not YAML, or is YAML this cannot represent as JSON.</summary>
    public static string Convert(string text)
    {
        try
        {
            var yaml = new YamlStream();
            using var reader = new StringReader(text);
            yaml.Load(reader);

            if (yaml.Documents.Count == 0) return null;

            var node = Node(yaml.Documents[0].RootNode, 0);
            return node?.ToJsonString();
        }
        catch
        {
            return null;
        }
    }

    private static JsonNode Node(YamlNode node, int depth)
    {
        // A cap rather than a cycle check: YAML anchors can point back at an ancestor, and the
        // representation model happily hands the same node out again.
        if (depth > 128) return null;

        switch (node)
        {
            case YamlMappingNode mapping:
                {
                    var result = new JsonObject();
                    foreach (var entry in mapping.Children)
                    {
                        var key = (entry.Key as YamlScalarNode)?.Value;
                        if (key == null) continue;
                        result[key] = Node(entry.Value, depth + 1);
                    }
                    return result;
                }

            case YamlSequenceNode sequence:
                {
                    var result = new JsonArray();
                    foreach (var child in sequence.Children) result.Add(Node(child, depth + 1));
                    return result;
                }

            case YamlScalarNode scalar:
                return Scalar(scalar);

            default:
                return null;
        }
    }

    /// <summary>
    /// A quoted scalar is always a string; an unquoted one is typed the way YAML types it, so
    /// <c>required: true</c> and <c>maxLength: 40</c> reach the importer as a boolean and a number.
    /// </summary>
    private static JsonNode Scalar(YamlScalarNode scalar)
    {
        if (scalar.Style is YamlDotNet.Core.ScalarStyle.SingleQuoted or YamlDotNet.Core.ScalarStyle.DoubleQuoted)
            return JsonValue.Create(scalar.Value);

        var value = scalar.Value;
        if (string.IsNullOrEmpty(value)) return JsonValue.Create(string.Empty);

        if (value is "null" or "Null" or "NULL" or "~") return null;
        if (value is "true" or "True" or "TRUE") return JsonValue.Create(true);
        if (value is "false" or "False" or "FALSE") return JsonValue.Create(false);

        if (long.TryParse(value, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var integer))
            return JsonValue.Create(integer);

        if (double.TryParse(value, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var number))
            return JsonValue.Create(number);

        return JsonValue.Create(value);
    }
}
