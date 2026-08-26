using System.Windows;
using System.Windows.Media;
using MaterialDesignThemes.Wpf;

namespace GetMan.Controls;

public enum AppTheme
{
    Dark,
    Light
}

/// <summary>Swaps the token dictionary and the Material palette at run time.</summary>
public static class ThemeManager
{
    private const string DarkTokens = "Themes/Tokens.Dark.xaml";
    private const string LightTokens = "Themes/Tokens.Light.xaml";

    public static AppTheme Current { get; private set; } = AppTheme.Dark;

    /// <summary>Raised after the resources have been swapped, for anything that caches colours.</summary>
    public static event Action ThemeChanged;

    public static void Apply(AppTheme theme)
    {
        var app = Application.Current;
        if (app == null) return;

        var wanted = theme == AppTheme.Dark ? DarkTokens : LightTokens;
        var unwanted = theme == AppTheme.Dark ? LightTokens : DarkTokens;

        var dictionaries = app.Resources.MergedDictionaries;
        var existing = dictionaries.FirstOrDefault(d => d.Source != null &&
            d.Source.OriginalString.EndsWith(unwanted, StringComparison.OrdinalIgnoreCase));

        var replacement = new ResourceDictionary { Source = new Uri(wanted, UriKind.Relative) };

        if (existing != null)
        {
            var index = dictionaries.IndexOf(existing);
            dictionaries[index] = replacement;
        }
        else if (!dictionaries.Any(d => d.Source != null &&
                     d.Source.OriginalString.EndsWith(wanted, StringComparison.OrdinalIgnoreCase)))
        {
            dictionaries.Insert(0, replacement);
        }

        ApplyMaterialPalette(theme);

        Current = theme;
        ThemePalette.Refresh();
        ThemeChanged?.Invoke();
    }

    public static void Toggle() => Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);

    private static void ApplyMaterialPalette(AppTheme theme)
    {
        try
        {
            var helper = new PaletteHelper();
            var materialTheme = helper.GetTheme();
            materialTheme.SetBaseTheme(theme == AppTheme.Dark ? BaseTheme.Dark : BaseTheme.Light);
            materialTheme.SetPrimaryColor(Color(theme == AppTheme.Dark ? "#FF22C55E" : "#FF16A34A"));
            materialTheme.SetSecondaryColor(Color(theme == AppTheme.Dark ? "#FF38BDF8" : "#FF0284C7"));
            helper.SetTheme(materialTheme);
        }
        catch
        {
            // A palette failure must not take the window down; tokens already switched.
        }
    }

    private static Color Color(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
