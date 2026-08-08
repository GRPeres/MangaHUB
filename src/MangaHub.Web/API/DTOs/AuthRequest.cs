namespace MangaHub.Web.API.DTOs;

public sealed record AuthRequest(string Username, string Password, string Email = "");
