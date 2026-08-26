using System.Collections.Concurrent;
using System.Windows;
using System.Windows.Media;

namespace GetMan.Controls;

/// <summary>
/// Brushes handed out to value converters. Bindings hold onto the instance they were given,
/// so a converter that looked a brush up once would freeze the old theme's colour in place.
/// These brushes are mutable and cached by token name; <see cref="Refresh"/> re-reads every
/// cached token after a theme swap and repaints in place.
/// </summary>
public static class ThemePalette
{
    private static readonly ConcurrentDictionary<string, SolidColorBrush> Cache = new();

    public static SolidColorBrush Get(string token)
    {
        return Cache.GetOrAdd(token, key =>
        {
            var brush = new SolidColorBrush(Resolve(key));
            return brush;
        });
    }

    /// <summary>Re-reads every cached token from the active resource dictionary.</summary>
    public static void Refresh()
    {
        foreach (var (token, brush) in Cache)
        {
            var color = Resolve(token);
            if (brush.Color != color) brush.Color = color;
        }
    }

    private static Color Resolve(string token)
    {
        if (Application.Current?.TryFindResource(token) is SolidColorBrush found)
            return found.Color;
        return Colors.Gray;
    }

    public static SolidColorBrush ForMethod(string method) => Get((method ?? "GET").ToUpperInvariant() switch
    {
        "GET" => "MethodGET",
        "POST" => "MethodPOST",
        "PUT" => "MethodPUT",
        "PATCH" => "MethodPATCH",
        "DELETE" => "MethodDELETE",
        "HEAD" => "MethodHEAD",
        "OPTIONS" => "MethodOPTIONS",
        _ => "FgDim"
    });

    public static SolidColorBrush ForStatus(int code) => Get(code switch
    {
        >= 200 and < 300 => "Ok",
        >= 300 and < 400 => "Info",
        >= 400 and < 500 => "Warn",
        >= 500 => "Danger",
        _ => "FgMuted"
    });
}
