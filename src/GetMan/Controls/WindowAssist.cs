using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;

namespace GetMan.Controls;

/// <summary>Opts every window into the Windows 10/11 dark title bar so the chrome matches the theme.</summary>
public static class WindowAssist
{
    private const int DwmwaUseImmersiveDarkMode = 20;
    private const int DwmwaUseImmersiveDarkModeBefore20H1 = 19;
    private const int DwmwaCaptionColor = 35;
    private const int DwmwaBorderColor = 34;

    [DllImport("dwmapi.dll", PreserveSig = true)]
    private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attr, ref int value, int size);

    public static readonly DependencyProperty DarkTitleBarProperty = DependencyProperty.RegisterAttached(
        "DarkTitleBar", typeof(bool), typeof(WindowAssist), new PropertyMetadata(false, OnDarkTitleBarChanged));

    public static void SetDarkTitleBar(DependencyObject d, bool value) => d.SetValue(DarkTitleBarProperty, value);
    public static bool GetDarkTitleBar(DependencyObject d) => (bool)d.GetValue(DarkTitleBarProperty);

    private static void OnDarkTitleBarChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Window window || e.NewValue is not true) return;

        if (new WindowInteropHelper(window).Handle != IntPtr.Zero) Apply(window);
        else window.SourceInitialized += (_, _) => Apply(window);
    }

    private static void Apply(Window window)
    {
        try
        {
            var hwnd = new WindowInteropHelper(window).Handle;
            if (hwnd == IntPtr.Zero) return;

            int on = 1;
            if (DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkMode, ref on, sizeof(int)) != 0)
                DwmSetWindowAttribute(hwnd, DwmwaUseImmersiveDarkModeBefore20H1, ref on, sizeof(int));

            // 0x00BBGGRR - matches the app bar surface so the caption blends in (Windows 11 only).
            int caption = 0x00272020;
            DwmSetWindowAttribute(hwnd, DwmwaCaptionColor, ref caption, sizeof(int));
            int border = 0x00453A3A;
            DwmSetWindowAttribute(hwnd, DwmwaBorderColor, ref border, sizeof(int));
        }
        catch
        {
            // Older Windows builds simply keep the default chrome.
        }
    }
}
