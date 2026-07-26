namespace MangaHub.Web.API.DTOs;

public sealed record UserResponse(Guid Id, string Username, string Role, string PreferredLanguage, string SessionToken);
