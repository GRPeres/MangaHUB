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
}
