using System.Security.Cryptography;
using System.Text;
using MangaHub.Api.Common;
using MangaHub.Api.Repositories;
using MangaHub.Core.Dto;
using MangaHub.Core.Models;
using MangaHub.Core.Services;
using Microsoft.Extensions.Configuration;

namespace MangaHub.Api.Services;

public sealed class AuthService(UserRepository users, IPasswordHasher passwordHasher, ISessionTokenService tokens, IEmailSender emailSender, IConfiguration configuration)
{
    public AuthService(UserRepository users, IPasswordHasher passwordHasher, ISessionTokenService tokens)
        : this(users, passwordHasher, tokens, new DisabledEmailSender(), new ConfigurationBuilder().Build())
    {
    }

    public async Task<UserResponse?> RegisterAsync(AuthRequest request, CancellationToken cancellationToken)
    {
        var username = request.Username.Trim();
        var email = NormalizeEmail(request.Email);
        if (await users.UsernameExistsAsync(username, cancellationToken))
        {
            return null;
        }

        var isFirstUser = !await users.AnyAsync(cancellationToken);
        var user = new MangaUser
        {
            Username = username,
            PasswordHash = passwordHasher.Hash(request.Password),
            PendingEmail = email,
            Role = isFirstUser ? "admin" : "user"
        };

        await users.AddAsync(user, cancellationToken);
        await SendEmailVerificationAsync(user, cancellationToken);
        var token = tokens.CreateToken(user.Id, user.Username);
        return ApiMapping.ToUserResponse(user, token);
    }

    public async Task<UserResponse?> LoginAsync(AuthRequest request, CancellationToken cancellationToken)
    {
        var identifier = request.Username.Trim();
        var user = await users.GetByUsernameAsync(identifier, cancellationToken)
                   ?? await users.GetByEmailAsync(NormalizeEmail(identifier), cancellationToken);
        if (user is null || string.IsNullOrWhiteSpace(user.PasswordHash) || !passwordHasher.Verify(request.Password, user.PasswordHash))
        {
            return null;
        }

        var token = tokens.CreateToken(user.Id, user.Username);
        return ApiMapping.ToUserResponse(user, token);
    }

    public async Task<UserResponse> UpdatePreferredLanguageAsync(MangaUser user, UpdatePreferredLanguageRequest request, CancellationToken cancellationToken)
    {
        user.PreferredLanguage = LanguagePreferences.Normalize(request.PreferredLanguage);
        await users.SaveChangesAsync(cancellationToken);
        return ApiMapping.ToUserResponse(user);
    }

    public async Task<string?> UpdateAccountAsync(MangaUser user, UpdateAccountRequest request, CancellationToken cancellationToken)
    {
        var email = NormalizeEmail(request.Email);
        if (string.IsNullOrWhiteSpace(email) || !IsValidEmail(email)) return "Enter a valid email address.";
        var emailOwner = await users.GetByEmailAsync(email, cancellationToken);
        if (emailOwner is not null && emailOwner.Id != user.Id) return "That email address is already linked to another account.";

        var wantsPasswordChange = !string.IsNullOrWhiteSpace(request.NewPassword);
        if (wantsPasswordChange)
        {
            var passwordError = PasswordRules.Validate(request.NewPassword);
            if (passwordError is not null) return passwordError;
            if (!string.IsNullOrWhiteSpace(user.PasswordHash)
                && !passwordHasher.Verify(request.CurrentPassword, user.PasswordHash))
                return "Your current password is incorrect.";
            user.PasswordHash = passwordHasher.Hash(request.NewPassword);
            user.SessionInvalidBefore = DateTimeOffset.UtcNow;
        }

        var emailChanged = !string.Equals(user.Email, email, StringComparison.OrdinalIgnoreCase)
                           || user.EmailConfirmedAt is null;
        if (emailChanged)
        {
            user.PendingEmail = email;
            user.EmailConfirmedAt = null;
            await SendEmailVerificationAsync(user, cancellationToken);
        }
        await users.SaveChangesAsync(cancellationToken);
        return null;
    }

