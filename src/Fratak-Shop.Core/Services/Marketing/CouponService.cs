using System.Security.Cryptography;
using Fratak_Shop.Core.DTOs.Marketing;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Marketing;

/// <summary>
/// Discount codes: validation of all business rules against a cart, usage recording
/// inside the order transaction (atomic, so limits can't be exceeded by parallel
/// orders) and release of the usage when the order is cancelled.
/// </summary>
public interface ICouponService
{
    /// <summary>Upper-case Latin form of a typed code ("welcome۱۰ " → "WELCOME10"); null when invalid.</summary>
    static string? NormalizeCode(string? code)
    {
        if (string.IsNullOrWhiteSpace(code))
            return null;

        var chars = code.Trim().Select(ch => ch switch
        {
            >= '۰' and <= '۹' => (char)('0' + (ch - '۰')),
            >= '٠' and <= '٩' => (char)('0' + (ch - '٠')),
            _ => char.ToUpperInvariant(ch),
        }).ToArray();

        var normalized = new string(chars);
        return normalized.Length is >= 3 and <= 50 && normalized.All(ch => ch is >= 'A' and <= 'Z' or >= '0' and <= '9' or '-' or '_')
            ? normalized
            : null;
    }

    Task<OperationResult<CouponEvaluation>> EvaluateAsync(CouponEvaluationRequest request);

    /// <summary>Active public coupons plus the customer's personal coupons.</summary>
    Task<List<CouponDto>> GetAvailableCouponsAsync(Guid? userId);

    /// <summary>
    /// Records the use of a coupon by an order. Must run inside the order transaction:
    /// the total limit is enforced with a conditional UPDATE and the customer row is
    /// locked so per-customer limits hold under concurrency.
    /// </summary>
    Task<OperationResult> RecordUsageAsync(Guid couponId, Guid? userId, Guid orderId, decimal discount);

    /// <summary>Gives the use back when an order is cancelled / returned (joins the ambient transaction).</summary>
    Task ReleaseUsageAsync(Guid orderId);

    /// <summary>Creates a single-use personal coupon (customer-club rewards, apologies, ...).</summary>
    Task<CouponDto> CreatePersonalCouponAsync(Guid userId, string title, CouponType type, decimal value,
        decimal? maxDiscount, decimal minOrderAmount, int validDays, string prefix = "CLUB");
}

public class CouponService(FratakShopDbContext context) : ICouponService
{
    private const string InvalidCode = "کد تخفیف وارد شده معتبر نیست.";

