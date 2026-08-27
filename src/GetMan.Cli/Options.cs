namespace GetMan.Cli;

/// <summary>Everything the command line can set. Parsing is hand-rolled to keep the tool dependency-free.</summary>
public sealed class Options
{
    public string Collection { get; set; }
    public List<string> Environments { get; } = new();
    public string Globals { get; set; }
    public string DataFile { get; set; }
    public int? Iterations { get; set; }
    public int Delay { get; set; }
    public string Folder { get; set; }
    public int? TimeoutMs { get; set; }
    public int? ScriptTimeoutMs { get; set; }
    public bool Insecure { get; set; }
    public bool Bail { get; set; }
    public bool NoColor { get; set; }
    public string Reporter { get; set; } = "cli";
    public string Output { get; set; }
    public string Language { get; set; }
    public Dictionary<string, string> Variables { get; } = new(StringComparer.Ordinal);

    public static readonly string[] Reporters = { "cli", "json", "junit" };

    /// <summary>
    /// Returns the options, or null plus a message. A tuple rather than an out parameter because
    /// the argument loop uses a local function, and C# will not let one capture an out parameter.
    /// </summary>
    public static (Options Options, string Error) Parse(string[] args)
    {
        string error = null;
        var options = new Options();

        for (int i = 0; i < args.Length; i++)
        {
            var arg = args[i];

            string Next(string name)
            {
                if (i + 1 >= args.Length)
                {
                    error ??= $"{name} needs a value";
                    return null;
                }
                return args[++i];
            }

            int? NextInt(string name)
            {
                var text = Next(name);
                if (text == null) return null;
                if (int.TryParse(text, out var value)) return value;
                error ??= $"{name} expects a number, got '{text}'";
                return null;
            }

            switch (arg)
            {
                case "-e" or "--environment":
                    var env = Next(arg);
                    if (env != null) options.Environments.Add(env);
                    break;
                case "-g" or "--globals": options.Globals = Next(arg); break;
                case "-d" or "--data": options.DataFile = Next(arg); break;
                case "--folder": options.Folder = Next(arg); break;
                case "-r" or "--reporter": options.Reporter = Next(arg)?.ToLowerInvariant(); break;
                case "-o" or "--output": options.Output = Next(arg); break;
                case "--lang" or "--language": options.Language = Next(arg); break;

                case "--insecure": options.Insecure = true; break;
                case "--bail": options.Bail = true; break;
                case "--no-color": options.NoColor = true; break;

                case "-n" or "--iterations": options.Iterations = NextInt(arg); break;
                case "--delay": options.Delay = NextInt(arg) ?? 0; break;
                case "--timeout": options.TimeoutMs = NextInt(arg); break;
                case "--script-timeout": options.ScriptTimeoutMs = NextInt(arg); break;

                case "--var":
                    var pair = Next(arg);
                    if (pair == null) break;
                    var split = pair.IndexOf('=');
                    if (split <= 0) { error ??= $"--var expects name=value, got '{pair}'"; break; }
                    options.Variables[pair[..split]] = pair[(split + 1)..];
                    break;

                default:
                    if (arg.StartsWith('-')) { error ??= $"unknown option '{arg}'"; break; }
                    if (options.Collection != null) { error ??= $"unexpected argument '{arg}'"; break; }
                    options.Collection = arg;
                    break;
            }
        }

        if (error != null) return (null, error);
        if (options.Collection == null) return (null, "no collection given");

        if (!Reporters.Contains(options.Reporter))
            return (null, $"unknown reporter '{options.Reporter}' (expected {string.Join(", ", Reporters)})");

        if (options.Iterations is < 1) return (null, "--iterations must be at least 1");
        if (options.Delay < 0) return (null, "--delay cannot be negative");

        return (options, null);
    }
}
