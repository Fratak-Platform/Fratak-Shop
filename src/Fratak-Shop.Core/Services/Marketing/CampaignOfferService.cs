using System.Security.Cryptography;
using Fratak_Shop.Core.Services.Design;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Marketing;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Marketing;

/// <summary>یک ردیف فهرست «قاعده‌های کمپین» در پنل (طراحی و بازاریابی).</summary>
public sealed record CampaignOfferItem(Guid Id, string Title, string VariantGroup, CampaignOfferScope Scope, string? ScopeName, int? DiscountPercent,
    decimal? PointsMultiplier, string? CouponCode, CampaignOfferStatus Status, int DurationDays, DateTimeOffset? StartsAt, DateTimeOffset? EndsAt, DateTimeOffset CreatedAt);

/// <summary>
/// دور ۲۴ (بنر هوشمند): مدیریت «قاعدهٔ واقعی» پشت بنر کمپین (<see cref="CampaignOffer"/>).
/// <list type="bullet">
/// <item><see cref="CreateDraftAsync"/>: هنگام «ایده ← طرح» یک پیش‌نویس + کوپن غیرفعال (با هدف دسته/برند/کالا) می‌سازد.</item>
/// <item>فعال‌سازی در <see cref="CampaignOffers.ActivateForGroupAsync"/> و فقط هم‌زمان با انتشار بنر (DesignService.PublishBannerAsync).</item>
/// <item><see cref="CancelAsync"/>: پیش‌نویس ← لغو (کوپن حذف)؛ کمپین فعال ← همین حالا تمام (کوپن غیرفعال).</item>
/// <item><see cref="MaintainAsync"/>: کمپین‌های گذشته ← «تمام‌شده»؛ پیش‌نویس‌های رهاشدهٔ بیش از ۳۰ روز ← لغو (داده‌ی بی‌مصرف نمی‌ماند).</item>
/// </list>
/// <para>محل استفاده: DesignStudio.BuildFromBriefAsync، صفحهٔ «استودیوی طراحی»، EngagementMaintenanceService.</para>
/// </summary>
public interface ICampaignOfferService
{
    /// <summary>کد کوپن یکتای تازه، مثل «CMP7K2QX» (حروف/ارقام بدون نویسه‌های گیج‌کننده مثل O/0 و I/1).</summary>
    Task<string> NewCodeAsync(CancellationToken cancellationToken = default);

    /// <summary>ساخت پیش‌نویس قاعده (و کوپن غیرفعال اگر درصد دارد). <paramref name="group"/> = DesignBrief.VariantGroup طرح‌ها.</summary>
    Task<CampaignOffer> CreateDraftAsync(BriefOffer offer, string group, string title, int durationDays, string? actor, CancellationToken cancellationToken = default);

    /// <summary>قاعده‌های اخیر (جدیدترها اول).</summary>
    Task<List<CampaignOfferItem>> ListAsync(int take = 30, CancellationToken cancellationToken = default);

    /// <summary>قاعدهٔ هر گروه طرح (برای نمایش کنار طرح‌ها). مثال: [«brief-1a2b3c4d»] ← {«brief-1a2b3c4d»: …}.</summary>
    Task<Dictionary<string, CampaignOfferItem>> ForGroupsAsync(IEnumerable<string> groups, CancellationToken cancellationToken = default);

    Task<OperationResult> CancelAsync(Guid id, string? actor, CancellationToken cancellationToken = default);

    /// <summary>نگه‌داری روزانه. خروجی: تعداد ردیف تغییرکرده.</summary>
    Task<int> MaintainAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

public sealed class CampaignOfferService(FratakShopDbContext context) : ICampaignOfferService
{
    /// <summary>پیش‌نویسی که این‌قدر روز منتشر نشود لغو می‌شود.</summary>
    public const int DraftLifetimeDays = 30;

    private const string CodeAlphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";