    public async Task RequestPasswordResetAsync(string email, CancellationToken cancellationToken)
    {
        var normalized = NormalizeEmail(email);
        var user = await users.GetByEmailAsync(normalized, cancellationToken);
        if (user is null || user.EmailConfirmedAt is null || !emailSender.IsConfigured) return;

        await users.DeleteExpiredResetTokensAsync(cancellationToken);
        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        await users.AddResetTokenAsync(new PasswordResetToken
        {
            UserId = user.Id,
            TokenHash = HashToken(rawToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(1)
        }, cancellationToken);

        var origin = configuration["FrontendOrigin"]?.TrimEnd('/') ?? "http://localhost:3000";
        var url = $"{origin}/reset-password?token={Uri.EscapeDataString(rawToken)}";
        await emailSender.SendAsync(user.Email, "Reset your MangaHub password",
            $"<p>Use this link to reset your MangaHub password. It expires in one hour.</p><p><a href=\"{url}\">Reset password</a></p>",
            cancellationToken);
    }

    public async Task<bool> ResetPasswordAsync(ResetPasswordRequest request, CancellationToken cancellationToken)
    {
        var passwordError = PasswordRules.Validate(request.NewPassword);
        if (passwordError is not null) return false;
        var token = await users.GetResetTokenAsync(HashToken(request.Token), cancellationToken);
        if (token is null) return false;
        var user = await users.GetByIdAsync(token.UserId, cancellationToken);
        if (user is null) return false;
        user.PasswordHash = passwordHasher.Hash(request.NewPassword);
        token.UsedAt = DateTimeOffset.UtcNow;
        user.SessionInvalidBefore = DateTimeOffset.UtcNow;
        await users.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<UserResponse> LoginWithGoogleAsync(string subject, string email, string displayName, CancellationToken cancellationToken)
    {
        var normalizedEmail = NormalizeEmail(email);
        var user = await users.GetByGoogleSubjectAsync(subject, cancellationToken);
        var emailOwner = user is null ? await users.GetByEmailAsync(normalizedEmail, cancellationToken) : null;
        if (emailOwner?.EmailConfirmedAt is not null)
        {
            user = emailOwner;
        }
        if (user is null)
        {
            user = new MangaUser
            {
                Username = await CreateGoogleUsernameAsync(displayName, normalizedEmail, cancellationToken),
                Email = normalizedEmail,
                EmailConfirmedAt = DateTimeOffset.UtcNow,
                GoogleSubject = subject,
                Role = !await users.AnyAsync(cancellationToken) ? "admin" : "user"
            };
            await users.AddAsync(user, cancellationToken);
        }
        else if (string.IsNullOrWhiteSpace(user.GoogleSubject))
        {
            user.GoogleSubject = subject;
            await users.SaveChangesAsync(cancellationToken);
        }

        return ApiMapping.ToUserResponse(user, tokens.CreateToken(user.Id, user.Username));
    }

    public async Task<bool> ConfirmEmailAsync(string tokenValue, CancellationToken cancellationToken)
    {
        var token = await users.GetEmailVerificationTokenAsync(HashToken(tokenValue), cancellationToken);
        if (token is null) return false;
        var user = await users.GetByIdAsync(token.UserId, cancellationToken);
        if (user is null || string.IsNullOrWhiteSpace(user.PendingEmail)) return false;
        var existing = await users.GetByEmailAsync(user.PendingEmail, cancellationToken);
        if (existing is not null && existing.Id != user.Id) return false;
        user.Email = user.PendingEmail;
        user.PendingEmail = "";
        user.EmailConfirmedAt = DateTimeOffset.UtcNow;
        token.UsedAt = DateTimeOffset.UtcNow;
        await users.SaveChangesAsync(cancellationToken);
        return true;
    }

    public async Task<string?> LinkGoogleAsync(Guid userId, string subject, CancellationToken cancellationToken)
    {
        var user = await users.GetByIdAsync(userId, cancellationToken);
        if (user is null) return "Your account is no longer available.";
        var existing = await users.GetByGoogleSubjectAsync(subject, cancellationToken);
        if (existing is not null && existing.Id != userId) return "That Google account is already linked to another MangaHub account.";
        user.GoogleSubject = subject;
        await users.SaveChangesAsync(cancellationToken);
        return null;
    }

    private async Task<string> CreateGoogleUsernameAsync(string displayName, string email, CancellationToken cancellationToken)
    {
        var seed = string.IsNullOrWhiteSpace(displayName) ? email.Split('@')[0] : displayName;
        var normalized = new string(seed.ToLowerInvariant().Select(character => char.IsLetterOrDigit(character) ? character : '-').ToArray())
            .Trim('-');
        if (normalized.Length < 3) normalized = "reader";
        normalized = normalized[..Math.Min(normalized.Length, 70)];
        var candidate = normalized;
        var suffix = 1;
        while (await users.UsernameExistsAsync(candidate, cancellationToken))
        {
            candidate = $"{normalized}-{suffix++}";
        }
        return candidate;
    }

    public static bool IsValidEmail(string? email) => !string.IsNullOrWhiteSpace(email) && System.Net.Mail.MailAddress.TryCreate(email, out _);
    public static string NormalizeEmail(string? email) => (email ?? "").Trim().ToLowerInvariant();
    private static string HashToken(string token) => Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(token)));

    private async Task SendEmailVerificationAsync(MangaUser user, CancellationToken cancellationToken)
    {
        if (!emailSender.IsConfigured) return;
        await users.DeleteExpiredEmailVerificationTokensAsync(cancellationToken);
        var rawToken = Convert.ToHexString(RandomNumberGenerator.GetBytes(32)).ToLowerInvariant();
        var origin = configuration["FrontendOrigin"]?.TrimEnd('/') ?? "http://localhost:3000";
        var url = $"{origin}/auth/verify-email?token={Uri.EscapeDataString(rawToken)}";
        await emailSender.SendAsync(user.PendingEmail, "Verify your MangaHub recovery email",
            $"<p>Confirm this address for MangaHub password recovery.</p><p><a href=\"{url}\">Verify recovery email</a></p>",
            cancellationToken);
        await users.AddEmailVerificationTokenAsync(new EmailVerificationToken
        {
            UserId = user.Id,
            TokenHash = HashToken(rawToken),
            ExpiresAt = DateTimeOffset.UtcNow.AddHours(24)
        }, cancellationToken);
    }

    private sealed class DisabledEmailSender : IEmailSender
    {
        public bool IsConfigured => false;
        public Task SendAsync(string recipient, string subject, string htmlBody, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
