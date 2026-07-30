namespace MangaHub.Web.API.DTOs;

public sealed record MangaNotificationResponse(Guid Id, Guid MangaEntryId, string Type, decimal ChapterNumber, string Language, string Title, string Body, DateTimeOffset CreatedAt, DateTimeOffset? ReadAt);
