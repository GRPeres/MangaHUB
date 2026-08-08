using MangaHub.Core.Services;

namespace MangaHub.Core.Tests;

public sealed class LanguagePreferencesTests
{
    [Fact]
    public void Parse_NormalizesDeduplicatesAndPreservesPriority()
    {
        var languages = LanguagePreferences.Parse(" EN, pt-BR; en\nfr ");

        Assert.Equal(["en", "pt-br", "fr"], languages);
        Assert.Equal("en,pt-br,fr", LanguagePreferences.Normalize(" EN, pt-BR; en\nfr "));
    }

    [Fact]
    public void Parse_UsesEnglishWhenNoLanguageWasProvided()
    {
        Assert.Equal(["en"], LanguagePreferences.Parse("  "));
        Assert.True(LanguagePreferences.Contains(LanguagePreferences.Parse("en,pt-br"), "pt-br"));
        Assert.Equal(0, LanguagePreferences.IndexOf(LanguagePreferences.Parse("en,pt-br"), "en"));
        Assert.Equal(1, LanguagePreferences.IndexOf(LanguagePreferences.Parse("en,pt-br"), "pt-br"));
    }
}
