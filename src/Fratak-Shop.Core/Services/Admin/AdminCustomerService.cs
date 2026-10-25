using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Auth;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Auditing;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Loyalty;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Admin;

/// <summary>Customer actions of the admin panel that go beyond plain editing (blocking, sessions, points).</summary>
public interface IAdminCustomerService
{
    /// <summary>Blocks / unblocks an account. Blocking signs the customer out everywhere immediately.</summary>
    Task<OperationResult> SetBlockedAsync(Guid userId, bool blocked, string? reason, AdminAccess actor);

    Task<OperationResult> RevokeSessionsAsync(Guid userId, AdminAccess actor);

    Task<OperationResult> UpdateProfileAsync(Guid userId, string? firstName, string? lastName, string? email, string? adminNote);

    /// <summary>Adds (positive) or removes (negative) club points with a reason.</summary>
    Task<OperationResult> AdjustPointsAsync(Guid userId, int points, string? reason);
}

public class AdminCustomerService(FratakShopDbContext context, IRefreshTokenService refreshTokens, IMemoryCache cache) : IAdminCustomerService
{
    public async Task<OperationResult> SetBlockedAsync(Guid userId, bool blocked, string? reason, AdminAccess actor)
    {
        if (userId == actor.UserId)
            return OperationResult.Error("حساب خودتان را نمی‌توانید مسدود کنید.");

        var user = await context.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            return OperationResult.NotFound("کاربر یافت نشد.");

        if (user.Role == UserRole.SuperAdmin && !actor.IsOwner)
            return OperationResult.Error("مالک فروشگاه را فقط مالک دیگری می‌تواند مسدود کند.");

        if (blocked && user.Role == UserRole.SuperAdmin && await context.Users.CountAsync(u => u.Role == UserRole.SuperAdmin && !u.IsBlocked) <= 1)
            return OperationResult.Error("آخرین مالک فروشگاه را نمی‌توان مسدود کرد.");

        var text = reason?.Trim();
        if (blocked && string.IsNullOrEmpty(text))
            return OperationResult.Error("دلیل مسدودسازی را بنویسید.");
        if (text?.Length > 500)
            return OperationResult.Error("دلیل حداکثر ۵۰۰ نویسه است.");

        user.IsBlocked = blocked;
        user.BlockedAt = blocked ? DateTimeOffset.UtcNow : null;
        user.BlockReason = blocked ? text : null;
        if (blocked)
            user.SecurityStamp = PasswordHasher.NewSecurityStamp();

        context.SecurityLogs.Add(new SecurityLog
        {
            Type = blocked ? SecurityEventType.UserBlocked : SecurityEventType.UserUnblocked,
            UserId = userId,
            Identifier = MonitoringService.Mask(user.PhoneNumber),
            IpAddress = AuditContext.Current?.IpAddress,
            Details = Truncate(blocked ? $"مسدود توسط {actor.DisplayName}: {text}" : $"رفع مسدودی توسط {actor.DisplayName}", 500),
        });
        await context.SaveChangesAsync();

        if (blocked)
        {
            await refreshTokens.RevokeAllAsync(userId, "blocked");
            cache.Remove(CacheKeys.SecurityStamp(userId));
        }

        AdminAccessService.InvalidateAll();
        return OperationResult.Success(blocked ? "حساب کاربر مسدود شد و از همه دستگاه‌ها خارج شد." : "مسدودی حساب برداشته شد.");
    }

    private static string Truncate(string value, int max) => value.Length > max ? value[..max] : value;

    public async Task<OperationResult> RevokeSessionsAsync(Guid userId, AdminAccess actor)
    {
        var user = await context.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            return OperationResult.NotFound("کاربر یافت نشد.");
        if (user.Role == UserRole.SuperAdmin && !actor.IsOwner && userId != actor.UserId)
            return OperationResult.Error("نشست‌های مالک فروشگاه را فقط خود مالک می‌تواند ببندد.");

        user.SecurityStamp = PasswordHasher.NewSecurityStamp();
        context.SecurityLogs.Add(new SecurityLog
        {
            Type = SecurityEventType.SessionsRevoked,
            UserId = userId,
            Identifier = MonitoringService.Mask(user.PhoneNumber),
            IpAddress = AuditContext.Current?.IpAddress,
            Details = $"خروج از همه دستگاه‌ها توسط {actor.DisplayName}",
        });
        await context.SaveChangesAsync();
        var count = await refreshTokens.RevokeAllAsync(userId, "admin");
        cache.Remove(CacheKeys.SecurityStamp(userId));
        return OperationResult.Success($"کاربر از همه دستگاه‌ها خارج شد ({count} نشست).");
    }

