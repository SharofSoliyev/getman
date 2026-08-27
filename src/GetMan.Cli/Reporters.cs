using System.Text;
using System.Text.Json;
using System.Xml;
using GetMan.Models;
using GetMan.Services;

namespace GetMan.Cli;

public static class Reporters
{
    public static string Render(RunReport report, string reporter) => reporter switch
    {
        "json" => Json(report),
        "junit" => JUnit(report),
        _ => string.Empty // the cli reporter writes as it goes, so there is nothing to render here
    };

    // ------------------------------------------------------------------ cli

    /// <summary>
    /// Prints one line per request as it finishes, so a long run is watchable rather than silent.
    /// Colour goes through Console.ForegroundColor, which Windows ignores when output is redirected.
    /// </summary>
    public sealed class Console_ : IDisposable
    {
        private readonly bool _color;
        private readonly bool _unicode;

        public Console_(bool color)
        {
            _color = color && !Console.IsOutputRedirected;

            // Tick marks need a UTF-8 code page; a redirected pipe or an old console may refuse.
            bool unicode;
            try { Console.OutputEncoding = Encoding.UTF8; unicode = true; }
            catch { unicode = false; }
            _unicode = unicode;
        }

        public string Pass => _unicode ? "✓" : "+";
        public string Fail => _unicode ? "✗" : "x";

        public void Write(string text, ConsoleColor? color = null)
        {
            if (_color && color.HasValue) Console.ForegroundColor = color.Value;
            Console.Write(text);
            if (_color && color.HasValue) Console.ResetColor();
        }

        public void Line(string text = "", ConsoleColor? color = null)
        {
            Write(text, color);
            Console.WriteLine();
        }

        public void Dispose()
        {
            if (_color) Console.ResetColor();
        }
    }

    public static void WriteItem(Console_ console, RunItem item, bool showIteration)
    {
        var failed = item.Failed;
        var prefix = showIteration ? $"[{item.Iteration + 1}] " : string.Empty;

        console.Write("  ");
        console.Write(failed ? console.Fail : console.Pass, failed ? ConsoleColor.Red : ConsoleColor.Green);
        console.Write($"  {item.Method,-6} {prefix}{item.Name}");

        if (!string.IsNullOrEmpty(item.Error))
        {
            console.Line("   " + item.Error, ConsoleColor.Red);
            return;
        }

        var status = $"{item.StatusCode} {item.StatusText}".Trim();
        console.Write("   ");
        console.Write(status, item.StatusCode >= 400 ? ConsoleColor.Red : ConsoleColor.Green);
        console.Line($"   {TextFormatter.HumanTime(item.ElapsedMs)}   {TextFormatter.HumanSize(item.SizeBytes)}");

        foreach (var test in item.Tests)
        {
            var ok = test.Status == TestStatus.Pass;
            console.Write("       ");
            console.Write(ok ? console.Pass : console.Fail, ok ? ConsoleColor.Green : ConsoleColor.Red);
            console.Write(" " + test.Name);
            if (!ok && !string.IsNullOrWhiteSpace(test.Message))
                console.Write("  " + test.Message.Replace("\n", " ").Trim(), ConsoleColor.Red);
            console.Line();
        }
    }

    public static void WriteSummary(Console_ console, RunReport report)
    {
        console.Line();
        console.Write($"  {report.Requests} request(s), {report.Assertions} assertion(s), ");
        console.Write($"{report.Passed} passed", ConsoleColor.Green);

        if (report.Failed > 0) { console.Write(", "); console.Write($"{report.Failed} failed", ConsoleColor.Red); }
        if (report.Skipped > 0) console.Write($", {report.Skipped} skipped");
        if (report.RequestsFailed > 0)
        {
            console.Write(", ");
            console.Write($"{report.RequestsFailed} request(s) errored", ConsoleColor.Red);
        }

        console.Line();
        console.Line($"  total {TextFormatter.HumanTime(report.TotalMs)}");
        console.Line();
    }

