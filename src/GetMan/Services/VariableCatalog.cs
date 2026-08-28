using GetMan.Models;

namespace GetMan.Services;

public sealed class VariableSuggestion
{
    public string Name { get; init; }
    public string Value { get; init; }
    public VariableScope Scope { get; init; }

    /// <summary>Translated, and read at call time so a language switch reaches an open list.</summary>
    public string ScopeLabel => Scope switch
    {
        VariableScope.Local => Loc.T("s.var_scope_local"),
        VariableScope.Data => Loc.T("s.var_scope_data"),
        VariableScope.Environment => Loc.T("s.var_scope_environment"),
        VariableScope.Collection => Loc.T("s.var_scope_collection"),
        VariableScope.Global => Loc.T("s.var_scope_global"),
        _ => Loc.T("s.var_scope_dynamic")
    };

    /// <summary>
    /// One line of preview. A dynamic variable has no stored value - it is generated per request -
    /// so it shows what it produces instead of an empty column.
    /// </summary>
    public string Preview => Scope == VariableScope.Dynamic
        ? Loc.T("s.var_generated_each_run")
        : string.IsNullOrEmpty(Value) ? Loc.T("s.var_empty") : Value;

    /// <summary>What goes into the text when this entry is chosen.</summary>
    public string Token => "{{" + Name + "}}";
}

/// <summary>
/// The list the {{...}} picker offers. It reads the live resolver rather than keeping its own
/// copy, so what the list shows and what a request actually sends cannot drift apart.
/// </summary>
public static class VariableCatalog
{
    /// <summary>Set once by the app; left null in tests and in the CLI, where nothing picks.</summary>
    public static Func<VariableResolver> Source { get; set; }

    public static IReadOnlyList<VariableSuggestion> All()
    {
        var resolver = Source?.Invoke();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<VariableSuggestion>();

        if (resolver != null)
        {
            // Nearest scope first, and a name already taken by a nearer scope is not offered
            // again: two entries reading {{token}} that resolve differently is a trap, not a list.
            Add(resolver.LocalVars, VariableScope.Local);
            Add(resolver.DataVars, VariableScope.Data);
            Add(resolver.EnvironmentVars, VariableScope.Environment);
            Add(resolver.CollectionVars, VariableScope.Collection);
            Add(resolver.Globals, VariableScope.Global);
        }

        foreach (var name in VariableResolver.DynamicNames)
            if (seen.Add(name))
                result.Add(new VariableSuggestion { Name = name, Scope = VariableScope.Dynamic });

        return result;

        void Add(Dictionary<string, string> source, VariableScope scope)
        {
            foreach (var pair in source.OrderBy(p => p.Key, StringComparer.OrdinalIgnoreCase))
                if (seen.Add(pair.Key))
                    result.Add(new VariableSuggestion { Name = pair.Key, Value = pair.Value, Scope = scope });
        }
    }

    /// <summary>
    /// Entries matching what has been typed so far. A name that starts with the text sorts above
    /// one that merely contains it, so typing "url" offers urlBase before myServiceUrl.
    /// </summary>
    public static IReadOnlyList<VariableSuggestion> Matching(string prefix)
    {
        var all = All();
        if (string.IsNullOrEmpty(prefix)) return all;

        return all
            .Select(s => (Suggestion: s, Rank: Rank(s.Name, prefix)))
            .Where(x => x.Rank >= 0)
            .OrderBy(x => x.Rank)
            .Select(x => x.Suggestion)
            .ToList();
    }

    private static int Rank(string name, string prefix)
    {
        if (name.StartsWith(prefix, StringComparison.OrdinalIgnoreCase)) return 0;
        return name.IndexOf(prefix, StringComparison.OrdinalIgnoreCase) >= 0 ? 1 : -1;
    }
}
