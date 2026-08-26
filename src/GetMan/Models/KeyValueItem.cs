using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GetMan.Models;

/// <summary>Single editable row used by params, headers, form fields and variables.</summary>
public partial class KeyValueItem : ObservableObject
{
    [ObservableProperty] private bool _enabled = true;
    [ObservableProperty] private string _key = string.Empty;
    [ObservableProperty] private string _value = string.Empty;
    [ObservableProperty] private string _description = string.Empty;
    [ObservableProperty] private ParamKind _kind = ParamKind.Text;

    /// <summary>Local file paths for form-data file rows.</summary>
    [ObservableProperty] private string _filePath = string.Empty;

    /// <summary>Used by variable tables: the value the environment ships with.</summary>
    [ObservableProperty] private string _initialValue = string.Empty;

    [ObservableProperty] private bool _secret;

    [JsonIgnore]
    public bool IsFile => Kind == ParamKind.File;

    public KeyValueItem() { }

    public KeyValueItem(string key, string value, bool enabled = true)
    {
        _key = key;
        _value = value;
        _enabled = enabled;
    }

    public KeyValueItem Clone() => new()
    {
        Enabled = Enabled,
        Key = Key,
        Value = Value,
        Description = Description,
        Kind = Kind,
        FilePath = FilePath,
        InitialValue = InitialValue,
        Secret = Secret
    };

    partial void OnKindChanged(ParamKind value) => OnPropertyChanged(nameof(IsFile));
}
