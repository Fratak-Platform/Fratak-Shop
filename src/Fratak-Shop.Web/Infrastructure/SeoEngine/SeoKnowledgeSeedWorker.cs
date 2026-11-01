using Fratak_Shop.Core.Services.Seo.Knowledge;
using Fratak_Shop.Data.Contexts;

namespace Fratak_Shop.Web.Infrastructure.SeoEngine;

/// <summary>
/// سرویس پس‌زمینهٔ یک‌باره (مرحلهٔ شانزدهم): چند ثانیه بعد از راه‌اندازی، دادهٔ اولیهٔ جدول دانش سئو را
/// (حدود ۲۰۰ هزار ردیف واژه، املا، الگوی تیتر، مزیت، فراخوان و ...) در جدول SeoKnowledgeEntries می‌ریزد.
/// <para>چرا پس‌زمینه: ریختن این حجم چند ثانیه طول می‌کشد و نباید بالا آمدن سایت را کند کند. تا وقتی بذر کامل نشده،
/// <see cref="SeoKnowledgeService"/> همان دادهٔ فایل داخلی را به کار می‌برد؛ پس هیچ قابلیتی منتظر نمی‌ماند.</para>
/// <para>فقط وقتی نسخهٔ داده عوض شده باشد کاری انجام می‌دهد (اجرای دوباره روی دیتابیس به‌روز = یک کوئری کوچک)
/// و هرگز ردیف‌های آموخته یا ویرایش‌شدهٔ مدیر را تغییر نمی‌دهد. هر خطایی (مثلاً دیتابیس در دسترس نیست) فقط لاگ می‌شود.</para>
/// ثبت: Extensions/WebServiceCollectionExtensions.cs کنار سرویس‌های موتور سئو.
/// </summary>
public sealed class SeoKnowledgeSeedWorker(IServiceScopeFactory scopeFactory, ISeoKnowledgeService knowledge,
    ILogger<SeoKnowledgeSeedWorker> logger) : BackgroundService
{
    /// <summary>تأخیر پیش از شروع (تا مهاجرت‌ها و بذرهای اصلی تمام شوند و اولین درخواست‌ها سریع جواب بگیرند).</summary>
    public static readonly TimeSpan StartDelay = TimeSpan.FromSeconds(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        try
        {
            await Task.Delay(StartDelay, stoppingToken);
            using var scope = scopeFactory.CreateScope();
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var added = await SeoKnowledgeSeeder.EnsureSeededAsync(context, logger, stoppingToken);
            if (added > 0)
                knowledge.Invalidate(); // نمای حافظه از این به بعد از دیتابیس (با شمارنده‌های یادگیری) خوانده شود
        }
        catch (OperationCanceledException)
        {
            // خاموش شدن برنامه پیش از پایان: بار بعد از همان جا ادامه پیدا می‌کند (ردیف‌های موجود دوباره افزوده نمی‌شوند).
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "SEO knowledge seeding failed; the embedded seed stays in use until the next start.");
        }
    }
}
