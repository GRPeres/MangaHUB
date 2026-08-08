namespace MangaHub.Web.API.DTOs;

public sealed record UpdateAccountRequest(string Email, string CurrentPassword = "", string NewPassword = "");
