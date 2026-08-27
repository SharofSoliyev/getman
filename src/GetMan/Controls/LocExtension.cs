using System.Windows.Data;
using System.Windows.Markup;

// The namespace stays GetMan.Services so `xmlns:loc="clr-namespace:GetMan.Services"` keeps
// resolving, but the file lives under Controls: it is the only part of localization that needs
// WPF, and Services/ has to compile without it for the CLI and the headless tests.
namespace GetMan.Services;

/// <summary>XAML sugar: <c>Text="{loc:T s.send}"</c>.</summary>
public sealed class TExtension : MarkupExtension
{
    public TExtension() { }
    public TExtension(string key) => Key = key;

    public string Key { get; set; }

    public override object ProvideValue(IServiceProvider serviceProvider)
    {
        var binding = new Binding($"[{Key}]")
        {
            Source = Loc.Instance,
            Mode = BindingMode.OneWay
        };
        return binding.ProvideValue(serviceProvider);
    }
}
