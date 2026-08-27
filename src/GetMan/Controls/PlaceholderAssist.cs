using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Media;

namespace GetMan.Controls;

/// <summary>
/// Grey hint text shown inside an empty field.
///
/// Drawn as an adorner rather than added to each control template: the app has several text box
/// templates and would otherwise need the same block in every one, and a template is exactly the
/// thing a restyle throws away. This attaches to any TextBox or PasswordBox and survives whatever
/// the control looks like.
/// </summary>
public static class PlaceholderAssist
{
    public static readonly DependencyProperty TextProperty = DependencyProperty.RegisterAttached(
        "Text", typeof(string), typeof(PlaceholderAssist), new PropertyMetadata(null, OnTextChanged));

    public static void SetText(DependencyObject d, string value) => d.SetValue(TextProperty, value);
    public static string GetText(DependencyObject d) => (string)d.GetValue(TextProperty);

    private static void OnTextChanged(DependencyObject d, DependencyPropertyChangedEventArgs e)
    {
        if (d is not Control control) return;

        control.Loaded -= OnLoaded;
        control.Loaded += OnLoaded;

        // An adorner does not follow its owner out of view. Auth swaps whole blocks of fields by
        // visibility, so without this the hints of every hidden block keep drawing over the
        // visible one, at whatever position the layout last gave them.
        control.IsVisibleChanged -= OnVisibilityChanged;
        control.IsVisibleChanged += OnVisibilityChanged;

        switch (control)
        {
            case TextBox box:
                box.TextChanged -= OnContentChanged;
                box.TextChanged += OnContentChanged;
                break;
            case PasswordBox password:
                password.PasswordChanged -= OnContentChanged;
                password.PasswordChanged += OnContentChanged;
                break;
        }

        if (control.IsLoaded) Refresh(control);
    }

    private static void OnLoaded(object sender, RoutedEventArgs e) => Refresh((Control)sender);

    private static void OnVisibilityChanged(object sender, DependencyPropertyChangedEventArgs e) =>
        Refresh((Control)sender);

    private static void OnContentChanged(object sender, RoutedEventArgs e) => Refresh((Control)sender);

    private static bool IsEmpty(Control control) => control switch
    {
        TextBox box => string.IsNullOrEmpty(box.Text),
        PasswordBox password => password.SecurePassword.Length == 0,
        _ => false
    };

    private static void Refresh(Control control)
    {
        var layer = AdornerLayer.GetAdornerLayer(control);
        if (layer == null) return;   // not in the tree yet, or a window with no adorner decorator

        var existing = layer.GetAdorners(control)?.OfType<PlaceholderAdorner>().FirstOrDefault();
        var wanted = control.IsVisible && IsEmpty(control) && !string.IsNullOrEmpty(GetText(control));

        if (wanted && existing == null) layer.Add(new PlaceholderAdorner(control));
        else if (!wanted && existing != null) layer.Remove(existing);
        else existing?.InvalidateVisual();
    }

    private sealed class PlaceholderAdorner : Adorner
    {
        private readonly Control _owner;

        public PlaceholderAdorner(Control owner) : base(owner)
        {
            _owner = owner;
            IsHitTestVisible = false;   // clicks belong to the field underneath
        }

        protected override void OnRender(DrawingContext context)
        {
            var text = GetText(_owner);
            if (string.IsNullOrEmpty(text)) return;

            var brush = _owner.TryFindResource("FgMuted") as Brush ?? Brushes.Gray;

            var formatted = new FormattedText(
                text,
                System.Globalization.CultureInfo.CurrentUICulture,
                FlowDirection.LeftToRight,
                new Typeface(_owner.FontFamily, _owner.FontStyle, _owner.FontWeight, _owner.FontStretch),
                _owner.FontSize,
                brush,
                VisualTreeHelper.GetDpi(_owner).PixelsPerDip)
            {
                MaxTextWidth = Math.Max(1, _owner.ActualWidth - _owner.Padding.Left - _owner.Padding.Right - 4),
                MaxLineCount = 1,
                Trimming = TextTrimming.CharacterEllipsis
            };

            // Vertically centred against the field rather than the padding box, so the hint sits
            // exactly where the caret will.
            var y = (_owner.ActualHeight - formatted.Height) / 2;
            context.DrawText(formatted, new Point(_owner.Padding.Left + 2, y));
        }
    }
}
