using System.Diagnostics;
using System.IO;
using GetMan.Models;
using GetMan.Services;

namespace GetMan.Cli;

/// <summary>
/// Headless collection runner. It drives exactly the same engine the window does -
/// <see cref="RequestRunner"/>, <see cref="VariableResolver"/> and the Jint script runtime - so a
/// collection that passes here passes in the app, and the other way round.
/// </summary>
public static class Program
{
    private const int ExitOk = 0;
    private const int ExitTestsFailed = 1;
    private const int ExitUsage = 2;

    public static async Task<int> Main(string[] args)
    {
        if (args.Length == 0 || args.Contains("-h") || args.Contains("--help"))
        {
            PrintUsage();
            return args.Length == 0 ? ExitUsage : ExitOk;
        }

        if (args.Contains("--version"))
        {
            Console.WriteLine(Version());
            return ExitOk;
        }

        // `getman run collection.json` and `getman collection.json` both work; the verb is there
        // for the day a second one exists.
        var rest = args[0] == "run" ? args[1..] : args;

        var (options, error) = Options.Parse(rest);
        if (options == null)
        {
            Console.Error.WriteLine("getman: " + error);
            Console.Error.WriteLine("Try 'getman --help'.");
            return ExitUsage;
        }

        if (!string.IsNullOrEmpty(options.Language)) Loc.Instance.SetLanguage(options.Language);

        try
        {
            return await RunAsync(options);
        }
        catch (FileNotFoundException ex)
        {
            Console.Error.WriteLine("getman: " + ex.Message);
            return ExitUsage;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("getman: " + ex.Message);
            return ExitUsage;
        }
    }

