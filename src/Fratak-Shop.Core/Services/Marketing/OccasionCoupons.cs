using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Seo.Autopilot;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Marketing;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Marketing;

/// <summary>پیش‌نویس کوپن یک مناسبت (هنوز ذخیره نشده).</summary>
public sealed record OccasionCouponDraft(string Code, string Title, string Description, DateTimeOffset StartsAt, DateTimeOffset EndsAt, string OccasionName);

/// <summary>
/// دور ۲۳ (مورد ۸۲): کوپن مناسبتی «با تأیید مدیر».
/// <para>چه می‌کند؟ ۵ تا ۱۴ روز مانده به هر مناسبت تقویم (<see cref="SeoCalendar"/>: نوروز، یلدا، روز مادر، بلک فرایدی و …)
/// یک کوپن <b>غیرفعال</b> می‌سازد و به کارکنان بازاریابی اعلان می‌دهد. تا مدیر فعالش نکند هیچ مشتری‌ای نمی‌تواند از آن استفاده کند.</para>
/// <para>قانون‌ها: ۱۰٪ با سقف ۵۰۰ هزار تومان، حداقل سفارش ۱٫۵ میلیون، هر مشتری یک بار، کالاهای تخفیف‌دار مستثنا (تا سود زیر کف نرود)؛
/// از ۳ روز قبل تا پایان روز بعد از مناسبت. کد = نام انگلیسی مناسبت + سال و ماه (مثال: YALDA2612). اگر این کد قبلاً ساخته شده باشد —
/// حتی اگر مدیر حذفش کرده باشد (یعنی «نه» گفته) — دوباره ساخته نمی‌شود.</para>
/// <para>استفاده: <see cref="OccasionCouponService"/> ← کارگر هوشمند (هر ۱۲ ساعت).</para>
/// </summary>
public static class OccasionCoupons
{
    /// <summary>نام انگلیسی کد هر مناسبت (مناسبت بی‌نام انگلیسی کوپن نمی‌گیرد).</summary>
    private static readonly Dictionary<string, string> Keys = new(StringComparer.Ordinal)
    {
        ["نوروز"] = "NOWRUZ",
        ["بازگشایی مدارس"] = "SCHOOL",
        ["شب یلدا"] = "YALDA",
        ["روز دانشجو"] = "STUDENT",
        ["سپندارمذگان (روز عشق ایرانی)"] = "SEPANDAR",
        ["روز معلم"] = "TEACHER",
        ["ولنتاین"] = "VALENTINE",
        ["بلک فرایدی"] = "BLACKFRIDAY",
        ["روز مادر"] = "MOTHER",
        ["روز پدر"] = "FATHER",
    };

    public const int MinDaysLeft = 5;
    public const int MaxDaysLeft = 14;
    public const decimal Percent = 10m;
    public const decimal MaxDiscount = 500_000m;
    public const decimal MinOrder = 1_500_000m;

    /// <summary>
    /// پیش‌نویس‌های لازم برای امروز. مثال: امروز ۱۲ روز به یلدا مانده ← [YALDA2612]؛ ۳۰ روز مانده ← [] (هنوز زود است).
    /// </summary>
    public static List<OccasionCouponDraft> Plan(DateOnly today, IEnumerable<SeoOccasion> upcoming)
    {
        var result = new List<OccasionCouponDraft>();
        foreach (var occasion in upcoming)
        {
            if (occasion.DaysLeft is < MinDaysLeft or > MaxDaysLeft || !Keys.TryGetValue(occasion.Name, out var key))
                continue;
            var code = key + occasion.Date.ToString("yyMM", System.Globalization.CultureInfo.InvariantCulture);
            var starts = IranClock.StartOfDay(occasion.Date.AddDays(-3));
            var ends = IranClock.StartOfDay(occasion.Date.AddDays(2)).AddSeconds(-1);
            result.Add(new OccasionCouponDraft(code, $"تخفیف {occasion.Name}",
                $"پیش‌نویس خودکار برای {occasion.Name}: {Percent:0}٪ تا سقف {MaxDiscount:N0} تومان، حداقل سفارش {MinOrder:N0} تومان، هر مشتری یک بار، بدون کالاهای تخفیف‌دار. تا تأیید (فعال‌سازی) مدیر استفاده نمی‌شود.",
                starts, ends, occasion.Name));
        }
        return result;
    }
}

/// <summary>سازندهٔ کوپن‌های مناسبتی غیرفعال و اعلان آن‌ها (منطق در <see cref="OccasionCoupons"/>).</summary>
public interface IOccasionCouponService
{
    /// <summary>کوپن‌های تازهٔ لازم را (غیرفعال) می‌سازد؛ تعداد ساخته‌شده را برمی‌گرداند.</summary>
    Task<int> PlanAsync(DateTimeOffset now, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class OccasionCouponService(FratakShopDbContext context, IStaffNotificationService staff) : IOccasionCouponService
{
    public async Task<int> PlanAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var today = IranClock.Today(now);
        var drafts = OccasionCoupons.Plan(today, SeoCalendar.Upcoming(today, Array.Empty<string>(), OccasionCoupons.MaxDaysLeft));
        if (drafts.Count == 0)
            return 0;
        var codes = drafts.Select(d => d.Code).ToList();
        // IgnoreQueryFilters: کوپنی که مدیر حذف کرده (یعنی رد کرده) هم حساب است و دوباره ساخته نمی‌شود.
        var existing = await context.Coupons.IgnoreQueryFilters().Where(c => codes.Contains(c.Code)).Select(c => c.Code).ToListAsync(cancellationToken);
        var created = new List<(Coupon Coupon, OccasionCouponDraft Draft)>();
        foreach (var draft in drafts.Where(d => !existing.Contains(d.Code, StringComparer.OrdinalIgnoreCase)))
        {
            var coupon = new Coupon
            {
                Code = draft.Code,
                Title = draft.Title,
                Description = draft.Description,
                Type = CouponType.Percentage,
                Value = OccasionCoupons.Percent,
                MaxDiscountAmount = OccasionCoupons.MaxDiscount,
                MinOrderAmount = OccasionCoupons.MinOrder,
                StartsAt = draft.StartsAt,
                EndsAt = draft.EndsAt,
                UsageLimitPerUser = 1,
                ExcludeDiscountedProducts = true,
                IsPublic = true,
                IsActive = false,
            };
            context.Coupons.Add(coupon);
            created.Add((coupon, draft));
        }
        if (created.Count == 0)
            return 0;
        await context.SaveChangesAsync(cancellationToken);
        foreach (var (coupon, draft) in created)
        {
            await staff.NotifyAsync(new StaffAlert(StaffNotificationKind.Approval,
                $"کوپن {draft.OccasionName} آمادهٔ تأیید است",
                $"کد {draft.Code} غیرفعال ساخته شد. اگر موافقید در صفحهٔ کوپن «فعال» را بزنید؛ اگر نه حذفش کنید (دوباره ساخته نمی‌شود).",
                $"/admin/manage/coupons/edit/{coupon.Id}", new[] { AdminPermissions.MarketingManage }));
        }
        return created.Count;
    }
}
