using System.Windows;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using GetMan.Services;

namespace GetMan.Controls;

/// <summary>
/// Turns any TextBox into a place you can pick an environment variable instead of remembering one.
/// Typing <c>{{</c> opens a list of everything in scope, filtered as you keep typing; Ctrl+Space
/// opens it anywhere. Enter or Tab inserts <c>{{name}}</c> and closes.
///
/// The list is never given focus - it is a Popup whose content is not focusable, and the keys that
/// drive it are handled on the TextBox. Handing focus to a ListBox would end the text edit, which
/// is the usual way this kind of control turns into a fight with the caret.
/// </summary>
public static class VariableAssist
{
    public static readonly DependencyProperty EnabledProperty = DependencyProperty.RegisterAttached(
        "Enabled", typeof(bool), typeof(VariableAssist),
        new PropertyMetadata(false, OnEnabledChanged));

    public static void SetEnabled(DependencyObject o, bool value) => o.SetValue(EnabledProperty, value);
    public static bool GetEnabled(DependencyObject o) => (bool)o.GetValue(EnabledProperty);

    private static readonly DependencyProperty PickerProperty = DependencyProperty.RegisterAttached(
        "Picker", typeof(Picker), typeof(VariableAssist), new PropertyMetadata(null));

    private static void OnEnabledChanged(DependencyObject o, DependencyPropertyChangedEventArgs e)
    {
        if (o is not TextBox box) return;

        // Detach first either way: a style can set this more than once on the same control.
        box.TextChanged -= OnTextChanged;
        box.PreviewKeyDown -= OnPreviewKeyDown;
        box.LostKeyboardFocus -= OnLostFocus;

        if (!(bool)e.NewValue)
        {
            Close(box);
            return;
        }

        box.TextChanged += OnTextChanged;
        box.PreviewKeyDown += OnPreviewKeyDown;
        box.LostKeyboardFocus += OnLostFocus;
    }

    private static void OnLostFocus(object sender, KeyboardFocusChangedEventArgs e) => Close((TextBox)sender);

    private static void OnTextChanged(object sender, TextChangedEventArgs e)
    {
        var box = (TextBox)sender;
        // Only while the caret is where the typing is. A binding writing into the box - a request
        // being opened in a tab, say - must not pop a list up in the user's face.
        if (!box.IsKeyboardFocusWithin) return;
        Refresh(box, openWithoutToken: false);
    }

