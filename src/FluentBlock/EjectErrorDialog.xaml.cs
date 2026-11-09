using System.Windows;
using FluentBlock.Models;
using FluentBlock.Services;

namespace FluentBlock;

public partial class EjectErrorDialog : Window
{
    public EjectErrorDialog(string title, string cause, string remedy,
        IReadOnlyList<LockHolder>? blockers = null,
        IReadOnlyList<HandleEnumerator.SkippedProcess>? hidden = null)
    {
        InitializeComponent();
        Title = title;
        DlgTitle.Text = title;
        DlgCause.Text = cause;
        DlgRemedy.Text = remedy;
        bool hasBlockers = blockers != null && blockers.Count > 0;
        bool hasHidden = hidden != null && hidden.Count > 0;
        if (hasBlockers)
        {
            var lines = blockers!
                .GroupBy(h => (h.ProcessName, h.Pid))
                .Select(g =>
                {
                    var first = g.First();
                    string files = string.Join(", ",
                        g.Select(h => h.FilePath).Distinct().Take(2));
                    if (g.Select(h => h.FilePath).Distinct().Count() > 2)
                        files += ", …";
                    return $"{first.ProcessName} (PID {first.Pid}) — {files}";
                })
                .Take(20)
                .ToList();
            DlgBlockersTitle.Text = string.Format(L10n.Get("EjectBlockersTitle"), blockers!.Count);
            DlgBlockersTitle.Visibility = Visibility.Visible;
            DlgBlockersList.ItemsSource = lines;
            DlgBlockersList.Visibility = Visibility.Visible;
        }
        if (hasHidden)
        {
            // Processes Windows won't let us open (protected, e.g. antivirus):
            // name them so the user knows who may be holding the drive.
            DlgProtectedTitle.Text = string.Format(L10n.Get("EjectProtectedTitle"), hidden!.Count);
            DlgProtectedTitle.Visibility = Visibility.Visible;
            DlgProtectedList.ItemsSource = hidden!
                .Take(10)
                .Select(h => $"{h.ProcessName} (PID {h.Pid})")
                .ToList();
            DlgProtectedList.Visibility = Visibility.Visible;
            if (!hasBlockers)
                DlgRemedy.Text = L10n.Get("EjectProtectedHint");
        }
        if (!hasBlockers && !hasHidden)
        {
            // No holders found: still explain the temporary-block case.
            DlgCause.Text = cause + " " + L10n.Get("EjectNoBlockers");
        }
    }

    private void OnOkClick(object sender, RoutedEventArgs e) => Close();
}
