using System.ComponentModel;
using System.Reflection;
using System.Text.Json;
using System.Windows.Markup;
using System.Windows.Data;

namespace GetMan.Services;

public record LanguageOption(string Code, string Name, string EnglishName)
{
    public override string ToString() => Name;
}

/// <summary>
/// Runtime string table. Bindings target the indexer, so raising a change for "Item[]"
/// re-evaluates every localized binding in the app and the language switches live.
/// </summary>
public sealed class Loc : INotifyPropertyChanged
{
    public static Loc Instance { get; } = new();

    public static readonly LanguageOption[] Languages =
    {
        new("en", "English", "English"),
        new("ru", "Русский", "Russian"),
        new("uz", "O'zbekcha", "Uzbek")
    };

    private Dictionary<string, string> _current = new(StringComparer.Ordinal);
    private Dictionary<string, string> _fallback = new(StringComparer.Ordinal);

    public string Code { get; private set; } = "en";

    private Loc()
    {
        _fallback = Load("en");
        _current = _fallback;
    }

    /// <summary>Missing keys fall back to English, then to the key itself so nothing renders blank.</summary>
    public string this[string key]
    {
        get
        {
            if (key == null) return string.Empty;
            if (_current.TryGetValue(key, out var value)) return value;
            if (_fallback.TryGetValue(key, out var english)) return english;
            return key;
        }
    }

    public void SetLanguage(string code)
    {
        code = Languages.Any(l => l.Code == code) ? code : "en";
        if (code == Code && _current.Count > 0) return;

        Code = code;
        _current = code == "en" ? _fallback : Load(code);
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs("Item[]"));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Code)));
        LanguageChanged?.Invoke();
    }

    /// <summary>
    /// First run has no stored choice, so follow the Windows display language when it is one
    /// of ours and fall back to English otherwise.
    /// </summary>
    public static string Detect()
    {
        var two = System.Globalization.CultureInfo.CurrentUICulture.TwoLetterISOLanguageName;
        return Languages.Any(l => l.Code == two) ? two : "en";
    }

    /// <summary>The keys one language file carries, for the self-check that compares them.</summary>
    public static IReadOnlyCollection<string> Keys(string code) => Load(code).Keys.ToList();

    /// <summary>For code that formats strings itself rather than binding.</summary>
    public static string T(string key) => Instance[key];

    public static string T(string key, params object[] args)
    {
        try { return string.Format(Instance[key], args); }
        catch (FormatException) { return Instance[key]; }
    }

    public event PropertyChangedEventHandler PropertyChanged;

    /// <summary>Raised after the table swaps, for anything that caches text.</summary>
    public static event Action LanguageChanged;

    private static Dictionary<string, string> Load(string code)
    {
        try
        {
            var name = $"GetMan.Assets.Lang.{code}.json";
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(name);
            if (stream == null) return new Dictionary<string, string>(StringComparer.Ordinal);

            using var doc = JsonDocument.Parse(stream);
            var table = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var property in doc.RootElement.EnumerateObject())
                table[property.Name] = property.Value.GetString() ?? property.Name;
            return table;
        }
        catch
        {
            return new Dictionary<string, string>(StringComparer.Ordinal);
        }
    }
}

/// <summary>XAML sugar: <c>Text="{loc:T App.New}"</c>.</summary>
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
