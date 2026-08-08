namespace MangaHub.Api.Services;

public static class PasswordRules
{
    public const int MinimumLength = 10;

    public static string? Validate(string? password)
    {
        if (string.IsNullOrWhiteSpace(password) || password.Length < MinimumLength)
            return $"Use at least {MinimumLength} characters.";
        if (!password.Any(char.IsUpper))
            return "Add an uppercase letter.";
        if (!password.Any(char.IsLower))
            return "Add a lowercase letter.";
        if (!password.Any(char.IsDigit))
            return "Add a number.";
        return null;
    }
}
