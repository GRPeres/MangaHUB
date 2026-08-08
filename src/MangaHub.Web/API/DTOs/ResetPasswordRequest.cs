namespace MangaHub.Web.API.DTOs;

public sealed record ResetPasswordRequest(string Token, string NewPassword);
