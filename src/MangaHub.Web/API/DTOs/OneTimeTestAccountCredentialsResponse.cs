namespace MangaHub.Web.API.DTOs;

public sealed record OneTimeTestAccountCredentialsResponse(string Username, string Password, string Role, DateTimeOffset ExpiresAt);
