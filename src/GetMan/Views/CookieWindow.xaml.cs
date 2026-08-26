using System.Windows;
using GetMan.Models;
using GetMan.Services;

namespace GetMan.Views;

public partial class CookieWindow : Window
{
    private readonly HttpEngine _engine;

    public CookieWindow(HttpEngine engine)
    {
        InitializeComponent();
        _engine = engine;
        Load();
    }

    private void Load()
    {
        var rows = _engine.AllCookies().Select(c => new ResponseCookie
        {
            Name = c.Name,
            Value = c.Value,
            Domain = c.Domain,
            Path = c.Path,
            Expires = c.Expires == DateTime.MinValue ? "session" : c.Expires.ToString("u"),
            Secure = c.Secure,
            HttpOnly = c.HttpOnly
        }).OrderBy(c => c.Domain).ThenBy(c => c.Name).ToList();
        Grid.ItemsSource = rows;
    }

    private void OnRefresh(object sender, RoutedEventArgs e) => Load();

    private void OnClear(object sender, RoutedEventArgs e)
    {
        _engine.ClearCookies();
        Load();
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
