using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Media;

namespace GetMan.Controls;

/// <summary>
/// Tints the native caption of the windows that still have one - the dialogs - so they follow the
/// theme instead of sitting in whatever grey Windows picked. The main window does not need this:
/// it draws its own title bar inside the app bar.
/// </summary>
public static class WindowAssist
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaBorderColor = 34;
    private const int DwmwaTextColor = 36;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    /// <summary>Every window that opted in, so a theme switch can repaint all of them at once.</summary>
    private static readonly List<WeakReference<Window>> Tracked = new();

    static WindowAssist() => ThemeManager.ThemeChanged += RepaintAll;

    public static readonly DependencyProperty DarkTitleBarProperty = DependencyProperty.RegisterAttached(
        "DarkTitleBar", typeof(bool), typeof(WindowAssist), new PropertyMetadata(false, OnDarkTitleBarChanged));

    public static void SetDarkTitleBar(DependencyObject d, bool value) => d.SetValue(DarkTitleBarProperty, value);
    public static bool GetDarkTitleBar(DependencyObject d) => (bool)d.GetValue(DarkTitleBarProperty);

    private static void OnDarkTitleBarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window || e.NewValue is not true) return;

        Track(window);

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) Apply(window);
        else window.SourceInitialized += (_, _) => Apply(window);
    }

    private static void Track(Window window)
    {
        Tracked.RemoveAll(r => !r.TryGetTarget(out var w) || ReferenceEquals(w, window));
        Tracked.Add(new WeakReference<Window>(window));
        window.Closed += (_, _) => Tracked.RemoveAll(r => !r.TryGetTarget(out var w) || ReferenceEquals(w, window));
    }

    private static void RepaintAll()
    {
        foreach (var reference in Tracked.ToList())
            if (reference.TryGetTarget(out var window)) Apply(window);
    }

    private static void Apply(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            // Tells Windows which way round the caption buttons should be drawn.
            int dark = ThemeManager.Current == AppTheme.Dark ? 1 : 0;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref dark, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref dark, sizeof(int));

            // Windows 11 only; older builds ignore these and keep the default caption, which the
            // dark-mode flag above has already made bearable.
            SetColour(hwnd, DwmwaCaptionColor, "Bg2");
            SetColour(hwnd, DwmwaBorderColor, "BorderBrushMain");
            SetColour(hwnd, DwmwaTextColor, "Fg");
        }
        catch
        {
            // Older Windows builds simply keep the default chrome.
        }
    }

    /// <summary>Reads a theme brush and hands DWM the 0x00BBGGRR it wants.</summary>
    private static void SetColour(IntPtr hwnd, int attribute, string token)
    {
        if (Application.Current?.TryFindResource(token) is not SolidColorBrush brush) return;

        var c = brush.Color;
        int bgr = c.R | (c.G << 8) | (c.B << 16);
        DwmSetWindowAttribute(hwnd, attribute, ref bgr, sizeof(int));
    }
}