    private static void OnPreviewKeyDown(object sender, KeyEventArgs e)
    {
        var box = (TextBox)sender;

        if (e.Key == Key.Space && (Keyboard.Modifiers & ModifierKeys.Control) == ModifierKeys.Control)
        {
            Refresh(box, openWithoutToken: true);
            e.Handled = true;
            return;
        }

        var picker = (Picker)box.GetValue(PickerProperty);
        if (picker == null || !picker.IsOpen) return;

        switch (e.Key)
        {
            case Key.Down:
                picker.Move(1);
                e.Handled = true;
                break;
            case Key.Up:
                picker.Move(-1);
                e.Handled = true;
                break;
            case Key.PageDown:
                picker.Move(8);
                e.Handled = true;
                break;
            case Key.PageUp:
                picker.Move(-8);
                e.Handled = true;
                break;
            case Key.Enter:
            case Key.Tab:
                if (picker.Selected != null)
                {
                    Accept(box, picker.Selected);
                    e.Handled = true;
                }
                break;
            case Key.Escape:
                Close(box);
                // Handled, so Escape closes the list rather than the dialog it sits in.
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Works out whether the caret sits inside an unclosed <c>{{</c> and, if it does, shows the
    /// entries matching what has been typed since.
    /// </summary>
    private static void Refresh(TextBox box, bool openWithoutToken)
    {
        var caret = box.CaretIndex;
        var text = box.Text ?? string.Empty;
        if (caret > text.Length) caret = text.Length;

        var start = TokenStart(text, caret);
        if (start < 0)
        {
            if (!openWithoutToken)
            {
                Close(box);
                return;
            }

            // Ctrl+Space in plain text writes the braces, so the same insertion path can finish
            // the job and the user is left with a well-formed token either way.
            box.SelectedText = string.Empty;
            box.Text = text.Insert(caret, "{{");
            box.CaretIndex = caret + 2;
            Refresh(box, openWithoutToken: false);
            return;
        }

        var typed = text.Substring(start + 2, caret - start - 2);
        var matches = VariableCatalog.Matching(typed);
        if (matches.Count == 0)
        {
            Close(box);
            return;
        }

        var picker = (Picker)box.GetValue(PickerProperty);
        if (picker == null)
        {
            picker = new Picker(box, s => Accept(box, s));
            box.SetValue(PickerProperty, picker);
        }

        picker.Show(matches);
    }

    /// <summary>
    /// Index of the <c>{{</c> the caret is inside, or -1. A <c>}}</c> between it and the caret
    /// means the token is finished and this is ordinary text again.
    /// </summary>
    private static int TokenStart(string text, int caret)
    {
        for (var i = caret - 1; i >= 1; i--)
        {
            if (text[i] == '}' && text[i - 1] == '}') return -1;
            if (text[i] == '{' && text[i - 1] == '{') return i - 1;
        }
        return -1;
    }

    private static void Accept(TextBox box, VariableSuggestion suggestion)
    {
        var text = box.Text ?? string.Empty;
        var caret = Math.Min(box.CaretIndex, text.Length);
        var start = TokenStart(text, caret);
        if (start < 0) return;

        // Swallow a closing "}}" the user already typed, so accepting never leaves "{{a}}}}".
        var end = caret;
        if (end + 1 < text.Length && text[end] == '}' && text[end + 1] == '}') end += 2;

        box.Text = text.Substring(0, start) + suggestion.Token + text.Substring(end);
        box.CaretIndex = start + suggestion.Token.Length;
        Close(box);
        box.Focus();
    }

    private static void Close(TextBox box)
    {
        var picker = (Picker)box.GetValue(PickerProperty);
        picker?.Hide();
    }

    /// <summary>
    /// The open list for this box, or null when nothing is offered. Exists so --self-check can
    /// drive the picker the way a person does instead of asserting on its internals.
    /// </summary>
    internal static ListBox OpenListFor(TextBox box) =>
        box.GetValue(PickerProperty) is Picker picker && picker.IsOpen ? picker.List : null;

    /// <summary>The popup itself. One per TextBox, built on first use and reused after that.</summary>
    private sealed class Picker
    {
        private readonly Popup _popup;
        private readonly ListBox _list;

        public Picker(TextBox owner, Action<VariableSuggestion> accept)
        {
            _list = new ListBox
            {
                // Not focusable, so the caret stays in the TextBox while the list is driven
                // from its key handler.
                Focusable = false,
                MaxHeight = 260,
                BorderThickness = new Thickness(0),
                Background = System.Windows.Media.Brushes.Transparent,
                Style = Application.Current?.TryFindResource("VariablePickerList") as Style,
                ItemContainerStyle = Application.Current?.TryFindResource("VariablePickerItem") as Style,
                ItemTemplate = Application.Current?.TryFindResource("VariablePickerTemplate") as DataTemplate
            };

            _list.PreviewMouseLeftButtonUp += (_, e) =>
            {
                if (ItemUnder(e.OriginalSource as DependencyObject) is { } picked) accept(picked);
                e.Handled = true;
            };

            var shell = new Border
            {
                Background = Application.Current?.TryFindResource("Bg2") as System.Windows.Media.Brush,
                BorderBrush = Application.Current?.TryFindResource("BorderBrushMain") as System.Windows.Media.Brush,
                BorderThickness = new Thickness(1),
                CornerRadius = new CornerRadius(8),
                Padding = new Thickness(4),
                MinWidth = 320,
                Child = _list
            };

            _popup = new Popup
            {
                PlacementTarget = owner,
                Placement = PlacementMode.Bottom,
                HorizontalOffset = 0,
                VerticalOffset = 2,
                AllowsTransparency = true,
                // StaysOpen keeps it from closing the moment the TextBox is clicked again; it is
                // closed deliberately instead, on Escape, on accept and on losing focus.
                StaysOpen = true,
                Focusable = false,
                Child = shell
            };
        }

        public bool IsOpen => _popup.IsOpen;

        public ListBox List => _list;

        public VariableSuggestion Selected => _list.SelectedItem as VariableSuggestion;

        public void Show(IReadOnlyList<VariableSuggestion> items)
        {
            var previous = Selected?.Name;
            _list.ItemsSource = items;
            _list.SelectedIndex = previous != null
                ? Math.Max(0, IndexOf(items, previous))
                : 0;
            _popup.IsOpen = true;
            _list.ScrollIntoView(_list.SelectedItem);
        }

        public void Hide() => _popup.IsOpen = false;

        public void Move(int delta)
        {
            if (_list.Items.Count == 0) return;
            var index = _list.SelectedIndex + delta;
            _list.SelectedIndex = Math.Clamp(index, 0, _list.Items.Count - 1);
            _list.ScrollIntoView(_list.SelectedItem);
        }

        private static int IndexOf(IReadOnlyList<VariableSuggestion> items, string name)
        {
            for (var i = 0; i < items.Count; i++)
                if (string.Equals(items[i].Name, name, StringComparison.Ordinal))
                    return i;
            return -1;
        }

        private VariableSuggestion ItemUnder(DependencyObject source)
        {
            while (source != null && source != _list)
            {
                if (source is ListBoxItem item) return item.DataContext as VariableSuggestion;
                source = System.Windows.Media.VisualTreeHelper.GetParent(source)
                         ?? LogicalTreeHelper.GetParent(source);
            }
            return null;
        }
    }
}