    public async Task<string> NewCodeAsync(CancellationToken cancellationToken = default)
    {
        for (var attempt = 0; attempt < 8; attempt++)
        {
            var code = "CMP" + RandomNumberGenerator.GetString(CodeAlphabet, 5);
            if (!await context.Coupons.AnyAsync(c => c.Code == code, cancellationToken))
                return code;
        }
        // احتمال برخورد ۸ بار پشت هم عملاً صفر است؛ با طول بیشتر ادامه می‌دهیم.
        return "CMP" + RandomNumberGenerator.GetString(CodeAlphabet, 8);
    }

    public async Task<CampaignOffer> CreateDraftAsync(BriefOffer offer, string group, string title, int durationDays, string? actor,
        CancellationToken cancellationToken = default)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(group);
        var entity = new CampaignOffer
        {
            Title = CampaignOffers.Cut(title, 200) ?? "کمپین",
            VariantGroup = group.Length > 80 ? group[..80] : group,
            Scope = offer.Scope,
            ScopeId = offer.Scope == CampaignOfferScope.All ? null : offer.ScopeId,
            ScopeName = CampaignOffers.Cut(offer.ScopeName, 200),
            DiscountPercent = offer.Percent is { } p ? Math.Clamp(p, 1, DesignBrief.MaxOfferPercent) : null,
            PointsMultiplier = offer.Points is { } m ? Math.Clamp(m, 2, DesignBrief.MaxPointsMultiplier) : null,
            DurationDays = Math.Clamp(durationDays, 1, 60),
            Status = CampaignOfferStatus.Draft,
            CreatedByName = CampaignOffers.Cut(actor, 150),
        };
        if (entity.DiscountPercent is { } percent)
        {
            var code = string.IsNullOrWhiteSpace(offer.Code) ? await NewCodeAsync(cancellationToken) : offer.Code.Trim().ToUpperInvariant();
            var coupon = new Coupon
            {
                Code = code,
                Title = CampaignOffers.Cut("کمپین: " + entity.Title, 200)!,
                Description = "ساخته‌شده از بنر هوشمند؛ با انتشار بنر فعال می‌شود.",
                Type = CouponType.Percentage,
                Value = CampaignOffers.CouponPercentFor(percent, entity.Scope == CampaignOfferScope.Product ? offer.ExistingDiscount : 0),
                UsageLimitPerUser = 1,
                // تخفیف روی تخفیف ← ضرر: کالاهای تخفیف‌دار مشمول نیستند، مگر کمپین مخصوص همان یک کالا باشد.
                ExcludeDiscountedProducts = entity.Scope != CampaignOfferScope.Product,
                IsPublic = true,
                IsActive = false,
            };
            if (entity.Scope != CampaignOfferScope.All && entity.ScopeId is { } targetId)
            {
                coupon.Targets.Add(new CouponTarget
                {
                    TargetType = entity.Scope switch
                    {
                        CampaignOfferScope.Category => CouponTargetType.Category,
                        CampaignOfferScope.Brand => CouponTargetType.Brand,
                        _ => CouponTargetType.Product,
                    },
                    TargetId = targetId,
                });
            }
            context.Coupons.Add(coupon);
            entity.CouponId = coupon.Id;
            entity.CouponCode = code;
        }
        context.CampaignOffers.Add(entity);
        await context.SaveChangesAsync(cancellationToken);
        return entity;
    }

    public Task<List<CampaignOfferItem>> ListAsync(int take = 30, CancellationToken cancellationToken = default) =>
        Project(context.CampaignOffers.AsNoTracking().OrderByDescending(o => o.CreatedAt).Take(Math.Clamp(take, 1, 200)))
            .ToListAsync(cancellationToken);

