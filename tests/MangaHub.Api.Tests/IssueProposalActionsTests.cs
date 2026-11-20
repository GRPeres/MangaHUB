using MangaHub.Api.Services;

namespace MangaHub.Api.Tests;

public sealed class IssueProposalActionsTests
{
    [Theory]
    [InlineData("refresh_chapters", "mangadex-release-stalled", "", "", "refresh_chapters")]
    [InlineData("clear_hiatus", "mangadex-language-coverage", "", "", "clear_hiatus")]
    [InlineData("mark_complete", "mangadex-release-stalled", "", "", "mark_complete")]
    [InlineData("record_external", "mangadex-language-coverage", "4.5", "pt-br", "record_external")]
    [InlineData("record_external", "mangadex-language-coverage", "4.5555", "en", "leave")]
    [InlineData("repair_mal", "mangadex-identity-mismatch", "42", "", "repair_mal")]
    [InlineData("repair_mangaupdates", "mangadex-release-stalled", "abc123", "", "repair_mangaupdates")]
    [InlineData("repair_mangaupdates", "mangadex-release-stalled", "bad/path", "", "leave")]
    public void ParsesAdditionalActions(string action, string kind, string value, string language, string expected)
    {
        const string sourceUrl = "https://publisher.example/announcement";
        var response = System.Text.Json.JsonSerializer.Serialize(new[] { new { number = 1, action, value, language, sourceUrl, reason = "Evidence" } });
        var draft = IssueProposalActions.Create(Guid.NewGuid(), "key", Guid.NewGuid(), "snapshot", 1,
            response + "\n\nResearch sources:\n" + sourceUrl, null, null, kind);
        Assert.Equal(expected, draft.Action);
    }
    [Theory]
    [InlineData("mangadex-release-stalled", "https://publisher.example/hiatus", "mark_hiatus")]
    [InlineData("mangadex-release-stalled", "https://other.example/page", "leave")]
    [InlineData("mangadex-language-coverage", "https://publisher.example/hiatus", "leave")]
    public void HiatusRequiresAnExactResearchCitationAndAStallIssue(string kind, string citedUrl, string expected)
    {
        const string response = """[{"number":1,"action":"mark_hiatus","reason":"Publisher announced a hiatus","sourceUrl":"https://publisher.example/hiatus"}]""";
        var draft = IssueProposalActions.Create(Guid.NewGuid(), "key", Guid.NewGuid(), "snapshot", 1,
            response + "\n\nResearch sources:\n" + citedUrl, null, null, kind);
        Assert.Equal(expected, draft.Action);
    }
    [Fact]
    public void EvidenceOmitsPrivateMetadataAndOnlyIncludesAggregateLanguages()
    {
        var facts = IssueProposalEvidence.ReadIssueFacts("""
            {"maximumChapterGap":4,"fallbackReaderUrl":"http://private.local","userId":"secret",
             "affectedLanguageSets":[{"preferredLanguages":["en","pt-br"],"currentChapter":17,"userId":"secret"}]}
            """);
        Assert.Equal(2, facts.Count);
        Assert.Equal("4", facts["maximumChapterGap"]);
        Assert.Equal("en, pt-br", facts["affectedLanguages"]);
    }
    [Theory]
    [InlineData("snooze", 30, "snooze")]
    [InlineData("snooze", 999, "leave")]
    [InlineData("delete", 30, "leave")]
    [InlineData("repair_mangadex", 30, "leave")]
    public void ParseOnlyAllowsSupportedValidatedActions(string action, int days, string expected)
    {
        var response = System.Text.Json.JsonSerializer.Serialize(new[] { new { number = 1, action, days, reason = "Evidence summary" } });
        var draft = IssueProposalActions.Create(Guid.NewGuid(), "key", Guid.NewGuid(), "snapshot", 1, response, null, null);
        Assert.Equal(expected, draft.Action);
    }

    [Fact]
    public void RepairUsesTheServerCandidateNotAModelProvidedId()
    {
        var candidate = Guid.NewGuid();
        var response = """[{"number":1,"action":"repair_mangadex","reason":"Verified match","replacementId":"invented"}]""";
        var draft = IssueProposalActions.Create(Guid.NewGuid(), "key", Guid.NewGuid(), "snapshot", 1, response, candidate.ToString(), "42");
        Assert.Equal("repair_mangadex", draft.Action);
        Assert.Equal(candidate.ToString(), draft.ReplacementId);
    }

    [Fact]
    public void FingerprintIgnoresCheckTimeButNotChapterEvidence()
    {
        var original = IssueProposalActions.Fingerprint("""{"latestChapter":10,"checkedAt":"old"}""");
        Assert.Equal(original, IssueProposalActions.Fingerprint("""{"latestChapter":10,"checkedAt":"new"}"""));
        Assert.NotEqual(original, IssueProposalActions.Fingerprint("""{"latestChapter":11,"checkedAt":"new"}"""));
    }
}
