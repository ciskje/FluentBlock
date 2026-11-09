namespace FluentBlock.Services;

/// <summary>Append-only diagnostic log in %TEMP%. Best effort, never throws.</summary>
public static class Log
{
    public static string FilePath { get; } =
        System.IO.Path.Combine(System.IO.Path.GetTempPath(), "FluentBlock.log");

    public static void Info(string message)
    {
        try
        {
            System.IO.File.AppendAllText(FilePath,
                $"{DateTime.Now:yyyy-MM-dd HH:mm:ss} {message}{Environment.NewLine}");
        }
        catch { }
    }
}