    public async Task<Dictionary<string, CampaignOfferItem>> ForGroupsAsync(IEnumerable<string> groups, CancellationToken cancellationToken = default)
    {
        var keys = groups.Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.Ordinal).Take(200).ToList();
        if (keys.Count == 0)
            return new Dictionary<string, CampaignOfferItem>(StringComparer.Ordinal);
        var rows = await Project(context.CampaignOffers.AsNoTracking().Where(o => keys.Contains(o.VariantGroup)).OrderByDescending(o => o.CreatedAt))
            .ToListAsync(cancellationToken);
        // هر گروه یک قاعده؛ اگر چند تا بود (ساخت دوباره)، جدیدترین.
        return rows.GroupBy(r => r.VariantGroup, StringComparer.Ordinal).ToDictionary(g => g.Key, g => g.First(), StringComparer.Ordinal);
    }

    public async Task<OperationResult> CancelAsync(Guid id, string? actor, CancellationToken cancellationToken = default)
    {
        var offer = await context.CampaignOffers.AsTracking().FirstOrDefaultAsync(o => o.Id == id, cancellationToken);
        if (offer is null)
            return OperationResult.NotFound("قاعدهٔ کمپین پیدا نشد.");
        var coupon = offer.CouponId is { } couponId ? await context.Coupons.AsTracking().FirstOrDefaultAsync(c => c.Id == couponId, cancellationToken) : null;
        var now = DateTimeOffset.UtcNow;
        switch (offer.Status)
        {
            case CampaignOfferStatus.Draft:
                offer.Status = CampaignOfferStatus.Cancelled;
                // کوپن هیچ‌وقت فعال نشده ← حذف نرم (کد آزاد می‌شود).
                if (coupon is not null && coupon.UsedCount == 0)
                    context.Coupons.Remove(coupon);
                await context.SaveChangesAsync(cancellationToken);
                return OperationResult.Success("پیش‌نویس کمپین لغو شد.");
            case CampaignOfferStatus.Active:
                offer.Status = CampaignOfferStatus.Ended;
                offer.EndsAt = now;
                offer.ActivatedByName ??= CampaignOffers.Cut(actor, 150);
                if (coupon is not null)
                {
                    coupon.IsActive = false;
                    coupon.EndsAt = now;
                }
                await context.SaveChangesAsync(cancellationToken);
                return OperationResult.Success("کمپین همین حالا متوقف شد (کد تخفیف و ضریب امتیاز دیگر اعمال نمی‌شود).");
            default:
                return OperationResult.Error("این کمپین قبلاً تمام یا لغو شده است.");
        }
    }

    public async Task<int> MaintainAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var ended = await context.CampaignOffers
            .Where(o => o.Status == CampaignOfferStatus.Active && o.EndsAt != null && o.EndsAt <= now)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, CampaignOfferStatus.Ended).SetProperty(o => o.UpdatedAt, now), cancellationToken);

        var staleBefore = now.AddDays(-DraftLifetimeDays);
        var stale = await context.CampaignOffers.AsTracking()
            .Where(o => o.Status == CampaignOfferStatus.Draft && o.CreatedAt < staleBefore)
            .Take(200)
            .ToListAsync(cancellationToken);
        if (stale.Count > 0)
        {
            var couponIds = stale.Where(o => o.CouponId != null).Select(o => o.CouponId!.Value).ToList();
            var coupons = await context.Coupons.AsTracking().Where(c => couponIds.Contains(c.Id) && !c.IsActive && c.UsedCount == 0).ToListAsync(cancellationToken);
            context.Coupons.RemoveRange(coupons);
            foreach (var offer in stale)
                offer.Status = CampaignOfferStatus.Cancelled;
            await context.SaveChangesAsync(cancellationToken);
        }
        return ended + stale.Count;
    }

    private static IQueryable<CampaignOfferItem> Project(IQueryable<CampaignOffer> query) =>
        query.Select(o => new CampaignOfferItem(o.Id, o.Title, o.VariantGroup, o.Scope, o.ScopeName, o.DiscountPercent, o.PointsMultiplier, o.CouponCode,
            o.Status, o.DurationDays, o.StartsAt, o.EndsAt, o.CreatedAt));
}