    public async Task<OperationResult<CouponEvaluation>> EvaluateAsync(CouponEvaluationRequest request)
    {
        var code = ICouponService.NormalizeCode(request.Code);
        if (code is null)
            return OperationResult<CouponEvaluation>.Error(InvalidCode);

        var coupon = await context.Coupons
            .Include(c => c.Targets)
            .FirstOrDefaultAsync(c => c.Code == code);

        if (coupon is null || !coupon.IsActive)
            return OperationResult<CouponEvaluation>.Error(InvalidCode);

        var now = DateTimeOffset.UtcNow;

        if (coupon.StartsAt.HasValue && coupon.StartsAt > now)
            return OperationResult<CouponEvaluation>.Error("زمان استفاده از این کد تخفیف هنوز شروع نشده است.");

        if (coupon.EndsAt.HasValue && coupon.EndsAt < now)
            return OperationResult<CouponEvaluation>.Error("مهلت استفاده از این کد تخفیف به پایان رسیده است.");

        if (coupon.UsageLimit.HasValue && coupon.UsedCount >= coupon.UsageLimit)
            return OperationResult<CouponEvaluation>.Error("ظرفیت استفاده از این کد تخفیف تکمیل شده است.");

        var userError = await ValidateUserAsync(coupon, request.UserId, request.RequireUser);
        if (userError is not null)
            return OperationResult<CouponEvaluation>.Error(userError);

        var lines = request.Lines.Where(l => l.Quantity > 0).GroupBy(l => l.ProductId)
            .Select(g => new CouponLine(g.Key, g.Sum(l => l.Quantity))).ToList();
        if (lines.Count == 0)
            return OperationResult<CouponEvaluation>.Error("سبد خرید شما خالی است.");

        var productIds = lines.Select(l => l.ProductId).ToList();
        var products = await context.Products
            .Where(p => productIds.Contains(p.Id) && p.IsActive)
            .ToListAsync();

        var categoryScope = await ResolveCategoryScopeAsync(coupon.Targets);
        var includes = coupon.Targets.Where(t => !t.IsExcluded).ToList();
        var excludes = coupon.Targets.Where(t => t.IsExcluded).ToList();

        decimal eligibleAmount = 0;
        var eligibleCount = 0;

        foreach (var line in lines)
        {
            var product = products.FirstOrDefault(p => p.Id == line.ProductId);
            if (product is null)
                continue;

            var finalPrice = product.GetFinalPrice(now);
            if (coupon.ExcludeDiscountedProducts && finalPrice < product.Price)
                continue;

            bool Matches(CouponTarget target) => target.TargetType switch
            {
                CouponTargetType.Product => target.TargetId == product.Id,
                CouponTargetType.Brand => target.TargetId == product.BrandId,
                CouponTargetType.Category => categoryScope.TryGetValue(target.Id, out var ids) && ids.Contains(product.CategoryId),
                _ => false,
            };

            if (includes.Count > 0 && !includes.Any(Matches))
                continue;

            if (excludes.Any(Matches))
                continue;

            eligibleAmount += finalPrice * line.Quantity;
            eligibleCount += line.Quantity;
        }

        if (eligibleAmount <= 0)
            return OperationResult<CouponEvaluation>.Error("این کد تخفیف برای کالاهای سبد خرید شما قابل استفاده نیست.");

        if (coupon.MinOrderAmount > 0 && eligibleAmount < coupon.MinOrderAmount)
            return OperationResult<CouponEvaluation>.Error(
                $"این کد برای خرید حداقل {coupon.MinOrderAmount:N0} تومان {(coupon.Targets.Count > 0 || coupon.ExcludeDiscountedProducts ? "از کالاهای مشمول " : "")}قابل استفاده است.");

        if (coupon.MaxOrderAmount.HasValue && eligibleAmount > coupon.MaxOrderAmount)
            return OperationResult<CouponEvaluation>.Error($"این کد فقط برای خریدهای تا سقف {coupon.MaxOrderAmount:N0} تومان قابل استفاده است.");

        if (coupon.MinItemCount > 0 && eligibleCount < coupon.MinItemCount)
            return OperationResult<CouponEvaluation>.Error($"برای استفاده از این کد حداقل {coupon.MinItemCount} کالای مشمول در سبد لازم است.");

        var evaluation = new CouponEvaluation
        {
            CouponId = coupon.Id,
            Code = coupon.Code,
            Title = coupon.Title,
            Type = coupon.Type,
            EligibleAmount = eligibleAmount,
        };

        switch (coupon.Type)
        {
            case CouponType.Percentage:
                var percent = decimal.Floor(eligibleAmount * coupon.Value / 100m);
                evaluation.ItemsDiscount = coupon.MaxDiscountAmount is > 0 ? Math.Min(percent, coupon.MaxDiscountAmount.Value) : percent;
                break;
            case CouponType.FixedAmount:
                evaluation.ItemsDiscount = Math.Min(decimal.Floor(coupon.Value), eligibleAmount);
                break;
            case CouponType.FreeShipping:
                evaluation.FreeShipping = true;
                var shipping = request.ShippingCost ?? 0;
                evaluation.ShippingDiscount = coupon.MaxDiscountAmount is > 0 ? Math.Min(shipping, coupon.MaxDiscountAmount.Value) : shipping;
                break;
            default:
                return OperationResult<CouponEvaluation>.Error(InvalidCode);
        }

        var message = coupon.Type == CouponType.FreeShipping
            ? $"کد «{coupon.Code}» اعمال شد: ارسال رایگان."
            : $"کد «{coupon.Code}» اعمال شد: {evaluation.ItemsDiscount:N0} تومان تخفیف.";

        return OperationResult<CouponEvaluation>.Success(evaluation, message);
    }

