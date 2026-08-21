using System.Globalization;

namespace MangaHub.Core.Services;

public sealed class ChapterNumberComparer : IComparer<string>
{
    public static ChapterNumberComparer Instance { get; } = new();

    public int Compare(string? left, string? right)
    {
        var leftParts = ParseParts(left);
        var rightParts = ParseParts(right);

        if (leftParts is null || rightParts is null)
        {
            if (leftParts is not null) return -1;
            if (rightParts is not null) return 1;
            return string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
        }

        for (var index = 0; index < Math.Min(leftParts.Length, rightParts.Length); index++)
        {
            var comparison = leftParts[index].CompareTo(rightParts[index]);
            if (comparison != 0) return comparison;
        }

        var lengthComparison = leftParts.Length.CompareTo(rightParts.Length);
        return lengthComparison != 0
            ? lengthComparison
            : string.Compare(left, right, StringComparison.OrdinalIgnoreCase);
    }

    private static int[]? ParseParts(string? value)
    {
        var numeric = new string((value ?? "")
            .SkipWhile(character => !char.IsDigit(character))
            .TakeWhile(character => char.IsDigit(character) || character is '.' or ',')
            .ToArray());
        if (string.IsNullOrWhiteSpace(numeric)) return null;

        var parts = numeric
            .Replace(',', '.')
            .Split('.', StringSplitOptions.RemoveEmptyEntries)
            .Select(part => int.TryParse(part, NumberStyles.None, CultureInfo.InvariantCulture, out var number) ? number : -1)
            .ToArray();

        return parts.Length == 0 || parts.Any(part => part < 0) ? null : parts;
    }
}
