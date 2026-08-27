using GetMan.Models;

namespace GetMan.Cli;

/// <summary>One request as it actually ran, with the assertions its test script produced.</summary>
public sealed class RunItem
{
    public int Iteration { get; set; }
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
    public string Method { get; set; } = "GET";
    public string Url { get; set; } = string.Empty;
    public int StatusCode { get; set; }
    public string StatusText { get; set; } = string.Empty;
    public double ElapsedMs { get; set; }
    public long SizeBytes { get; set; }
    public string Error { get; set; } = string.Empty;
    public List<TestResult> Tests { get; } = new();

    public bool Failed => !string.IsNullOrEmpty(Error) || Tests.Any(t => t.Status == TestStatus.Fail);
}

/// <summary>The whole run. This is what the json reporter serialises verbatim.</summary>
public sealed class RunReport
{
    public string Collection { get; set; } = string.Empty;
    public string StartedAt { get; set; } = string.Empty;
    public int Iterations { get; set; }
    public double TotalMs { get; set; }
    public List<RunItem> Items { get; } = new();

    public int Requests => Items.Count;
    public int RequestsFailed => Items.Count(i => !string.IsNullOrEmpty(i.Error));
    public int Assertions => Items.Sum(i => i.Tests.Count);
    public int Passed => Items.Sum(i => i.Tests.Count(t => t.Status == TestStatus.Pass));
    public int Failed => Items.Sum(i => i.Tests.Count(t => t.Status == TestStatus.Fail));
    public int Skipped => Items.Sum(i => i.Tests.Count(t => t.Status == TestStatus.Skip));

    /// <summary>A run is only green when nothing errored and nothing asserted false.</summary>
    public bool Success => Failed == 0 && RequestsFailed == 0;
}
