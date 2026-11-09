using System.Globalization;
using System.Resources;
using FluentBlock.Services;
using Xunit;

namespace FluentBlock.Tests;

/// <summary>
/// EFIGS: every resource key must resolve in every UI language, so windows
/// like the changelog never show a raw key fallback. Uses the same
/// ResourceManager path as the UI (no global culture mutation).
/// </summary>
public sealed class EfigsTests
{
    private static readonly string[] Langs = ["en", "fr", "it", "de", "es"];

    private static readonly ResourceManager Rm =
        new("FluentBlock.Resources.Strings", typeof(L10n).Assembly);

    private static SortedSet<string> KeysFor(string lang)
    {
        using var set = Rm.GetResourceSet(new CultureInfo(lang), createIfNotExists: true, tryParents: false);
        Assert.NotNull(set);
        var keys = new SortedSet<string>(StringComparer.Ordinal);
        foreach (System.Collections.DictionaryEntry e in set)
            keys.Add((string)e.Key);
        return keys;
    }

    [Fact]
    public void AllLanguagesExposeSameKeys()
    {
        var en = KeysFor("en");
        Assert.NotEmpty(en);
        foreach (var lang in Langs[1..])
            Assert.Equal(en, KeysFor(lang));
    }

    [Theory]
    [InlineData("en")]
    [InlineData("fr")]
    [InlineData("it")]
    [InlineData("de")]
    [InlineData("es")]
    public void ChangelogFollowsUiLanguage(string lang)
    {
        var culture = new CultureInfo(lang);
        foreach (var key in new[] { "MenuChangelog", "ChangelogTitle", "ChangelogBody" })
        {
            string? s = Rm.GetString(key, culture);
            Assert.False(string.IsNullOrWhiteSpace(s)); // no raw-key fallback
            Assert.NotEqual(key, s);
        }
        Assert.Contains("0.2.", Rm.GetString("ChangelogBody", culture));
    }

    /// <summary>
    /// Regression: SetLanguage must persist beyond the current
    /// ExecutionContext (WPF serves each click under its own context, so a
    /// bare CurrentUICulture assignment produced mixed-language UI).
    /// </summary>
    [Fact]
    public void SetLanguagePersistsProcessWide()
    {
        var prevDefault = CultureInfo.DefaultThreadCurrentUICulture;
        var prevCurrent = CultureInfo.CurrentUICulture;
        try
        {
            L10n.SetLanguage("fr");
            Assert.Equal("fr", CultureInfo.DefaultThreadCurrentUICulture?.Name);
            Assert.Equal("fr", CultureInfo.CurrentUICulture.Name);
        }
        finally
        {
            CultureInfo.DefaultThreadCurrentUICulture = prevDefault;
            CultureInfo.CurrentUICulture = prevCurrent;
        }
    }
}