/// <summary>
/// دور ۲۴: قاعده‌های خالص و کارهای «داخل همان ذخیره» کمپین (بدون DI تا DesignService و LoyaltyService سازنده‌شان عوض نشود).
/// </summary>
public static class CampaignOffers
{
    /// <summary>
    /// فعال‌سازی پیش‌نویسِ گروه طرح هم‌زمان با انتشار بنر (ذخیره را صدا‌زننده انجام می‌دهد ← اتمیک با خود بنر).
    /// کوپن: فعال، شروع = الان، پایان = الان + مدت. خروجی: قاعدهٔ فعال‌شده یا null (پیش‌نویس ندارد/قبلاً فعال شده).
    /// مثال: انتشار نسخهٔ ۲ از «brief-1a2b3c4d» ← قاعدهٔ «۲۰٪ هدفون» فعال، کد CMP7K2QX از همین لحظه تا ۷ روز معتبر.
    /// </summary>
    public static async Task<CampaignOffer?> ActivateForGroupAsync(FratakShopDbContext context, string? group, DateTimeOffset now, string? actor,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(group))
            return null;
        var offer = await context.CampaignOffers.AsTracking()
            .Where(o => o.VariantGroup == group && o.Status == CampaignOfferStatus.Draft)
            .OrderByDescending(o => o.CreatedAt)
            .FirstOrDefaultAsync(cancellationToken);
        if (offer is null)
            return null;
        offer.Status = CampaignOfferStatus.Active;
        offer.StartsAt = now;
        offer.EndsAt = now.AddDays(Math.Clamp(offer.DurationDays, 1, 60));
        offer.ActivatedByName = Cut(actor, 150);
        if (offer.CouponId is { } couponId
            && await context.Coupons.AsTracking().FirstOrDefaultAsync(c => c.Id == couponId, cancellationToken) is { } coupon)
        {
            coupon.IsActive = true;
            coupon.StartsAt = now;
            coupon.EndsAt = offer.EndsAt;
        }
        return offer;
    }

    /// <summary>
    /// متن کوتاه قاعده برای پیام‌ها. مثال: (20، 2، «CMP7K2QX») ← «تخفیف ۲۰٪ (کد CMP7K2QX) و امتیاز ۲ برابر».
    /// </summary>
    public static string Describe(int? percent, decimal? multiplier, string? code)
    {
        var parts = new List<string>();
        if (percent is { } p)
            parts.Add(code is null ? $"تخفیف {DesignTemplates.FaDigits(p + "%")}" : $"تخفیف {DesignTemplates.FaDigits(p + "%")} (کد {code})");
        if (multiplier is { } m)
            parts.Add($"امتیاز {DesignTemplates.FaDigits(decimal.ToInt32(decimal.Floor(m)).ToString(System.Globalization.CultureInfo.InvariantCulture))} برابر");
        return string.Join(" و ", parts);
    }

    /// <summary>
    /// امتیاز خرید با کمپین «امتیاز n برابر» (فقط بخش مشمول سفارش). کمپینی حساب می‌شود که هنگام ثبت سفارش فعال بوده
    /// (اگر امتیاز بعد از تحویل داده شود و کمپین تمام شده باشد، حق مشتری محفوظ است). بیشترین ضریب هر قلم حساب می‌شود، نه حاصل‌ضرب.
    /// <para>فرمول: امتیاز + کف(امتیاز × Σ(مبلغ قلم × (ضریب − ۱)) ÷ Σ مبلغ اقلام). مثال: ۱۰۰ امتیاز، نیمی از مبلغ مشمول ×۲ ← ۱۵۰.</para>
    /// <para>محل استفاده: LoyaltyService.AwardAsync (بعد از ضریب سطح باشگاه).</para>
    /// </summary>
    public static async Task<int> BoostPurchasePointsAsync(FratakShopDbContext context, Guid orderId, int points, CancellationToken cancellationToken = default)
    {
        if (points <= 0)
            return points;
        var placedAt = await context.Orders.AsNoTracking().Where(o => o.Id == orderId).Select(o => (DateTimeOffset?)o.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        if (placedAt is not { } at)
            return points;
        var offers = await context.CampaignOffers.AsNoTracking()
            .Where(o => (o.Status == CampaignOfferStatus.Active || o.Status == CampaignOfferStatus.Ended)
                        && o.PointsMultiplier > 1 && o.StartsAt != null && o.StartsAt <= at && (o.EndsAt == null || o.EndsAt > at))
            .Select(o => new { o.Scope, o.ScopeId, o.PointsMultiplier })
            .Take(50)
            .ToListAsync(cancellationToken);
        if (offers.Count == 0)
            return points;

        var items = await context.OrderItems.AsNoTracking()
            .Where(i => i.OrderId == orderId && i.ProductId != null)
            .Select(i => new { i.ProductId, i.TotalAmount, CategoryId = (Guid?)i.Product!.CategoryId, i.Product!.BrandId })
            .ToListAsync(cancellationToken);
        var total = items.Sum(i => i.TotalAmount);
        if (total <= 0)
            return points;

        // زیرشاخه‌های هر دستهٔ کمپین (کمپین «هدفون» شامل «هدفون بی‌سیم» هم هست).
        var categoryScopes = new Dictionary<Guid, HashSet<Guid>>();
        if (offers.Any(o => o.Scope == CampaignOfferScope.Category))
        {
            var tree = await context.Categories.AsNoTracking().Select(c => new { c.Id, c.ParentId }).Take(5000).ToListAsync(cancellationToken);
            foreach (var root in offers.Where(o => o.Scope == CampaignOfferScope.Category && o.ScopeId != null).Select(o => o.ScopeId!.Value).Distinct())
            {
                var set = new HashSet<Guid> { root };
                for (var added = true; added;)
                {
                    added = false;
                    foreach (var node in tree)
                    {
                        if (node.ParentId is { } parent && set.Contains(parent) && set.Add(node.Id))
                            added = true;
                    }
                }
                categoryScopes[root] = set;
            }
        }

        decimal extra = 0;
        foreach (var item in items)
        {
            var best = offers
                .Where(o => o.Scope switch
                {
                    CampaignOfferScope.All => true,
                    CampaignOfferScope.Product => o.ScopeId == item.ProductId,
                    CampaignOfferScope.Brand => o.ScopeId != null && o.ScopeId == item.BrandId,
                    CampaignOfferScope.Category => o.ScopeId is { } c && item.CategoryId is { } ic && categoryScopes.TryGetValue(c, out var scopeSet) && scopeSet.Contains(ic),
                    _ => false,
                })
                .Select(o => Math.Min(o.PointsMultiplier!.Value, DesignBrief.MaxPointsMultiplier))
                .DefaultIfEmpty(1m)
                .Max();
            extra += item.TotalAmount * (best - 1m);
        }
        if (extra <= 0)
            return points;
        var boosted = points + decimal.Floor(points * extra / total);
        return (int)Math.Min(int.MaxValue / 2, boosted);
    }

    /// <summary>
    /// درصد کوپن روی قیمتِ از قبل تخفیف‌خورده تا جمع دو تخفیف دست‌کم <paramref name="total"/> شود (گرد به بالا ← ادعای بنر کم نمی‌آید).
    /// مثال: کل ۳۰٪، تخفیف فعلی ۱۰٪ ← ۲۳٪ (۹۰٪ × ۷۷٪ = ۶۹٫۳٪ ← ۳۰٫۷٪ تخفیف کل)؛ تخفیف فعلی ۰ ← همان ۳۰٪.
    /// </summary>
    public static int CouponPercentFor(int total, int existing)
    {
        total = Math.Clamp(total, 1, 99);
        if (existing <= 0)
            return total;
        if (existing >= total)
            return 1;
        var needed = 100m - (100m - total) * 100m / (100m - existing);
        return (int)Math.Clamp(decimal.Ceiling(needed), 1m, total);
    }

    internal static string? Cut(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value))
            return null;
        value = value.Trim();
        return value.Length > max ? value[..max] : value;
    }
}
