using System.Windows;
using System.Windows.Media;

namespace GetMan.Controls;

public enum AppTheme
{
    Dark,
    Light
}

/// <summary>Swaps the token dictionary and the Fluent accent at run time.</summary>
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

        ApplyFluentAccent(theme);

        Current = theme;
        ThemePalette.Refresh();
        ThemeChanged?.Invoke();
    }

    public static void Toggle() => Apply(Current == AppTheme.Dark ? AppTheme.Light : AppTheme.Dark);

    /// <summary>
    /// WPF-UI writes its accent brushes straight into Application.Resources when it loads, and a
    /// value set there beats anything a merged dictionary says - which is why the rest of the
    /// Fluent bridge in Tokens.*.xaml takes but the accent alone stayed salmon. These are written
    /// the same way, after the swap, so the last word is ours.
    /// </summary>
    private static void ApplyFluentAccent(AppTheme theme)
    {
        var app = Application.Current;
        if (app == null) return;

        // Green is the action colour, so it is what Fluent fills a checked control with; sky
        // stays selection, links and anything Fluent calls "accent text".
        var action = Color(theme == AppTheme.Dark ? "#FF22C55E" : "#FF16A34A");
        var actionHover = Color(theme == AppTheme.Dark ? "#FF16A34A" : "#FF15803D");
        var accent = Color(theme == AppTheme.Dark ? "#FF38BDF8" : "#FF0284C7");
        var onAction = Color(theme == AppTheme.Dark ? "#FF052E16" : "#FFFFFFFF");

        foreach (var (key, colour) in new (string, Color)[]
                 {
                     ("SystemAccentColor", action),
                     ("SystemAccentColorPrimary", action),
                     ("SystemAccentColorSecondary", actionHover),
                     ("SystemAccentColorTertiary", actionHover),
                 })
            app.Resources[key] = colour;

        foreach (var (key, colour) in new (string, Color)[]
                 {
                     ("SystemAccentColorPrimaryBrush", action),
                     ("SystemAccentColorSecondaryBrush", actionHover),
                     ("SystemAccentColorTertiaryBrush", actionHover),
                     ("AccentFillColorDefaultBrush", action),
                     ("AccentFillColorSecondaryBrush", actionHover),
                     ("AccentFillColorTertiaryBrush", actionHover),
                     ("AccentTextFillColorPrimaryBrush", accent),
                     ("AccentTextFillColorSecondaryBrush", accent),
                     ("TextOnAccentFillColorPrimaryBrush", onAction),
                     ("TextOnAccentFillColorSecondaryBrush", onAction),
                     ("FocusStrokeColorOuterBrush", accent),
                 })
            app.Resources[key] = new SolidColorBrush(colour);
    }

    private static Color Color(string hex) => (Color)ColorConverter.ConvertFromString(hex);
}
