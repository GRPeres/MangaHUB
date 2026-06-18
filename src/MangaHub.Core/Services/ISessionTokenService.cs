namespace MangaHub.Core.Services;

public interface ISessionTokenService
{
    string CreateToken(Guid userId, string username);
    Guid? ReadUserId(string token);
}

