using System.Reflection;
using System.Windows;
using System.Windows.Media;
using System.Xml;
using ICSharpCode.AvalonEdit;
using ICSharpCode.AvalonEdit.Highlighting;
using ICSharpCode.AvalonEdit.Highlighting.Xshd;
using ICSharpCode.AvalonEdit.Search;

namespace GetMan.Controls;

/// <summary>AvalonEdit wired for MVVM: bindable text plus a theme-aware syntax palette.</summary>
public class CodeEditor : TextEditor
{
    private bool _updating;
    private static bool _highlightingLoaded;

    public static readonly DependencyProperty BoundTextProperty = DependencyProperty.Register(
        nameof(BoundText), typeof(string), typeof(CodeEditor),
        new FrameworkPropertyMetadata(string.Empty, FrameworkPropertyMetadataOptions.BindsTwoWayByDefault, OnBoundTextChanged));

    public static readonly DependencyProperty SyntaxLanguageProperty = DependencyProperty.Register(
        nameof(SyntaxLanguage), typeof(string), typeof(CodeEditor),
        new PropertyMetadata("text", OnLanguageChanged));

    public string BoundText
    {
        get => (string)GetValue(BoundTextProperty);
        set => SetValue(BoundTextProperty, value);
    }

    public string SyntaxLanguage
    {
        get => (string)GetValue(SyntaxLanguageProperty);
        set => SetValue(SyntaxLanguageProperty, value);
    }

    public CodeEditor()
    {
        EnsureHighlighting();

        FontSize = 13;
        ShowLineNumbers = true;
        WordWrap = true;
        Padding = new Thickness(8, 8, 8, 8);
        BorderThickness = new Thickness(0);
        Options.EnableHyperlinks = false;
        Options.EnableEmailHyperlinks = false;
        Options.HighlightCurrentLine = true;
        Options.ConvertTabsToSpaces = true;
        Options.IndentationSize = 2;
        Options.AllowScrollBelowDocument = false;

        ApplyTheme();
        SearchPanel.Install(this);

        ThemeManager.ThemeChanged += OnThemeChanged;
        Unloaded += (_, _) => ThemeManager.ThemeChanged -= OnThemeChanged;

        TextChanged += (_, _) =>
        {
            if (_updating) return;
            _updating = true;
            SetCurrentValue(BoundTextProperty, Text);
            _updating = false;
        };
    }

    private void OnThemeChanged() => Dispatcher.BeginInvoke(new Action(() =>
    {
        ApplyTheme();
        SyntaxHighlighting = Resolve(SyntaxLanguage);
    }));

    private void ApplyTheme()
    {
        Background = Res("Bg0", Brushes.Black);
        Foreground = Res("Fg", Brushes.White);
        FontFamily = Application.Current?.TryFindResource("MonoFont") as FontFamily ?? new FontFamily("Consolas");

        var light = ThemeManager.Current == AppTheme.Light;
        TextArea.TextView.CurrentLineBackground = new SolidColorBrush(light
            ? Color.FromArgb(16, 0, 0, 0)
            : Color.FromArgb(20, 255, 255, 255));
        TextArea.TextView.CurrentLineBorder = new Pen(Brushes.Transparent, 0);

        var accent = (Res("Accent", Brushes.DodgerBlue) as SolidColorBrush)?.Color ?? Colors.DodgerBlue;
        TextArea.SelectionBrush = new SolidColorBrush(Color.FromArgb(80, accent.R, accent.G, accent.B));
        TextArea.SelectionBorder = null;
        TextArea.TextView.LinkTextForegroundBrush = Res("Accent", Brushes.DodgerBlue);
        LineNumbersForeground = Res("FgMuted", Brushes.Gray);
    }

    private static Brush Res(string key, Brush fallback) =>
        Application.Current?.TryFindResource(key) as Brush ?? fallback;

    private static void OnBoundTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not CodeEditor editor || editor._updating) return;
        editor._updating = true;
        var value = e.NewValue as string ?? string.Empty;
        if (editor.Text != value)
        {
            var caret = editor.CaretOffset;
            editor.Text = value;
            editor.CaretOffset = Math.Min(caret, value.Length);
        }
        editor._updating = false;
    }

    private static void OnLanguageChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is CodeEditor editor)
            editor.SyntaxHighlighting = Resolve(e.NewValue as string);
    }

    public static IHighlightingDefinition Resolve(string language)
    {
        var suffix = ThemeManager.Current == AppTheme.Light ? "Light" : "Dark";
        return (language ?? "text").ToLowerInvariant() switch
        {
            "json" => HighlightingManager.Instance.GetDefinition("Json" + suffix),
            "xml" or "html" or "htm" or "svg" => HighlightingManager.Instance.GetDefinition("Xml" + suffix),
            "javascript" or "js" => HighlightingManager.Instance.GetDefinition("JavaScript" + suffix),
            _ => null
        };
    }

    private static void EnsureHighlighting()
    {
        if (_highlightingLoaded) return;
        _highlightingLoaded = true;

        Load("GetMan.Assets.Json.xshd", "JsonDark", new[] { ".json" });
        Load("GetMan.Assets.XmlDark.xshd", "XmlDark", new[] { ".xml", ".html", ".htm", ".svg" });
        Load("GetMan.Assets.JavaScriptDark.xshd", "JavaScriptDark", new[] { ".js" });
        Load("GetMan.Assets.JsonLight.xshd", "JsonLight", Array.Empty<string>());
        Load("GetMan.Assets.XmlLight.xshd", "XmlLight", Array.Empty<string>());
        Load("GetMan.Assets.JavaScriptLight.xshd", "JavaScriptLight", Array.Empty<string>());
    }

    private static void Load(string resource, string name, string[] extensions)
    {
        try
        {
            using var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resource);
            if (stream == null) return;
            using var reader = new XmlTextReader(stream);
            var def = HighlightingLoader.Load(reader, HighlightingManager.Instance);
            HighlightingManager.Instance.RegisterHighlighting(name, extensions, def);
        }
        catch
        {
            // Highlighting is cosmetic - never let it break start-up.
        }
    }
}
