using MangaHub.Core.Models;
using MangaHub.Infrastructure.Data;
using Microsoft.EntityFrameworkCore;

namespace MangaHub.Api.Repositories;

public sealed class UserRepository(MangaHubDbContext db)
{
    public Task<bool> AnyAsync(CancellationToken cancellationToken) =>
        db.Users.AnyAsync(cancellationToken);

    public Task<bool> UsernameExistsAsync(string username, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(x => x.Username == username, cancellationToken);

    public Task<MangaUser?> GetByUsernameAsync(string username, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(x => x.Username == username, cancellationToken);

    public Task<MangaUser?> GetByEmailAsync(string email, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(x => x.Email == email, cancellationToken);

    public Task<MangaUser?> GetByGoogleSubjectAsync(string subject, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(x => x.GoogleSubject == subject, cancellationToken);

    public Task<MangaUser?> GetByIdAsync(Guid id, CancellationToken cancellationToken) =>
        db.Users.FirstOrDefaultAsync(x => x.Id == id, cancellationToken);

    public Task<bool> ExistsAsync(Guid id, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(x => x.Id == id, cancellationToken);

    public Task<int> CountAdminsAsync(CancellationToken cancellationToken) =>
        db.Users.CountAsync(x => x.Role == "admin", cancellationToken);

    public Task<List<MangaUser>> ListAsync(CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().OrderBy(x => x.Username).ToListAsync(cancellationToken);

    public async Task AddAsync(MangaUser user, CancellationToken cancellationToken)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);

    public async Task AddResetTokenAsync(PasswordResetToken token, CancellationToken cancellationToken)
    {
        db.PasswordResetTokens.Add(token);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<PasswordResetToken?> GetResetTokenAsync(string tokenHash, CancellationToken cancellationToken) =>
        db.PasswordResetTokens.FirstOrDefaultAsync(x => x.TokenHash == tokenHash && x.UsedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow, cancellationToken);

    public Task DeleteExpiredResetTokensAsync(CancellationToken cancellationToken) =>
        db.PasswordResetTokens.Where(x => x.ExpiresAt <= DateTimeOffset.UtcNow || x.UsedAt != null).ExecuteDeleteAsync(cancellationToken);
}
