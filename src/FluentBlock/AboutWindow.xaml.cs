using System.Reflection;
using System.Windows;
using FluentBlock.Services;

namespace FluentBlock;

public partial class AboutWindow : Window
{
    public AboutWindow()
    {
        InitializeComponent();
        Title = L10n.Get("MenuAbout");
        AboutTitle.Text = L10n.Get("AppTitle");
        AboutVersion.Text = Assembly.GetExecutingAssembly().GetName().Version?.ToString(3) ?? "?";
        AboutBody.Text = L10n.Get("AboutText");
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => Close();
}