    private static async Task<int> RunAsync(Options options)
    {
        var collection = LoadCollection(options.Collection, out var loadError);
        if (collection == null)
        {
            Console.Error.WriteLine("getman: " + loadError);
            return ExitUsage;
        }

        var target = collection;
        if (!string.IsNullOrEmpty(options.Folder))
        {
            target = collection.Flatten().FirstOrDefault(n =>
                n.Kind != NodeKind.Request &&
                string.Equals(n.Name, options.Folder, StringComparison.OrdinalIgnoreCase));

            if (target == null)
            {
                Console.Error.WriteLine($"getman: no folder named '{options.Folder}' in this collection");
                return ExitUsage;
            }
        }

        var requests = target.Flatten().Where(n => n.Kind == NodeKind.Request && n.Request != null).ToList();
        if (requests.Count == 0)
        {
            Console.Error.WriteLine("getman: nothing to run - the collection has no requests");
            return ExitUsage;
        }

        var environment = LoadEnvironment(options.Environments, out var envError);
        if (envError != null)
        {
            Console.Error.WriteLine("getman: " + envError);
            return ExitUsage;
        }

        var globals = string.IsNullOrEmpty(options.Globals)
            ? new EnvironmentModel { Name = "Globals", IsGlobal = true }
            : LoadEnvironment(new List<string> { options.Globals }, out _) ??
              new EnvironmentModel { Name = "Globals", IsGlobal = true };

        // --var wins over the environment file, which is what a CI job overriding one value expects.
        environment ??= new EnvironmentModel { Name = "CLI" };
        foreach (var (key, value) in options.Variables)
        {
            var existing = environment.Variables.FirstOrDefault(v => v.Key == key);
            if (existing != null) existing.Value = value;
            else environment.Variables.Add(new KeyValueItem(key, value));
        }

        var data = string.IsNullOrEmpty(options.DataFile)
            ? new List<Dictionary<string, string>>()
            : DataFile.Read(options.DataFile);

        var iterations = options.Iterations ?? (data.Count > 0 ? data.Count : 1);

        var settings = new AppSettings
        {
            SslVerification = !options.Insecure,
            RequestTimeoutMs = options.TimeoutMs ?? 0,
            ScriptTimeoutMs = options.ScriptTimeoutMs ?? 5000
        };

        var engine = new HttpEngine();
        var runner = new RequestRunner(engine);

        var report = new RunReport
        {
            Collection = collection.Name,
            StartedAt = DateTime.Now.ToString("O"),
            Iterations = iterations
        };

        var live = options.Reporter == "cli";
        using var console = new Reporters.Console_(!options.NoColor);

        if (live)
        {
            console.Line();
            console.Line($"  {Version()} - running \"{target.PathString()}\"");
            if (iterations > 1) console.Line($"  {iterations} iterations" + (data.Count > 0 ? $" from {data.Count} data row(s)" : string.Empty));
            console.Line();
        }

        var clock = Stopwatch.StartNew();
        var stop = false;

        for (int iteration = 0; iteration < iterations && !stop; iteration++)
        {
            int index = 0;
            while (index < requests.Count && !stop)
            {
                var node = requests[index];

                var vars = new VariableResolver();
                vars.LoadGlobals(globals);
                vars.LoadEnvironment(environment);
                vars.LoadCollectionChain(node.Parent ?? node);

                if (data.Count > 0)
                    foreach (var (key, value) in data[iteration % data.Count])
                        vars.DataVars[key] = value;

                var result = await runner.ExecuteAsync(node, node.Request, vars, settings,
                    iteration, iterations);

                ApplyVariableOps(result.VariableOps, node, globals, environment);

                var item = new RunItem
                {
                    Iteration = iteration,
                    Name = node.Name,
                    Path = node.PathString(),
                    Method = result.Request?.Method ?? node.Request.Method,
                    Url = result.Request?.Url ?? node.Request.Url,
                    StatusCode = result.Response?.StatusCode ?? 0,
                    StatusText = result.Response?.StatusText ?? string.Empty,
                    ElapsedMs = result.Response?.ElapsedMs ?? 0,
                    SizeBytes = result.Response?.SizeBytes ?? 0,
                    Error = result.Response?.Error ?? (result.Response == null ? "no response" : string.Empty)
                };
                item.Tests.AddRange(result.Tests);
                report.Items.Add(item);

                if (live) Reporters.WriteItem(console, item, iterations > 1);

                if (options.Bail && item.Failed) { stop = true; break; }

                // pm.execution.setNextRequest, same as the window honours it.
                if (!string.IsNullOrEmpty(result.NextRequest))
                {
                    var jump = requests.FindIndex(r =>
                        string.Equals(r.Name, result.NextRequest, StringComparison.OrdinalIgnoreCase));
                    if (jump >= 0) { index = jump; continue; }
                }

                index++;
                if (options.Delay > 0 && index < requests.Count) await Task.Delay(options.Delay);
            }
        }

        clock.Stop();
        report.TotalMs = clock.Elapsed.TotalMilliseconds;

        if (live) Reporters.WriteSummary(console, report);

        var rendered = Reporters.Render(report, options.Reporter);
        if (!string.IsNullOrEmpty(options.Output))
        {
            var directory = Path.GetDirectoryName(Path.GetFullPath(options.Output));
            if (!string.IsNullOrEmpty(directory)) Directory.CreateDirectory(directory);
            PersistenceService.ExportToFile(options.Output, rendered.Length > 0 ? rendered : Reporters.Render(report, "json"));
            if (live) console.Line($"  report written to {options.Output}");
        }
        else if (rendered.Length > 0)
        {
            Console.WriteLine(rendered);
        }

        return report.Success ? ExitOk : ExitTestsFailed;
    }