    public async Task<List<CouponDto>> GetAvailableCouponsAsync(Guid? userId)
    {
        var now = DateTimeOffset.UtcNow;

        var coupons = await context.Coupons
            .Where(c => c.IsActive
                        && (c.StartsAt == null || c.StartsAt <= now)
                        && (c.EndsAt == null || c.EndsAt >= now)
                        && (c.UsageLimit == null || c.UsedCount < c.UsageLimit)
                        && ((c.IsPublic && c.UserId == null) || (userId != null && c.UserId == userId)))
            .OrderByDescending(c => c.UserId != null)
            .ThenBy(c => c.EndsAt ?? DateTimeOffset.MaxValue)
            .ThenByDescending(c => c.CreatedAt)
            .Take(30)
            .ToListAsync();

        Dictionary<Guid, int> used = [];
        var hasOrders = false;
        if (userId.HasValue && coupons.Count > 0)
        {
            var ids = coupons.Select(c => c.Id).ToList();
            used = await context.CouponUsages
                .Where(u => u.UserId == userId && !u.IsReverted && ids.Contains(u.CouponId))
                .GroupBy(u => u.CouponId)
                .Select(g => new { g.Key, Count = g.Count() })
                .ToDictionaryAsync(x => x.Key, x => x.Count);

            hasOrders = await HasActiveOrdersAsync(userId.Value);
        }

        return coupons
            .Select(c =>
            {
                var dto = Map(c);
                if (userId.HasValue && c.UsageLimitPerUser.HasValue)
                    dto.RemainingUses = Math.Max(0, c.UsageLimitPerUser.Value - used.GetValueOrDefault(c.Id));
                return dto;
            })
            .Where(c => c.RemainingUses is null or > 0)
            .Where(c => !(c.FirstOrderOnly && hasOrders))
            .ToList();
    }

    public async Task<OperationResult> RecordUsageAsync(Guid couponId, Guid? userId, Guid orderId, decimal discount)
    {
        if (context.Database.CurrentTransaction is null)
            throw new InvalidOperationException("Coupon usage must be recorded inside the order transaction.");

        var coupon = await context.Coupons.FirstOrDefaultAsync(c => c.Id == couponId);
        if (coupon is null)
            return OperationResult.Error(InvalidCode);

        var now = DateTimeOffset.UtcNow;

        if (userId.HasValue)
        {
            // Serializes concurrent orders of the same customer until the transaction ends.
            await context.Users.Where(u => u.Id == userId.Value)
                .ExecuteUpdateAsync(s => s.SetProperty(u => u.UpdatedAt, now));

            var userError = await ValidateUserAsync(coupon, userId, requireUser: true, excludeOrderId: orderId);
            if (userError is not null)
                return OperationResult.Error(userError);
        }

        var affected = await context.Coupons
            .Where(c => c.Id == couponId && c.IsActive && (c.UsageLimit == null || c.UsedCount < c.UsageLimit))
            .ExecuteUpdateAsync(s => s
                .SetProperty(c => c.UsedCount, c => c.UsedCount + 1)
                .SetProperty(c => c.UpdatedAt, now));

        if (affected == 0)
            return OperationResult.Error("ظرفیت استفاده از این کد تخفیف تکمیل شده است.");

        context.CouponUsages.Add(new CouponUsage
        {
            CouponId = couponId,
            UserId = userId,
            OrderId = orderId,
            DiscountAmount = discount,
        });
        await context.SaveChangesAsync();

        return OperationResult.Success();
    }

    public async Task ReleaseUsageAsync(Guid orderId)
    {
        var now = DateTimeOffset.UtcNow;
        var usages = await context.CouponUsages
            .Where(u => u.OrderId == orderId && !u.IsReverted)
            .Select(u => new { u.Id, u.CouponId })
            .ToListAsync();

        foreach (var usage in usages)
        {
            var reverted = await context.CouponUsages
                .Where(u => u.Id == usage.Id && !u.IsReverted)
                .ExecuteUpdateAsync(s => s
                    .SetProperty(u => u.IsReverted, true)
                    .SetProperty(u => u.RevertedAt, now)
                    .SetProperty(u => u.UpdatedAt, now));

            if (reverted == 1)
            {
                await context.Coupons
                    .Where(c => c.Id == usage.CouponId && c.UsedCount > 0)
                    .ExecuteUpdateAsync(s => s
                        .SetProperty(c => c.UsedCount, c => c.UsedCount - 1)
                        .SetProperty(c => c.UpdatedAt, now));
            }
        }
    }

