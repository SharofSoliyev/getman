using System.IO;
using System.Text;
using System.Text.Json;

namespace GetMan.Services;

/// <summary>
/// Reads the data file that drives a collection run: one dictionary per iteration, exposed to
/// scripts as <c>pm.iterationData</c> and to requests as <c>{{column}}</c>. Shared by the runner
/// window and the command line so both accept exactly the same files.
/// </summary>
public static class DataFile
{
    /// <summary>Picks the reader from the extension; anything that is not .json is read as CSV.</summary>
    public static List<Dictionary<string, string>> Read(string path) =>
        path.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? ReadJson(path) : ReadCsv(path);

    public static List<Dictionary<string, string>> ReadJson(string path)
    {
        var rows = new List<Dictionary<string, string>>();
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        if (doc.RootElement.ValueKind != JsonValueKind.Array) return rows;

        foreach (var element in doc.RootElement.EnumerateArray())
        {
            if (element.ValueKind != JsonValueKind.Object) continue;
            var row = new Dictionary<string, string>();
            foreach (var property in element.EnumerateObject())
                row[property.Name] = property.Value.ValueKind == JsonValueKind.String
                    ? property.Value.GetString()
                    : property.Value.ToString();
            rows.Add(row);
        }
        return rows;
    }

    public static List<Dictionary<string, string>> ReadCsv(string path)
    {
        var rows = new List<Dictionary<string, string>>();
        var lines = File.ReadAllLines(path);
        if (lines.Length < 2) return rows;

        var headers = SplitCsv(lines[0]);
        for (int i = 1; i < lines.Length; i++)
        {
            if (string.IsNullOrWhiteSpace(lines[i])) continue;
            var cells = SplitCsv(lines[i]);
            var row = new Dictionary<string, string>();
            for (int c = 0; c < headers.Count; c++)
                row[headers[c]] = c < cells.Count ? cells[c] : string.Empty;
            rows.Add(row);
        }
        return rows;
    }

    /// <summary>Splits one CSV line, honouring quoted cells and doubled quotes inside them.</summary>
    public static List<string> SplitCsv(string line)
    {
        var cells = new List<string>();
        var current = new StringBuilder();
        bool quoted = false;

        for (int i = 0; i < line.Length; i++)
        {
            var ch = line[i];
            if (quoted)
            {
                if (ch == '"')
                {
                    if (i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                    else quoted = false;
                }
                else current.Append(ch);
            }
            else if (ch == '"') quoted = true;
            else if (ch == ',') { cells.Add(current.ToString().Trim()); current.Clear(); }
            else current.Append(ch);
        }

        cells.Add(current.ToString().Trim());
        return cells;
    }
}
