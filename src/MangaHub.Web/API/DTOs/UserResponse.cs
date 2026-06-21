namespace MangaHub.Web.API.DTOs;

public sealed record UserResponse(Guid Id, string Username, string Role, string SessionToken);
