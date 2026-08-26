using System.Windows;
using System.Windows.Input;
using System.Windows.Media;
using GetMan.Services;
using MaterialDesignThemes.Wpf;

namespace GetMan.Views;

public enum DialogKind
{
    Info,
    Success,
    Warning,
    Error,
    Question
}

public partial class MessageDialog : Window
{
    private MessageDialog()
    {
        InitializeComponent();
        PreviewKeyDown += (_, e) =>
        {
            if (e.Key != Key.Escape) return;
            DialogResult = SecondaryButton.Visibility == Visibility.Visible ? false : true;
            Close();
        };
    }

    private void OnDrag(object sender, MouseButtonEventArgs e)
    {
        if (e.ButtonState == MouseButtonState.Pressed) DragMove();
    }

    private void OnPrimary(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }

    private void OnSecondary(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    /// <summary>
    /// Themed replacement for MessageBox. Returns true when the primary button was chosen.
    /// <paramref name="secondary"/> being null makes it a one-button acknowledgement.
    /// </summary>
    public static bool Show(string title, string message, DialogKind kind = DialogKind.Info,
        string primary = "OK", string secondary = null, string detail = null, Window owner = null)
    {
        var dialog = Build(title, message, kind, primary, secondary, detail);
        dialog.Owner = owner ?? ActiveOwner();
        if (dialog.Owner == null) dialog.WindowStartupLocation = WindowStartupLocation.CenterScreen;
        return dialog.ShowDialog() == true;
    }

    /// <summary>Builds the dialog without showing it, so previews can render the same thing.</summary>
    internal static MessageDialog Build(string title, string message, DialogKind kind,
        string primary = "OK", string secondary = null, string detail = null)
    {
        var dialog = new MessageDialog
        {
            TitleText = { Text = title ?? "GetMan" },
            MessageText = { Text = message ?? string.Empty },
            PrimaryButton = { Content = primary }
        };

        if (string.IsNullOrEmpty(message)) dialog.MessageText.Visibility = Visibility.Collapsed;

        if (!string.IsNullOrWhiteSpace(detail))
        {
            dialog.DetailWell.Visibility = Visibility.Visible;
            dialog.DetailText.Text = detail;
        }

        if (secondary != null)
        {
            dialog.SecondaryButton.Visibility = Visibility.Visible;
            dialog.SecondaryButton.Content = secondary;
            dialog.SecondaryButton.IsCancel = true;
        }

        var (icon, token) = Style(kind);
        dialog.Icon.Kind = icon;
        dialog.Icon.Foreground = Brush(token);
        dialog.IconWell.Background = Brush(token + "Wash") ?? Brush("Bg3");
        return dialog;
    }

    public static void Info(string message, string title = "GetMan", Window owner = null) =>
        Show(title, message, DialogKind.Info, owner: owner);

    public static void Warn(string message, string title = "GetMan", string detail = null, Window owner = null) =>
        Show(title, message, DialogKind.Warning, detail: detail, owner: owner);

    // Button and fallback-title defaults have to be resolved at call time, not as compile-time
    // constants, so a language switch reaches them.
    public static void Error(string message, string title = null, Window owner = null) =>
        Show(title ?? Loc.T("s.dlg_something_went_wrong"), message, DialogKind.Error, owner: owner);

    public static bool Confirm(string message, string title = null,
        string primary = null, string secondary = null, Window owner = null) =>
        Show(title ?? Loc.T("s.dlg_are_you_sure"), message, DialogKind.Question,
            primary ?? Loc.T("s.delete"), secondary ?? Loc.T("s.cancel"), owner: owner);

    private static (PackIconKind Icon, string Token) Style(DialogKind kind) => kind switch
    {
        DialogKind.Success => (PackIconKind.CheckCircleOutline, "Ok"),
        DialogKind.Warning => (PackIconKind.AlertOutline, "Warn"),
        DialogKind.Error => (PackIconKind.AlertCircleOutline, "Danger"),
        DialogKind.Question => (PackIconKind.HelpCircleOutline, "Warn"),
        _ => (PackIconKind.InformationOutline, "Accent")
    };

    private static Brush Brush(string token) => Application.Current?.TryFindResource(token) as Brush;

    /// <summary>The window the user is actually looking at, so the dialog centres on it.</summary>
    private static Window ActiveOwner()
    {
        if (Application.Current == null) return null;
        foreach (Window w in Application.Current.Windows)
            if (w.IsActive && w.IsVisible) return w;
        return Application.Current.MainWindow is { IsVisible: true } main ? main : null;
    }
}
