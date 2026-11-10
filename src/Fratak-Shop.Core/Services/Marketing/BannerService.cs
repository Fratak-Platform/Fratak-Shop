using Fratak_Shop.Core.DTOs.Banners.Command;
using Fratak_Shop.Core.DTOs.Banners.Query;
using Fratak_Shop.Core.Mappers.Banners;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Marketing;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Marketing;

/// <summary>
/// سرویس بنرها. دور ۲۳: کالای ناموجود ← بنرش پنهان؛ آزمون A/B؛ توقف در روز سوگواری (تنظیم، پیش‌فرض خاموش)؛ سربرگ دسته؛ شمارش نمایش/کلیک.
/// </summary>
public class BannerService(FratakShopDbContext context, ISmartConfigService? smart = null) : IBannerService
{
    /// <summary>
    /// اعتبارسنجی فیلدهای دور ۲۳ (مشترک ساخت و ویرایش). مثال: سربرگ دسته بدون دسته ← خطا.
    /// </summary>
    private async Task<string?> ValidateRound23Async(BannerPosition position, Guid? productId, Guid? categoryId)
    {
        if (position == BannerPosition.CategoryHeader && categoryId is null)
            return "برای «سربرگ دسته» دسته را انتخاب کنید.";
        if (categoryId is { } cid && !await context.Categories.AnyAsync(c => c.Id == cid))
            return "دستهٔ انتخاب‌شده پیدا نشد.";
        if (productId is { } pid && !await context.Products.AnyAsync(p => p.Id == pid))
            return "کالای انتخاب‌شده پیدا نشد.";
        return null;
    }

    public async Task<OperationResult> CreateBannerAsync(CreateBannerCommand command)
    {
        if (string.IsNullOrWhiteSpace(command.ImageName))
            return OperationResult.Error("نام تصویر بنر را وارد کنید.");

        if (command.StartAt.HasValue && command.EndAt.HasValue && command.StartAt > command.EndAt)
            return OperationResult.Error("تاریخ شروع نباید از تاریخ پایان بزرگ‌تر باشد.");

        if (await ValidateRound23Async(command.Position, command.ProductId, command.CategoryId) is { } invalid)
            return OperationResult.Error(invalid);

        var banner = new Banner
        {
            ProductId = command.ProductId,
            CategoryId = command.CategoryId,
            VariantGroup = BannerRules.NormalizeGroup(command.VariantGroup),
            Title = command.Title,
            Subtitle = command.Subtitle,
            ImageName = command.ImageName.Trim(),
            MobileImageName = command.MobileImageName,
            ImageAlt = command.ImageAlt,
            ButtonText = command.ButtonText,
            LinkUrl = command.LinkUrl,
            Position = command.Position,
            DisplayOrder = command.DisplayOrder,
            StartAt = command.StartAt,
            EndAt = command.EndAt,
            IsActive = command.IsActive,
        };

        context.Banners.Add(banner);
        await context.SaveChangesAsync();

        return OperationResult.Success("بنر با موفقیت ایجاد شد.");
    }

    public async Task<OperationResult> EditBannerAsync(EditBannerCommand command)
    {
        var banner = await context.Banners.AsTracking().FirstOrDefaultAsync(x => x.Id == command.Id);
        if (banner is null)
            return OperationResult.NotFound("بنر مورد نظر یافت نشد.");

        if (string.IsNullOrWhiteSpace(command.ImageName))
            return OperationResult.Error("نام تصویر بنر را وارد کنید.");

        if (command.StartAt.HasValue && command.EndAt.HasValue && command.StartAt > command.EndAt)
            return OperationResult.Error("تاریخ شروع نباید از تاریخ پایان بزرگ‌تر باشد.");

        banner.Title = command.Title;
        banner.Subtitle = command.Subtitle;
        banner.ImageName = command.ImageName.Trim();
        banner.MobileImageName = command.MobileImageName;
        banner.ImageAlt = command.ImageAlt;
        banner.ButtonText = command.ButtonText;
        banner.LinkUrl = command.LinkUrl;
        banner.Position = command.Position;
        banner.DisplayOrder = command.DisplayOrder;
        banner.StartAt = command.StartAt;
        banner.EndAt = command.EndAt;
        banner.IsActive = command.IsActive;
        if (await ValidateRound23Async(command.Position, command.ProductId, command.CategoryId) is { } invalid)
            return OperationResult.Error(invalid);
        banner.ProductId = command.ProductId;
        banner.CategoryId = command.CategoryId;
        banner.VariantGroup = BannerRules.NormalizeGroup(command.VariantGroup);

        await context.SaveChangesAsync();

        return OperationResult.Success("تغییرات بنر با موفقیت اعمال شد.");
    }

