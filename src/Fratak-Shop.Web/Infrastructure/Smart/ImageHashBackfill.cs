using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Web.Services;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Infrastructure.Smart;

/// <summary>
/// دور ۲۳ (مورد ۱۲): پر کردن کم‌کم «اثر انگشت» عکس‌های کالا (ستون ProductImages.Hash) و حذف نرم عکس تکراری همان کالا.
/// <para>چرا کم‌کم؟ خواندن فایل و محاسبهٔ dHash برای هزاران عکس روی هاست ضعیف سنگین است؛ هر ساعت فقط <see cref="PerRun"/> عکس.</para>
/// <para>فایل گم‌شده یا خراب ← Hash = ۰ (یعنی «نامعلوم»؛ دیگر دوباره خوانده نمی‌شود و هرگز تکراری حساب نمی‌شود).</para>
/// <para>تکراری‌ها با <see cref="ImageDuplicates.Redundant"/> پیدا و حذف نرم می‌شوند (فایل روی دیسک می‌ماند؛ از سطل بازیابی برمی‌گردد).</para>
/// <para>اجرا: SmartWorker.CategorizeHourlyAsync. مثال خروجی: (۱۵۰ عکس اثر انگشت گرفت، ۲ عکس تکراری کنار رفت).</para>
/// <para>دور ۲۴ (پیشنهاد ۵۸): امتیاز کیفیت (ستون Quality) هم در همین خواندن حساب می‌شود و عکس اصلی ضعیف با بهترین عکس همان کالا عوض می‌شود.</para>
/// <para>دور ۲۸ (#۵۰): «بررسی سوژه» عکس‌های موجود هم در همین خواندن پر می‌شود (SubjectScore/SubjectFlags)؛ عکس‌های دارای آدم/واترمارک/صحنه
/// در صفحهٔ کالا و گزارش سلامت داده دیده می‌شوند تا کارشناس جایگزین کند (هیچ عکسی خودکار حذف نمی‌شود).</para>
/// </summary>
public static class ImageHashBackfill
{
    /// <summary>بیشترین عکس در هر اجرا.</summary>
    public const int PerRun = 150;

    public static async Task<(int Hashed, int Removed)> RunAsync(IServiceProvider services, CancellationToken cancellationToken)
    {
        var context = services.GetRequiredService<FratakShopDbContext>();
        var uploads = services.GetRequiredService<IUploadStore>();
        var rows = await context.ProductImages.AsTracking()
            .Where(i => i.Hash == null || i.Quality == null || i.SubjectScore == null)
            .OrderBy(i => i.Hash != null)
            .ThenBy(i => i.CreatedAt)
            .Take(PerRun)
            .ToListAsync(cancellationToken);
        if (rows.Count == 0)
            return (0, 0);

        foreach (var row in rows)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var path = uploads.GetPath(UploadArea.Products, Path.GetFileName(row.ImageName));
            ImageInfo? info = null;
            Fratak_Shop.Core.Services.Smart.SubjectVerdict? subject = null;
            if (path is not null)
            {
                try
                {
                    var bytes = await File.ReadAllBytesAsync(path, cancellationToken);
                    info = SmartImageAnalyzer.Analyze(bytes);
                    subject = info is null ? null : SmartImageAnalyzer.Subject(bytes, quality: info.Quality);
                }
                catch (IOException)
                {
                    info = null;
                }
            }
            row.Hash = info is null ? 0 : unchecked((long)info.Hash);
            // دور ۲۴ (پیشنهاد ۵۸): امتیاز کیفیت در همان خواندن فایل (۰ = فایل گم‌شده/خراب).
            row.Quality = (byte)Math.Clamp(info?.Quality ?? 0, 0, 100);
            // دور ۲۸ (#۵۰): فایل گم‌شده/خراب ← امتیاز ۰ و بدون نشانه (دوباره خوانده نمی‌شود).
            row.SubjectScore = subject is null ? (byte)0 : Fratak_Shop.Core.Services.Smart.ImageSubjectCheck.Score(subject);
            row.SubjectFlags = subject is null ? null : Fratak_Shop.Core.Services.Smart.ImageSubjectCheck.Flags(subject);
        }
        await context.SaveChangesAsync(cancellationToken);

        // دور ۲۴: اگر عکس اصلی کالا ضعیف (تار/تیره/کم‌وضوح) و عکس دیگری از همان کالا خیلی بهتر است، آن عکس اصلی می‌شود.
        var touched = rows.Select(r => r.ProductId).Distinct().ToList();
        var gallery = await context.ProductImages.AsTracking()
            .Where(i => touched.Contains(i.ProductId))
            .ToListAsync(cancellationToken);
        var switched = 0;
        foreach (var group in gallery.GroupBy(i => i.ProductId))
        {
            var pick = Fratak_Shop.Core.Services.Smart.ImageQuality.PickPrimary(group
                .Select(i => (i.Id, i.IsPrimary, i.Quality is null or 0 ? (int?)null : i.Quality)).ToList());
            if (pick is null)
                continue;
            foreach (var image in group)
                image.IsPrimary = image.Id == pick.Value;
            switched++;
        }
        if (switched > 0)
            await context.SaveChangesAsync(cancellationToken);

        var productIds = rows.Select(r => r.ProductId).Distinct().ToList();
        var fingerprints = await context.ProductImages.AsNoTracking()
            .Where(i => productIds.Contains(i.ProductId) && i.Hash != null && i.Hash != 0)
            .Select(i => new ImageFingerprint(i.Id, i.ProductId, i.IsPrimary, i.DisplayOrder, i.Hash))
            .ToListAsync(cancellationToken);
        var redundant = ImageDuplicates.Redundant(fingerprints);
        if (redundant.Count > 0)
        {
            var extra = await context.ProductImages.AsTracking().Where(i => redundant.Contains(i.Id)).ToListAsync(cancellationToken);
            context.ProductImages.RemoveRange(extra);
            await context.SaveChangesAsync(cancellationToken);
        }
        context.ChangeTracker.Clear();
        return (rows.Count, redundant.Count);
    }
}
