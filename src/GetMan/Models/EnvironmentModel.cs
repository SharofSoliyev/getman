using System.Collections.ObjectModel;
using System.Text.Json.Serialization;
using CommunityToolkit.Mvvm.ComponentModel;

namespace GetMan.Models;

public partial class EnvironmentModel : ObservableObject
{
    [ObservableProperty] private string _id = Guid.NewGuid().ToString("N");
    [ObservableProperty] private string _name = "New Environment";
    [ObservableProperty] private bool _isGlobal;

    public ObservableCollection<KeyValueItem> Variables { get; set; } = new();

    [JsonIgnore] public string DisplayName => IsGlobal ? "Globals" : Name;

    public EnvironmentModel Clone()
    {
        var e = new EnvironmentModel { Id = Guid.NewGuid().ToString("N"), Name = Name + " Copy", IsGlobal = IsGlobal };
        foreach (var v in Variables) e.Variables.Add(v.Clone());
        return e;
    }
}
