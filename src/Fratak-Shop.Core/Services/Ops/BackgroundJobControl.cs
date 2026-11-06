using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Ops;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Ops;

// ═══════════════════════════════════════════════════════════════════════════
//  توقف/ادامهٔ کارهای پس‌زمینه (مرحلهٔ هفتم – فاز ۹)
//  وضعیت «متوقف» در جدول BackgroundJobSettings ذخیره می‌شود تا با ری‌استارت سایت از دست نرود.
//  لایهٔ Web (JobMonitor.SetPaused) این فهرست را در حافظه نگه می‌دارد و هر کار قبل از اجرا آن را بررسی می‌کند.
//  مثال: موقع مهاجرت سرور، «بررسی منبع تأمین‌کننده» را متوقف می‌کنید تا محصولات به‌اشتباه ناموجود نشوند.
//  پنل: /admin/system/jobs
// ═══════════════════════════════════════════════════════════════════════════

public interface IBackgroundJobControl
{
    /// <summary>نام کارهای متوقف. محل استفاده: OpsWorker (هر دقیقه) و صفحهٔ کارها.</summary>
    Task<List<string>> GetPausedAsync(CancellationToken cancellationToken = default);

    Task<List<BackgroundJobSetting>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>توقف یا ادامهٔ یک کار. نام کار باید از فهرست کارهای شناخته‌شده باشد (کنترل در صفحه).</summary>
    Task<OperationResult> SetPausedAsync(string jobName, bool paused, string staffName, CancellationToken cancellationToken = default);
}

public sealed class BackgroundJobControl(FratakShopDbContext context) : IBackgroundJobControl
{
    public Task<List<string>> GetPausedAsync(CancellationToken cancellationToken = default) =>
        context.BackgroundJobSettings.AsNoTracking().Where(j => j.IsPaused).Select(j => j.JobName).ToListAsync(cancellationToken);

    public Task<List<BackgroundJobSetting>> ListAsync(CancellationToken cancellationToken = default) =>
        context.BackgroundJobSettings.AsNoTracking().ToListAsync(cancellationToken);

    public async Task<OperationResult> SetPausedAsync(string jobName, bool paused, string staffName, CancellationToken cancellationToken = default)
    {
        var name = jobName.Trim();
        if (name.Length is 0 or > 120)
            return OperationResult.Error("نام کار معتبر نیست.");

        var row = await context.BackgroundJobSettings.AsTracking().FirstOrDefaultAsync(j => j.JobName == name, cancellationToken);
        if (row is null)
        {
            row = new BackgroundJobSetting { JobName = name };
            context.BackgroundJobSettings.Add(row);
        }
        row.IsPaused = paused;
        row.PausedAt = paused ? DateTimeOffset.UtcNow : null;
        row.PausedBy = paused ? Truncate(staffName, 120) : null;
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success(paused ? $"«{name}» متوقف شد و تا ادامهٔ دستی اجرا نمی‌شود." : $"«{name}» دوباره فعال شد.");
    }

    private static string Truncate(string value, int max) => value.Length <= max ? value : value[..max];
}