    public async Task<CouponDto> CreatePersonalCouponAsync(Guid userId, string title, CouponType type, decimal value,
        decimal? maxDiscount, decimal minOrderAmount, int validDays, string prefix = "CLUB")
    {
        string code;
        do
        {
            code = $"{prefix}-{RandomCode(6)}";
        }
        while (await context.Coupons.IgnoreQueryFilters().AnyAsync(c => c.Code == code));

        var now = DateTimeOffset.UtcNow;
        var coupon = new Coupon
        {
            Code = code,
            Title = title,
            Type = type,
            Value = value,
            MaxDiscountAmount = maxDiscount,
            MinOrderAmount = minOrderAmount,
            StartsAt = now,
            EndsAt = now.AddDays(Math.Max(1, validDays)),
            UsageLimit = 1,
            UsageLimitPerUser = 1,
            UserId = userId,
        };

        context.Coupons.Add(coupon);
        await context.SaveChangesAsync();

        return Map(coupon);
    }

    private async Task<string?> ValidateUserAsync(Coupon coupon, Guid? userId, bool requireUser, Guid? excludeOrderId = null)
    {
        var needsUser = coupon.UserId.HasValue || coupon.FirstOrderOnly || coupon.UsageLimitPerUser.HasValue;
        if (!needsUser)
            return null;

        if (userId is null)
            return requireUser || coupon.UserId.HasValue ? "برای استفاده از این کد تخفیف وارد حساب کاربری خود شوید." : null;

        if (coupon.UserId.HasValue && coupon.UserId != userId)
            return InvalidCode;

        if (coupon.FirstOrderOnly && await HasActiveOrdersAsync(userId.Value, excludeOrderId))
            return "این کد تخفیف فقط برای اولین خرید قابل استفاده است.";

        if (coupon.UsageLimitPerUser.HasValue)
        {
            var used = await context.CouponUsages
                .CountAsync(u => u.CouponId == coupon.Id && u.UserId == userId && !u.IsReverted && u.OrderId != excludeOrderId);

            if (used >= coupon.UsageLimitPerUser)
                return "سقف استفاده شما از این کد تخفیف پر شده است.";
        }

        return null;
    }

    private Task<bool> HasActiveOrdersAsync(Guid userId, Guid? excludeOrderId = null) =>
        context.Orders.AnyAsync(o => o.UserId == userId
                                     && o.Id != excludeOrderId
                                     && o.Status != OrderStatus.Cancelled
                                     && o.Status != OrderStatus.Returned);

    /// <summary>Category targets include their sub categories (any depth).</summary>
    private async Task<Dictionary<Guid, HashSet<Guid>>> ResolveCategoryScopeAsync(List<CouponTarget> targets)
    {
        var categoryTargets = targets.Where(t => t.TargetType == CouponTargetType.Category).ToList();
        if (categoryTargets.Count == 0)
            return [];

        var tree = await context.Categories
            .Select(c => new { c.Id, c.ParentId })
            .ToListAsync();

        var children = tree.Where(c => c.ParentId.HasValue).ToLookup(c => c.ParentId!.Value, c => c.Id);

        var scope = new Dictionary<Guid, HashSet<Guid>>();
        foreach (var target in categoryTargets)
        {
            var ids = new HashSet<Guid> { target.TargetId };
            var queue = new Queue<Guid>([target.TargetId]);
            while (queue.Count > 0)
            {
                foreach (var child in children[queue.Dequeue()])
                {
                    if (ids.Add(child))
                        queue.Enqueue(child);
                }
            }

            scope[target.Id] = ids;
        }

        return scope;
    }

    private static string RandomCode(int length)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return string.Create(length, alphabet, (span, chars) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
        });
    }

    private static CouponDto Map(Coupon coupon) => new()
    {
        Id = coupon.Id,
        Code = coupon.Code,
        Title = coupon.Title,
        Description = coupon.Description,
        Type = coupon.Type,
        Value = coupon.Value,
        MaxDiscountAmount = coupon.MaxDiscountAmount,
        MinOrderAmount = coupon.MinOrderAmount,
        MaxOrderAmount = coupon.MaxOrderAmount,
        StartsAt = coupon.StartsAt,
        EndsAt = coupon.EndsAt,
        FirstOrderOnly = coupon.FirstOrderOnly,
        IsPersonal = coupon.UserId.HasValue,
    };
}
