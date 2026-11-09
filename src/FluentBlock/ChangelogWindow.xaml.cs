using System.Windows;
using FluentBlock.Services;

namespace FluentBlock;

public partial class ChangelogWindow : Window
{
    public ChangelogWindow()
    {
        InitializeComponent();
        Title = L10n.Get("ChangelogTitle");
        ChangelogTitle.Text = L10n.Get("ChangelogTitle");
        ChangelogBody.Text = L10n.Get("ChangelogBody");
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => Close();
}
