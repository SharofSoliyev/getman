using System.Windows.Controls;
using System.Windows.Input;
using GetMan.ViewModels;

namespace GetMan.Views;

public partial class RequestView : UserControl
{
    public string[] HttpVersions { get; } = { "auto", "1.0", "1.1", "2.0", "3.0" };

    public RequestView() => InitializeComponent();

    private void OnUrlKeyDown(object sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        if (DataContext is RequestTabViewModel vm && vm.SendCommand.CanExecute(null))
        {
            UrlBox.GetBindingExpression(TextBox.TextProperty)?.UpdateSource();
            vm.SendCommand.Execute(null);
            e.Handled = true;
        }
    }
}
