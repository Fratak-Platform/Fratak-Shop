using System.Globalization;
using System.Net;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Blogs;

/// <summary>
/// به‌روزرسانی خودکار مقاله‌های قدیمی (پیشنهاد ۳۶، دور ۲۳) – بخش خالص و قطعی (بدون دیتابیس) برای آزمون‌پذیری.
/// <para>مشکل: مقالهٔ قدیمی کالاهایی را معرفی کرده که حالا ناموجود یا حذف شده‌اند؛ کارت آن‌ها هنگام نمایش حذف می‌شود و مقاله
/// کم‌کم «خالی» و کهنه می‌شود.</para>
/// <para>راه‌حل امن (بدون خطر جریمه): فقط تاریخ عوض نمی‌شود؛ یک بخش واقعی «به‌روزرسانی» با کالاهای <b>موجود امروزِ همان دسته</b>
/// (کد کوتاه کارت محصول) به انتهای مقاله اضافه می‌شود. اجرای بعدی همان بخش را جایگزین می‌کند (نه تکرار).</para>
/// <para>مثال: مقالهٔ «راهنمای خرید هدفون» با ۴ کارت که ۳تایش ناموجود شده ← بخش
/// «به‌روزرسانی آبان ۱۴۰۵: گزینه‌های موجود امروز» با ۳ هدفون موجود و پرفروش.</para>
/// <para>محل استفاده: <see cref="BlogRefreshService"/> (روزی یک بار از SmartWorker).</para>
/// </summary>
public static class BlogRefresh
{
    /// <summary>کمترین سن مقاله (روز) برای بررسی؛ مقالهٔ تازه دست نمی‌خورد.</summary>
    public const int MinAgeDays = 180;

    /// <summary>بعد از هر به‌روزرسانی (یا ویرایش دستی) دست‌کم این مدت صبر می‌شود.</summary>
    public const int MinGapDays = 90;

    /// <summary>حداکثر کالای پیشنهادی در بخش به‌روزرسانی.</summary>
    public const int MaxProducts = 3;

    /// <summary>نشانهٔ پایان عنوان بخش؛ برای پیدا کردن و جایگزینی بخش قبلی.</summary>
    public const string Marker = ": گزینه‌های موجود امروز</h2>";

    private const string HeadingStart = "<h2>به‌روزرسانی ";

    /// <summary>
    /// آیا مقاله نیاز به به‌روزرسانی دارد؟ وقتی دست‌کم نیمی از کالاهای معرفی‌شده دیگر قابل خرید نیستند.
    /// مثال: ۴ کالا، ۲ ناموجود ← بله؛ ۴ کالا، ۱ ناموجود ← خیر؛ بدون کالا ← خیر.
    /// </summary>
    public static bool NeedsRefresh(int referenced, int available) =>
        referenced > 0 && available >= 0 && (referenced - available) * 2 >= referenced;

    /// <summary>
    /// متن بخش به‌روزرسانی. نام دسته HTML-encode می‌شود؛ هر کالا یک کد کوتاه کارت است (قیمت و موجودی هنگام نمایش خوانده می‌شود).
    /// مثال: («هدفون»، [a, b]، ۱۴۰۵/۰۸/۰۱) ← «&lt;h2&gt;به‌روزرسانی آبان ۱۴۰۵: گزینه‌های موجود امروز&lt;/h2&gt;&lt;p&gt;…&lt;/p&gt;&lt;p&gt;[[product:a]]&lt;/p&gt;…».
    /// </summary>
    public static string Section(string categoryName, IReadOnlyList<string> slugs, DateOnly today)
    {
        var persian = new PersianCalendar();
        var date = today.ToDateTime(TimeOnly.MinValue);
        var month = MonthNames[persian.GetMonth(date) - 1];
        var year = PersianText.Digits(persian.GetYear(date));
        var name = WebUtility.HtmlEncode(PersianText.Clean(categoryName));
        var cards = string.Concat(slugs.Take(MaxProducts).Select(s => $"<p>[[product:{s}]]</p>"));
        return $"{HeadingStart}{month} {year}{Marker}" +
               $"<p>برخی از کالاهای معرفی‌شده در این مقاله دیگر موجود نیستند. این گزینه‌ها همین حالا در دستهٔ {name} موجودند و قیمت و موجودی‌شان همیشه به‌روز نمایش داده می‌شود:</p>" +
               cards;
    }

    /// <summary>
    /// افزودن یا جایگزینی بخش در انتهای متن. اگر بخش قبلی (با <see cref="Marker"/>) باشد، از عنوانش تا انتها جایگزین می‌شود.
    /// مثال: «متن» + بخش ← «متن بخش»؛ «متن بخشِ‌قدیمی» + بخشِ‌جدید ← «متن بخشِ‌جدید».
    /// </summary>
    public static string Apply(string content, string section)
    {
        var markerAt = content.LastIndexOf(Marker, StringComparison.Ordinal);
        if (markerAt >= 0)
        {
            var start = content.LastIndexOf(HeadingStart, markerAt, StringComparison.Ordinal);
            if (start >= 0)
                content = content[..start].TrimEnd();
        }
        return content + "\n" + section;
    }

