using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GetMan.Models;

public partial class RequestBody : ObservableObject
{
    [ObservableProperty] private BodyMode _mode = BodyMode.None;

    [ObservableProperty] private string _raw = string.Empty;

    /// <summary>json | text | javascript | html | xml</summary>
    [ObservableProperty] private string _rawLanguage = "json";

    [ObservableProperty] private string _binaryPath = string.Empty;

    [ObservableProperty] private string _graphQlQuery = string.Empty;
    [ObservableProperty] private string _graphQlVariables = string.Empty;

    public ObservableCollection<KeyValueItem> FormData { get; set; } = new();
    public ObservableCollection<KeyValueItem> UrlEncoded { get; set; } = new();

    public RequestBody Clone()
    {
        var b = new RequestBody
        {
            Mode = Mode,
            Raw = Raw,
            RawLanguage = RawLanguage,
            BinaryPath = BinaryPath,
            GraphQlQuery = GraphQlQuery,
            GraphQlVariables = GraphQlVariables
        };
        foreach (var f in FormData) b.FormData.Add(f.Clone());
        foreach (var f in UrlEncoded) b.UrlEncoded.Add(f.Clone());
        return b;
    }
}
