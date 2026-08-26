using System.Windows;
using System.Windows.Controls;
using GetMan.Services;

namespace GetMan.Views;

public partial class CodeWindow : Window
{
    private readonly PreparedRequest _request;

    public CodeWindow(PreparedRequest request)
    {
        InitializeComponent();
        _request = request;
        TargetBox.ItemsSource = CodeGenerator.Targets;
        TargetBox.SelectedIndex = 0;
    }

    private void OnTargetChanged(object sender, SelectionChangedEventArgs e)
    {
        var target = TargetBox.SelectedItem as string ?? CodeGenerator.Targets[0];
        Editor.SyntaxLanguage = target switch
        {
            "JavaScript fetch" or "JavaScript axios" or "Node.js https" => "javascript",
            "HTTP raw" => "text",
            _ => "text"
        };
        Editor.BoundText = CodeGenerator.Generate(_request, target);
    }

    private void OnCopy(object sender, RoutedEventArgs e)
    {
        try { Clipboard.SetText(Editor.Text ?? string.Empty); } catch { }
    }

    private void OnClose(object sender, RoutedEventArgs e)
    {
        DialogResult = true;
        Close();
    }
}
