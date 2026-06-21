namespace MangaHub.Web.API.DTOs;

public sealed record UserAdminResponse(Guid Id, string Username, string Role, DateTimeOffset CreatedAt);
