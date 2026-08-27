using System.Reflection;

namespace GetMan.Services;

/// <summary>
/// The version the build was stamped with. Read from the informational version rather than the
/// assembly version so a pre-release tag such as 1.2.0-rc.1 survives - the assembly version can
/// only hold four numbers and would quietly report 1.2.0.
/// </summary>
public static class BuildInfo
{
    public static string Version { get; } = Resolve();

    /// <summary>What the app and the CLI print, and what a bug report should quote.</summary>
    public static string Display => "GetMan " + Version;

    private static string Resolve()
    {
        var assembly = Assembly.GetEntryAssembly() ?? typeof(BuildInfo).Assembly;

        var informational = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informational))
        {
            // The SDK appends +<commit sha> when the repository is available; that belongs in the
            // release notes, not in a window title.
            var build = informational.IndexOf('+');
            return build > 0 ? informational[..build] : informational;
        }

        return assembly.GetName().Version?.ToString(3) ?? "1.0.0";
    }
}
