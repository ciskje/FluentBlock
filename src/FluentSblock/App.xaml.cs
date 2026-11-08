using System.Globalization;
using System.IO;
using System.Windows;

namespace FluentSblock;

public partial class App : Application
{
    /// <summary>Culture passed via --lang= (wins over saved settings).</summary>
    public static string StartupLang { get; private set; } = "";

    protected override void OnStartup(StartupEventArgs e)
    {
        // Headless elevated worker mode: "--worker <pipeName> <parentPid>"
        if (e.Args.Length >= 3 && e.Args[0].Equals("--worker", StringComparison.OrdinalIgnoreCase)
            && int.TryParse(e.Args[2], out int parentPid))
        {
            FluentSblock.Services.ElevationService.RunWorker(e.Args[1], parentPid);
            Environment.Exit(0);
            return;
        }

        // --lang=fr override for testing EFIGS.
        foreach (var arg in e.Args)
        {
            if (arg.StartsWith("--lang=", StringComparison.OrdinalIgnoreCase))
            {
                var lang = arg["--lang=".Length..].Trim();
                try { CultureInfo.CurrentUICulture = new CultureInfo(lang); StartupLang = lang; }
                catch (CultureNotFoundException) { /* fallback to OS */ }
            }
        }

        // Starts as standard user by design (no UAC at launch): an elevated
        // background worker is spawned automatically on demand when needed.

        try { SetCurrentProcessExplicitAppUserModelId("FluentSblock"); }
        catch { /* taskbar grouping best effort */ }

        FluentSblock.Services.ThemeService.ApplySystemAccent();
        base.OnStartup(e);
    }

    [System.Runtime.InteropServices.DllImport("shell32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern int SetCurrentProcessExplicitAppUserModelId(string appId);
}