    // ----------------------------------------------------------------- json

    private static string Json(RunReport report) =>
        JsonSerializer.Serialize(report, new JsonSerializerOptions
        {
            WriteIndented = true,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        });

    // ---------------------------------------------------------------- junit

    /// <summary>
    /// One testsuite per request and one testcase per assertion, which is the shape every CI
    /// server understands. A request that never got a response becomes a failing testcase of its
    /// own, otherwise a DNS failure would be reported as a green run with no tests.
    /// </summary>
    private static string JUnit(RunReport report)
    {
        var buffer = new StringBuilder();

        // Writing to a StringBuilder makes XmlWriter declare utf-16 whatever Encoding says, and
        // some CI parsers refuse the file over that. The declaration is written by hand instead.
        buffer.Append("<?xml version=\"1.0\" encoding=\"utf-8\"?>").Append('\n');

        var settings = new XmlWriterSettings { Indent = true, OmitXmlDeclaration = true };
        using var writer = XmlWriter.Create(buffer, settings);

        writer.WriteStartElement("testsuites");
        writer.WriteAttributeString("name", report.Collection);
        writer.WriteAttributeString("tests", (report.Assertions + report.RequestsFailed).ToString());
        writer.WriteAttributeString("failures", (report.Failed + report.RequestsFailed).ToString());
        writer.WriteAttributeString("time", Seconds(report.TotalMs));

        foreach (var item in report.Items)
        {
            var name = report.Iterations > 1 ? $"[{item.Iteration + 1}] {item.Path}" : item.Path;
            var errored = !string.IsNullOrEmpty(item.Error);

            writer.WriteStartElement("testsuite");
            writer.WriteAttributeString("name", name);
            writer.WriteAttributeString("tests", (item.Tests.Count + (errored ? 1 : 0)).ToString());
            writer.WriteAttributeString("failures",
                (item.Tests.Count(t => t.Status == TestStatus.Fail) + (errored ? 1 : 0)).ToString());
            writer.WriteAttributeString("skipped", item.Tests.Count(t => t.Status == TestStatus.Skip).ToString());
            writer.WriteAttributeString("time", Seconds(item.ElapsedMs));

            if (errored)
            {
                writer.WriteStartElement("testcase");
                writer.WriteAttributeString("name", $"{item.Method} {item.Url}");
                writer.WriteAttributeString("classname", name);
                writer.WriteStartElement("failure");
                writer.WriteAttributeString("message", item.Error);
                writer.WriteAttributeString("type", "request");
                writer.WriteString(item.Error);
                writer.WriteEndElement();
                writer.WriteEndElement();
            }

            foreach (var test in item.Tests)
            {
                writer.WriteStartElement("testcase");
                writer.WriteAttributeString("name", test.Name);
                writer.WriteAttributeString("classname", name);
                writer.WriteAttributeString("time", Seconds(test.DurationMs));

                if (test.Status == TestStatus.Fail)
                {
                    writer.WriteStartElement("failure");
                    writer.WriteAttributeString("message", Trim(test.Message));
                    writer.WriteAttributeString("type", "assertion");
                    writer.WriteString(test.Message ?? string.Empty);
                    writer.WriteEndElement();
                }
                else if (test.Status == TestStatus.Skip)
                {
                    writer.WriteStartElement("skipped");
                    writer.WriteEndElement();
                }

                writer.WriteEndElement();
            }

            writer.WriteEndElement();
        }

        writer.WriteEndElement();
        writer.Flush();
        return buffer.ToString();
    }

    private static string Seconds(double ms) =>
        (ms / 1000.0).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);

    private static string Trim(string message)
    {
        message = (message ?? string.Empty).Replace("\r", " ").Replace("\n", " ").Trim();
        return message.Length > 240 ? message[..237] + "..." : message;
    }
}
