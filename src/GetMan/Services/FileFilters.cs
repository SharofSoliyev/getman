namespace GetMan.Services;

/// <summary>
/// The Open/Save dialog filter strings, in one place. They are built on demand rather than held in
/// a field so a language switch reaches them, and keeping the patterns here means adding a format -
/// YAML, most recently - is one edit rather than nine.
/// </summary>
public static class FileFilters
{
    public static string All => $"{Loc.T("s.filter_all_files")} (*.*)|*.*";

    /// <summary>Postman exports and OpenAPI/Swagger descriptions, which may be JSON or YAML.</summary>
    public static string Import =>
        $"{Loc.T("s.filter_api_files")} (*.json;*.yaml;*.yml)|*.json;*.yaml;*.yml|{All}";

    public static string Json => $"{Loc.T("s.filter_json_files")} (*.json)|*.json|{All}";

    public static string PostmanCollection => $"{Loc.T("s.filter_postman_collection")} (*.json)|*.json";

    public static string PostmanEnvironment => $"{Loc.T("s.filter_postman_environment")} (*.json)|*.json";

    public static string Data => $"{Loc.T("s.filter_data_files")} (*.csv;*.json)|*.csv;*.json|{All}";

    public static string Certificates =>
        $"{Loc.T("s.filter_certificates")} (*.pfx;*.p12)|*.pfx;*.p12|{All}";
}