    /// <summary>Scripts may set variables; without this a chained run loses the token it just fetched.</summary>
    private static void ApplyVariableOps(IEnumerable<VariableOp> ops, CollectionNode owner,
        EnvironmentModel globals, EnvironmentModel environment)
    {
        foreach (var op in ops)
        {
            var target = op.Scope switch
            {
                "globals" => globals,
                "collection" => null,
                _ => environment
            };

            if (op.Scope == "collection")
            {
                var collection = owner.AncestorsAndSelf().LastOrDefault();
                if (collection != null) ApplyOpTo(collection.Variables, op);
                continue;
            }

            if (target != null) ApplyOpTo(target.Variables, op);
        }
    }

    private static void ApplyOpTo(IList<KeyValueItem> variables, VariableOp op)
    {
        switch (op.Op)
        {
            case "clear":
                variables.Clear();
                return;
            case "unset":
                {
                    var existing = variables.FirstOrDefault(v => v.Key == op.Key);
                    if (existing != null) variables.Remove(existing);
                    return;
                }
            default:
                {
                    var existing = variables.FirstOrDefault(v => v.Key == op.Key);
                    if (existing != null) existing.Value = op.Value;
                    else variables.Add(new KeyValueItem(op.Key, op.Value));
                    return;
                }
        }
    }

    private static CollectionNode LoadCollection(string path, out string error)
    {
        error = null;
        if (!File.Exists(path)) { error = $"no such file: {path}"; return null; }

        var result = PostmanImporter.ImportFile(path);
        if (!string.IsNullOrEmpty(result.Error)) { error = result.Error; return null; }
        if (result.Collections.Count == 0) { error = $"{path} holds no collection"; return null; }

        var collection = result.Collections[0];
        collection.FixupParents();
        return collection;
    }

    private static EnvironmentModel LoadEnvironment(List<string> paths, out string error)
    {
        error = null;
        if (paths.Count == 0) return null;

        EnvironmentModel merged = null;
        foreach (var path in paths)
        {
            if (!File.Exists(path)) { error = $"no such file: {path}"; return null; }

            var result = PostmanImporter.ImportFile(path);
            if (!string.IsNullOrEmpty(result.Error)) { error = $"{path}: {result.Error}"; return null; }
            if (result.Environments.Count == 0) { error = $"{path} holds no environment"; return null; }

            // Several -e flags merge left to right, so a base file plus an override file works.
            if (merged == null) { merged = result.Environments[0]; continue; }
            foreach (var variable in result.Environments[0].Variables)
            {
                var existing = merged.Variables.FirstOrDefault(v => v.Key == variable.Key);
                if (existing != null) existing.Value = variable.Value;
                else merged.Variables.Add(variable);
            }
        }
        return merged;
    }

    private static string Version() => BuildInfo.Display;

    private static void PrintUsage()
    {
        Console.WriteLine($"""
            {Version()} - headless collection runner

            USAGE
              getman run <collection.json> [options]

            OPTIONS
              -e, --environment <file>   Postman environment export; repeat to merge, left to right
              -g, --globals <file>       Postman globals export
              -d, --data <file>          CSV or JSON data file, one iteration per row
              -n, --iterations <n>       iteration count (default: the data row count, else 1)
                  --delay <ms>           wait between requests
                  --folder <name>        run only this folder of the collection
                  --var name=value       set a variable; wins over the environment file, repeatable
                  --timeout <ms>         per-request timeout (0 = none, the default)
                  --script-timeout <ms>  per-script timeout (default 5000)
                  --insecure             do not verify TLS certificates
                  --bail                 stop at the first failing request
              -r, --reporter <name>      cli (default), json or junit
              -o, --output <file>        write the report to a file instead of stdout
                  --lang <code>          en, ru or uz
                  --no-color             plain output
              -h, --help                 this text
                  --version              version only

            EXIT CODES
              0  every request answered and every assertion passed
              1  an assertion failed, or a request never got a response
              2  the arguments or the files were wrong

            EXAMPLES
              getman run api.postman_collection.json -e staging.postman_environment.json
              getman run api.json -d users.csv -n 50 --delay 200 --bail
              getman run api.json -r junit -o results/getman.xml
            """);
    }
}
