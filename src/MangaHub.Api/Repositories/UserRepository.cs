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

    public Task<bool> HasUnclaimedTemporaryAccountAsync(Guid issuingAdminUserId, DateTimeOffset now, CancellationToken cancellationToken) =>
        db.Users.AnyAsync(x => x.TemporaryAccountCreatedByUserId == issuingAdminUserId
            && x.TemporaryAccountExpiresAt != null
            && x.TemporaryAccountLoginCount == 0
            && x.TemporaryAccountExpiresAt > now, cancellationToken);

    public Task<List<MangaUser>> ListAsync(CancellationToken cancellationToken) =>
        db.Users.AsNoTracking().OrderBy(x => x.Username).ToListAsync(cancellationToken);

    public async Task AddAsync(MangaUser user, CancellationToken cancellationToken)
    {
        db.Users.Add(user);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task SaveChangesAsync(CancellationToken cancellationToken) =>
        db.SaveChangesAsync(cancellationToken);

    public async Task<bool> DeleteTemporaryTestAccountAsync(Guid userId, CancellationToken cancellationToken)
    {
        var user = await db.Users.FirstOrDefaultAsync(item => item.Id == userId, cancellationToken);
        if (user is null
            || !string.Equals(user.Role, "test", StringComparison.OrdinalIgnoreCase)
            || user.TemporaryAccountCreatedByUserId is null)
        {
            return false;
        }

        db.PasswordResetTokens.RemoveRange(await db.PasswordResetTokens.Where(item => item.UserId == userId).ToListAsync(cancellationToken));
        db.EmailVerificationTokens.RemoveRange(await db.EmailVerificationTokens.Where(item => item.UserId == userId).ToListAsync(cancellationToken));
        db.UserMangaEntries.RemoveRange(await db.UserMangaEntries.Where(item => item.UserId == userId).ToListAsync(cancellationToken));
        db.Notifications.RemoveRange(await db.Notifications.Where(item => item.UserId == userId).ToListAsync(cancellationToken));
        db.WebPushSubscriptions.RemoveRange(await db.WebPushSubscriptions.Where(item => item.UserId == userId).ToListAsync(cancellationToken));
        db.ReadingProgress.RemoveRange(await db.ReadingProgress.Where(item => item.UserId == userId).ToListAsync(cancellationToken));
        db.Follows.RemoveRange(await db.Follows.Where(item => item.UserId == userId).ToListAsync(cancellationToken));
        db.UsageEvents.RemoveRange(await db.UsageEvents.Where(item => item.UserId == userId).ToListAsync(cancellationToken));
        db.UsageDailySummaries.RemoveRange(await db.UsageDailySummaries.Where(item => item.UserId == userId).ToListAsync(cancellationToken));
        db.AdminIssueReports.RemoveRange(await db.AdminIssueReports.Where(item => item.ReporterUserId == userId).ToListAsync(cancellationToken));
        db.MaintenanceJobs.RemoveRange(await db.MaintenanceJobs.Where(item => item.RequestedByUserId == userId).ToListAsync(cancellationToken));

        var resolvedIssues = await db.AdminIssues.Where(item => item.ResolvedByUserId == userId).ToListAsync(cancellationToken);
        foreach (var issue in resolvedIssues)
        {
            issue.ResolvedByUserId = null;
        }

        var createdEntries = await db.MangaEntries.Where(item => item.CreatedByUserId == userId).ToListAsync(cancellationToken);
        foreach (var entry in createdEntries)
        {
            entry.CreatedByUserId = null;
        }

        var issuedAccounts = await db.Users.Where(item => item.TemporaryAccountCreatedByUserId == userId).ToListAsync(cancellationToken);
        foreach (var account in issuedAccounts)
        {
            account.TemporaryAccountCreatedByUserId = null;
        }

        db.Users.Remove(user);
        await db.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task AddResetTokenAsync(PasswordResetToken token, CancellationToken cancellationToken)
    {
        db.PasswordResetTokens.Add(token);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<PasswordResetToken?> GetResetTokenAsync(string tokenHash, CancellationToken cancellationToken) =>
        db.PasswordResetTokens.FirstOrDefaultAsync(x => x.TokenHash == tokenHash && x.UsedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow, cancellationToken);

    public Task DeleteExpiredResetTokensAsync(CancellationToken cancellationToken) =>
        db.PasswordResetTokens.Where(x => x.ExpiresAt <= DateTimeOffset.UtcNow || x.UsedAt != null).ExecuteDeleteAsync(cancellationToken);

    public async Task AddEmailVerificationTokenAsync(EmailVerificationToken token, CancellationToken cancellationToken)
    {
        db.EmailVerificationTokens.Add(token);
        await db.SaveChangesAsync(cancellationToken);
    }

    public Task<EmailVerificationToken?> GetEmailVerificationTokenAsync(string tokenHash, CancellationToken cancellationToken) =>
        db.EmailVerificationTokens.FirstOrDefaultAsync(x => x.TokenHash == tokenHash && x.UsedAt == null && x.ExpiresAt > DateTimeOffset.UtcNow, cancellationToken);

    public Task DeleteExpiredEmailVerificationTokensAsync(CancellationToken cancellationToken) =>
        db.EmailVerificationTokens.Where(x => x.ExpiresAt <= DateTimeOffset.UtcNow || x.UsedAt != null).ExecuteDeleteAsync(cancellationToken);
}