    /// <summary>نامک‌های کالا خارج از بخش به‌روزرسانی (کالاهای اصلی مقاله).</summary>
    public static List<string> OriginalSlugs(string content)
    {
        var markerAt = content.LastIndexOf(Marker, StringComparison.Ordinal);
        if (markerAt >= 0)
        {
            var start = content.LastIndexOf(HeadingStart, markerAt, StringComparison.Ordinal);
            if (start >= 0)
                content = content[..start];
        }
        return BlogShortcodes.ProductSlugs(content);
    }

    private static readonly string[] MonthNames =
        ["فروردین", "اردیبهشت", "خرداد", "تیر", "مرداد", "شهریور", "مهر", "آبان", "آذر", "دی", "بهمن", "اسفند"];
}

/// <summary>سرویس به‌روزرسانی مقاله‌های قدیمی (پیشنهاد ۳۶). محل استفاده: SmartWorker (روزی یک بار).</summary>
public interface IBlogRefreshService
{
    /// <summary>حداکثر <paramref name="max"/> مقاله را بررسی و در صورت نیاز به‌روز می‌کند؛ تعداد مقاله‌های به‌روزشده را برمی‌گرداند.</summary>
    Task<int> RefreshAsync(DateTimeOffset now, int max = 3, CancellationToken cancellationToken = default);
}

/// <summary>
/// پیاده‌سازی <see cref="IBlogRefreshService"/>: مقاله‌های منتشرشدهٔ قدیمی (≥۱۸۰ روز، بدون ویرایش در ۹۰ روز اخیر) که کد کوتاه کالا دارند
/// بررسی می‌شوند؛ اگر نیمی از کالاهایشان ناموجود/حذف شده باشد، پرفروش‌ترین کالاهای موجود همان دسته به بخش «به‌روزرسانی» اضافه می‌شود.
/// <para>دستهٔ «دسته‌بندی نشده» هرگز استفاده نمی‌شود. تاریخ ویرایش (UpdatedAt) خودکار ثبت می‌شود و dateModified واقعی است.</para>
/// </summary>
public sealed class BlogRefreshService(FratakShopDbContext context, ILogger<BlogRefreshService> logger) : IBlogRefreshService
{
    public async Task<int> RefreshAsync(DateTimeOffset now, int max = 3, CancellationToken cancellationToken = default)
    {
        if (max <= 0)
            return 0;
        var oldest = now.AddDays(-BlogRefresh.MinAgeDays);
        var gap = now.AddDays(-BlogRefresh.MinGapDays);
        var candidates = await context.BlogPosts
            .Where(p => p.IsPublished && p.PublishedAt != null && p.PublishedAt <= oldest
                        && p.UpdatedAt <= gap && p.Content.Contains("product:")) // بدون «[»: در LIKE سرور SQL معنای ویژه دارد؛ بررسی دقیق با OriginalSlugs
            .OrderBy(p => p.UpdatedAt)
            .Select(p => new { p.Id, p.Content })
            .Take(40).ToListAsync(cancellationToken);

        var today = IranClock.Today(now);
        var updated = 0;
        foreach (var candidate in candidates)
        {
            if (updated >= max)
                break;
            var slugs = BlogRefresh.OriginalSlugs(candidate.Content);
            if (slugs.Count == 0)
                continue;
            var found = await context.Products
                .Where(p => slugs.Contains(p.Slug))
                .Select(p => new { p.Slug, p.CategoryId, Available = p.IsActive && p.PublishedAt != null && p.StockQuantity > 0 })
                .ToListAsync(cancellationToken);
            if (!BlogRefresh.NeedsRefresh(slugs.Count, found.Count(f => f.Available)))
                continue;

            var categoryId = found.GroupBy(f => f.CategoryId).OrderByDescending(g => g.Count()).Select(g => (Guid?)g.Key).FirstOrDefault();
            if (categoryId is null)
                continue;
            var category = await context.Categories
                .Where(c => c.Id == categoryId && c.IsActive && c.Slug != SmartCategoryService.UncategorizedSlug)
                .Select(c => c.Name).FirstOrDefaultAsync(cancellationToken);
            if (string.IsNullOrWhiteSpace(category))
                continue;
            var fresh = await context.Products
                .Where(p => p.CategoryId == categoryId && p.IsActive && p.PublishedAt != null && p.StockQuantity > 0 && !slugs.Contains(p.Slug))
                .OrderByDescending(p => p.SalesCount).ThenByDescending(p => p.CreatedAt)
                .Select(p => p.Slug).Take(BlogRefresh.MaxProducts).ToListAsync(cancellationToken);
            if (fresh.Count == 0)
                continue;

            var post = await context.BlogPosts.AsTracking().FirstOrDefaultAsync(p => p.Id == candidate.Id, cancellationToken);
            if (post is null)
                continue;
            post.Content = BlogRefresh.Apply(post.Content, BlogRefresh.Section(category, fresh, today));
            await context.SaveChangesAsync(cancellationToken);
            context.ChangeTracker.Clear();
            updated++;
            logger.LogInformation("Blog post {PostId} refreshed with {Count} in-stock products", candidate.Id, fresh.Count);
        }
        return updated;
    }
}
