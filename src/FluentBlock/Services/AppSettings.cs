using System.IO;
using System.Text.Json;

namespace FluentBlock.Services;

/// <summary>Theme/language/column layout in %APPDATA%. Never throws; defaults on any failure.</summary>
public static class AppSettings
{
    public sealed record Data(string Theme, string Lang, int TimeoutMs, Dictionary<string, ColumnState> Columns)
    {
        public Data() : this("system", "", 100, new Dictionary<string, ColumnState>()) { }
    }

    public sealed record ColumnState(int DisplayIndex, double Width);

    private static string FilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FluentBlock", "settings.json");

    /// <summary>One-time path (FluentSblock → FluentBlock rename).</summary>
    private static string LegacyFilePath { get; } = Path.Combine(
        Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
        "FluentSblock", "settings.json");

    public static Data Load()
    {
        try
        {
            if (!File.Exists(FilePath) && File.Exists(LegacyFilePath))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
                File.Copy(LegacyFilePath, FilePath);
            }
            if (File.Exists(FilePath))
            {
                var data = JsonSerializer.Deserialize<Data>(File.ReadAllText(FilePath));
                if (data != null)
                    return data with
                    {
                        Columns = data.Columns ?? new(),
                        TimeoutMs = data.TimeoutMs is >= 20 and <= 5000 ? data.TimeoutMs : 100
                    };
            }
        }
        catch { }
        return new Data();
    }

    public static void Save(Data data)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(FilePath)!);
            File.WriteAllText(FilePath, JsonSerializer.Serialize(data));
        }
        catch { }
    }
}
