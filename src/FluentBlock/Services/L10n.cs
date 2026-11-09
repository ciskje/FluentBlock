using System.Globalization;
using System.Resources;

namespace FluentBlock.Services;

public static class L10n
{
    private static readonly ResourceManager Rm =
        new("FluentBlock.Resources.Strings", typeof(L10n).Assembly);

    public static string Get(string key)
    {
        try
        {
            return Rm.GetString(key, CultureInfo.CurrentUICulture) ?? key;
        }
        catch { return key; }
    }

    public static void SetLanguage(string culture)
    {
        try
        {
            var c = new CultureInfo(culture);
            // CurrentUICulture alone lives in the current ExecutionContext:
            // in WPF each click runs under its own context, so windows
            // opened later fell back to the OS language (mixed-language UI).
            // The process-wide default persists across dispatcher messages
            // and worker threads.
            CultureInfo.DefaultThreadCurrentUICulture = c;
            CultureInfo.CurrentUICulture = c;
        }
        catch { }
    }
}
