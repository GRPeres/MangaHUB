using System.Globalization;
using System.Text;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;

namespace MangaHub.Api.Common;

public static class TextRules
{
    public static string? NormalizeUserRole(string role)
    {
        var normalized = role.Trim().ToLowerInvariant();
        return normalized is "admin" or "user" ? normalized : null;
    }

    public static string NormalizeShelfStatus(string status)
    {
        var normalized = status.Trim().ToLowerInvariant();
        return normalized switch
        {
            "finished" or "complete" or "completed" => "done",
            "ongoing" or "current" or "reading" => "reading",
            "hiatus" or "paused" => "paused",
            "to read" or "plan to read" or "planned" => "planned",
            "dropped" => "dropped",
            "done" => "done",
            _ => "planned"
        };
    }

    public static void ApplyShelfRequest(UserMangaEntry shelf, AddToShelfRequest request, MangaEntry? catalogEntry = null)
    {
        shelf.ReadingStatus = NormalizeShelfStatus(request.ReadingStatus);
        shelf.CurrentChapter = request.CurrentChapter.Trim();
        if (shelf.ReadingStatus == "done")
        {
            shelf.IsRead = true;
        }
        shelf.Score = NormalizeScore(request.Score);
        shelf.Category = FirstNonEmpty(request.Category, catalogEntry?.Category);
        shelf.Summary = FirstNonEmpty(request.Summary, catalogEntry?.Description);
        shelf.Notes = request.Notes.Trim();
    }

    public static int? NormalizeScore(int? score) => score is null or <= 0 ? null : Math.Clamp(score.Value, 1, 5);

    public static int? ParseScore(string score)
    {
        if (string.IsNullOrWhiteSpace(score))
        {
            return null;
        }

        var normalized = score.Trim().Replace(',', '.');
        if (!decimal.TryParse(normalized, NumberStyles.Number, CultureInfo.InvariantCulture, out var parsed) || parsed <= 0)
        {
            return null;
        }

        return Math.Clamp((int)Math.Round(parsed, MidpointRounding.AwayFromZero), 1, 5);
    }

    public static string FirstNonEmpty(params string?[] values) =>
        values.Select(x => x?.Trim() ?? "").FirstOrDefault(x => !string.IsNullOrWhiteSpace(x)) ?? "";

    public static string ExtractMangaDexId(string urlOrId)
    {
        var value = urlOrId.Trim();
        if (Guid.TryParse(value, out var id))
        {
            return id.ToString();
        }

        const string marker = "/title/";
        var index = value.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (index < 0)
        {
            return "";
        }

        var afterTitle = value[(index + marker.Length)..];
        var segment = afterTitle.Split('/', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        return Guid.TryParse(segment, out var parsed) ? parsed.ToString() : "";
    }

    public static string NormalizeHeader(string header)
    {
        var builder = new StringBuilder();
        foreach (var ch in header.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(ch) || ch == '+')
            {
                builder.Append(ch);
            }
        }

        return builder.ToString();
    }

    public static Dictionary<string, string> RowToDictionary(List<string> headers, List<string> row)
    {
        var values = new Dictionary<string, string>();
        for (var i = 0; i < headers.Count; i++)
        {
            values[headers[i]] = i < row.Count ? row[i] : "";
        }

        return values;
    }

    public static string FirstValue(Dictionary<string, string> values, params string[] keys)
    {
        foreach (var key in keys)
        {
            if (values.TryGetValue(NormalizeHeader(key), out var value) && !string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        return "";
    }

    public static string CleanTitle(string title)
    {
        var cleaned = title.Trim();
        if (cleaned.Length > 3 && cleaned.Length % 2 == 0)
        {
            var half = cleaned.Length / 2;
            if (string.Equals(cleaned[..half], cleaned[half..], StringComparison.OrdinalIgnoreCase))
            {
                cleaned = cleaned[..half].Trim();
            }
        }

        return cleaned;
    }

    public static List<List<string>> ParseCsv(string csv)
    {
        var rows = new List<List<string>>();
        var row = new List<string>();
        var field = new StringBuilder();
        var inQuotes = false;

        for (var i = 0; i < csv.Length; i++)
        {
            var ch = csv[i];
            if (ch == '"')
            {
                if (inQuotes && i + 1 < csv.Length && csv[i + 1] == '"')
                {
                    field.Append('"');
                    i++;
                }
                else
                {
                    inQuotes = !inQuotes;
                }
                continue;
            }

            if (ch == ',' && !inQuotes)
            {
                row.Add(field.ToString());
                field.Clear();
                continue;
            }

            if ((ch == '\n' || ch == '\r') && !inQuotes)
            {
                if (ch == '\r' && i + 1 < csv.Length && csv[i + 1] == '\n')
                {
                    i++;
                }

                row.Add(field.ToString());
                field.Clear();
                if (row.Any(x => !string.IsNullOrWhiteSpace(x)))
                {
                    rows.Add(row);
                }
                row = [];
                continue;
            }

            field.Append(ch);
        }

        row.Add(field.ToString());
        if (row.Any(x => !string.IsNullOrWhiteSpace(x)))
        {
            rows.Add(row);
        }

        return rows;
    }

    public static string DescribeImportException(Exception exception)
    {
        var root = exception;
        while (root.InnerException is not null)
        {
            root = root.InnerException;
        }

        var message = string.IsNullOrWhiteSpace(root.Message) ? exception.GetType().Name : root.Message;
        message = message.ReplaceLineEndings(" ").Trim();
        return message.Length <= 180 ? message : $"{message[..180]}...";
    }
}
