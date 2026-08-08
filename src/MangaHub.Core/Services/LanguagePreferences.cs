namespace MangaHub.Core.Services;

public static class LanguagePreferences
{
    public static IReadOnlyList<string> Parse(string? value)
    {
        var languages = (value ?? "")
            .Split([',', ';', '\r', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(NormalizeCode)
            .Where(language => language.Length > 0)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .Take(12)
            .ToList();

        return languages.Count == 0 ? ["en"] : languages;
    }

    public static string Normalize(string? value)
    {
        var normalized = new List<string>();
        var length = 0;
        foreach (var language in Parse(value))
        {
            var addedLength = language.Length + (normalized.Count == 0 ? 0 : 1);
            if (length + addedLength > 128) break;
            normalized.Add(language);
            length += addedLength;
        }

        return string.Join(',', normalized);
    }

    public static string Primary(string? value) => Parse(value)[0];

    public static bool Contains(IReadOnlyList<string> languages, string? language) =>
        languages.Contains(NormalizeCode(language), StringComparer.OrdinalIgnoreCase);

    public static int IndexOf(IReadOnlyList<string> languages, string? language)
    {
        var normalized = NormalizeCode(language);
        for (var index = 0; index < languages.Count; index++)
        {
            if (string.Equals(languages[index], normalized, StringComparison.OrdinalIgnoreCase)) return index;
        }

        return languages.Count;
    }

    private static string NormalizeCode(string? language)
    {
        var normalized = (language ?? "").Trim().ToLowerInvariant();
        return normalized[..Math.Min(normalized.Length, 16)];
    }
}