    public async Task<OperationResult> UpdateProfileAsync(Guid userId, string? firstName, string? lastName, string? email, string? adminNote)
    {
        var user = await context.Users.AsTracking().FirstOrDefaultAsync(u => u.Id == userId);
        if (user is null)
            return OperationResult.NotFound("کاربر یافت نشد.");

        string? Clean(string? v) => string.IsNullOrWhiteSpace(v) ? null : v.Trim();
        var first = Clean(firstName);
        var last = Clean(lastName);
        var mail = Clean(email);
        var note = Clean(adminNote);

        if (first?.Length > 100 || last?.Length > 100)
            return OperationResult.Error("نام و نام خانوادگی حداکثر ۱۰۰ نویسه است.");
        if (mail is not null && (mail.Length > 150 || !System.Net.Mail.MailAddress.TryCreate(mail, out _)))
            return OperationResult.Error("ایمیل معتبر نیست.");
        if (note?.Length > 2000)
            return OperationResult.Error("یادداشت حداکثر ۲۰۰۰ نویسه است.");
        if (mail is not null && await context.Users.AnyAsync(u => u.Email == mail && u.Id != userId))
            return OperationResult.Error("این ایمیل برای کاربر دیگری ثبت شده است.");

        user.FirstName = first?.SanitizeText();
        user.LastName = last?.SanitizeText();
        if (!string.Equals(user.Email, mail, StringComparison.OrdinalIgnoreCase))
        {
            user.Email = mail;
            user.IsEmailVerified = false;
        }
        user.AdminNote = note;
        await context.SaveChangesAsync();
        return OperationResult.Success("اطلاعات کاربر ذخیره شد.");
    }

    public async Task<OperationResult> AdjustPointsAsync(Guid userId, int points, string? reason)
    {
        if (points == 0 || Math.Abs(points) > 1_000_000)
            return OperationResult.Error("تعداد امتیاز معتبر نیست.");
        var text = reason?.Trim();
        if (string.IsNullOrEmpty(text) || text.Length > 300)
            return OperationResult.Error("دلیل تغییر امتیاز را (حداکثر ۳۰۰ نویسه) بنویسید.");

        await using var transaction = await context.Database.BeginTransactionAsync();
        var now = DateTimeOffset.UtcNow;
        var lifetime = Math.Max(0, points);
        var affected = await context.Users
            .Where(u => u.Id == userId && u.LoyaltyPoints + points >= 0)
            .ExecuteUpdateAsync(s => s
                .SetProperty(u => u.LoyaltyPoints, u => u.LoyaltyPoints + points)
                .SetProperty(u => u.LoyaltyLifetimePoints, u => u.LoyaltyLifetimePoints + lifetime)
                .SetProperty(u => u.UpdatedAt, now));
        if (affected == 0)
            return OperationResult.Error("کاربر یافت نشد یا امتیاز کافی برای کسر ندارد.");

        var balance = await context.Users.Where(u => u.Id == userId).Select(u => u.LoyaltyPoints).FirstAsync();
        context.LoyaltyTransactions.Add(new LoyaltyTransaction
        {
            UserId = userId,
            Type = LoyaltyTransactionType.AdminAdjust,
            Points = points,
            BalanceAfter = balance,
            Description = text,
            ReferenceKey = $"admin:{Guid.NewGuid():N}",
        });
        await context.SaveChangesAsync();
        await transaction.CommitAsync();
        return OperationResult.Success(points > 0 ? $"{points:#,0} امتیاز اضافه شد." : $"{-points:#,0} امتیاز کسر شد.");
    }
}