    public async Task<OperationResult> DeleteBannerAsync(Guid id)
    {
        var banner = await context.Banners.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (banner is null)
            return OperationResult.NotFound("بنر مورد نظر یافت نشد.");

        context.Banners.Remove(banner);
        await context.SaveChangesAsync();

        return OperationResult.Success("بنر با موفقیت حذف شد.");
    }

    public List<BannerDto> GetAllBannersAsync()
    {
        return context.Banners
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.CreatedAt)
            .ToList()
            .Map();
    }

    public async Task<List<BannerDto>> GetActiveBannersAsync(BannerPosition? position = null)
    {
        var now = DateTimeOffset.UtcNow;
        var banners = (await ActiveQuery(now, position).ToListAsync()).Map();

        var options = smart is null ? null : await smart.GetAsync();
        if (options?.PauseBannersOnMourning == true && MourningCalendar.IsMourning(now))
            banners = BannerRules.ForMourning(banners);
        if (options?.BannerAbTest != false)
            banners = BannerRules.Rotate(banners, Random.Shared.NextDouble);
        return banners;
    }

    /// <summary>
    /// دور ۲۳: سربرگ فعال یک دسته (یا null). اگر چند سربرگ هم‌گروه باشند، A/B بینشان می‌چرخد.
    /// مثال: صفحهٔ /category/headphones ← بنر ۱۹۲۰×۳۰۰ «هدفون‌های بی‌سیم».
    /// </summary>
    public async Task<BannerDto?> GetCategoryHeaderAsync(Guid categoryId)
    {
        var now = DateTimeOffset.UtcNow;
        var headers = (await ActiveQuery(now, BannerPosition.CategoryHeader).Where(b => b.CategoryId == categoryId).Take(10).ToListAsync()).Map();
        return headers.Count == 0 ? null : BannerRules.Rotate(headers, Random.Shared.NextDouble).FirstOrDefault();
    }

    /// <summary>
    /// دور ۲۳: افزودن شمارش‌های جمع‌شده در حافظه (هر چند دقیقه یک بار از BannerStats). فقط افزایش اتمی؛ بنر حذف‌شده نادیده.
    /// خروجی = تعداد بنرهای به‌روزشده.
    /// </summary>
    public async Task<int> AddStatsAsync(IReadOnlyDictionary<Guid, (int Views, int Clicks)> stats, CancellationToken cancellationToken = default)
    {
        var updated = 0;
        foreach (var (id, (views, clicks)) in stats)
        {
            if (views <= 0 && clicks <= 0)
                continue;
            var v = Math.Clamp(views, 0, 1_000_000);
            var c = Math.Clamp(clicks, 0, 1_000_000);
            updated += await context.Banners.Where(b => b.Id == id)
                .ExecuteUpdateAsync(u => u.SetProperty(b => b.Views, b => b.Views + v).SetProperty(b => b.Clicks, b => b.Clicks + c), cancellationToken);
        }
        return updated;
    }

    /// <summary>
    /// دور ۲۳: لینک مقصد بنر برای مسیر شمارش کلیک /b/{id} (فقط بنر فعال با لینک امن داخلی/https؛ وگرنه null).
    /// </summary>
    public async Task<string?> GetClickTargetAsync(Guid id)
    {
        var link = await context.Banners.Where(b => b.Id == id && b.IsActive).Select(b => b.LinkUrl).FirstOrDefaultAsync();
        return Fratak_Shop.Core.Services.Design.DesignService.SafeLink(link);
    }

    /// <summary>
    /// پرس‌وجوی بنرهای قابل نمایش: فعال، در بازهٔ زمانی و اگر کالای اصلی دارد، آن کالا فعال و موجود باشد
    /// (کالای ناموجود ← بنر پنهان، نه حذف؛ با موجود شدن دوباره خودش برمی‌گردد).
    /// </summary>
    private IQueryable<Banner> ActiveQuery(DateTimeOffset now, BannerPosition? position) =>
        context.Banners
            .Where(x => x.IsActive
                        && (position == null || x.Position == position)
                        && (!x.StartAt.HasValue || x.StartAt <= now)
                        && (!x.EndAt.HasValue || x.EndAt >= now)
                        && (x.ProductId == null || context.Products.Any(p => p.Id == x.ProductId && p.IsActive && p.StockQuantity > 0)))
            .OrderBy(x => x.DisplayOrder)
            .ThenBy(x => x.CreatedAt);

    public async Task<BannerDto?> GetBannerByIdAsync(Guid id)
    {
        var banner = await context.Banners.FirstOrDefaultAsync(x => x.Id == id);
        return banner?.Map();
    }
}
