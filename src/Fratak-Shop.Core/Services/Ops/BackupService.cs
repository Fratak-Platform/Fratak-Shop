using System.Data;
using System.Data.Common;
using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Ops;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Ops;

// ═══════════════════════════════════════════════════════════════════════════
//  پشتیبان‌گیری خودکار (مرحلهٔ هفتم – فاز ۹)
//  • پایگاه داده: دستور BACKUP DATABASE خود SQL Server (COPY_ONLY تا زنجیرهٔ پشتیبان‌های دیگر مدیر سرور به هم نخورد،
//    CHECKSUM برای تشخیص خرابی، فشرده‌سازی اگر نسخهٔ SQL Server پشتیبانی کند) و سپس RESTORE VERIFYONLY.
//    فایل روی «سرور SQL Server» ساخته می‌شود؛ اگر SQL Server روی همان سرور سایت باشد، دانلود و پاک کردن از پنل هم ممکن است.
//  • آپلودها (اختیاری): فایل zip پوشهٔ wwwroot/uploads در App_Data/backups (IUploadsArchiver در لایهٔ Web).
//  • زمان‌بندی: N بار در روز از ساعت تنظیم‌شده به وقت ایران (BackupSchedule)؛ هر نوبت فقط یک بار حتی با چند سرور
//    (ایندکس یکتای Kind+SlotKey). نگهداری: فقط BackupKeepCount نسخهٔ آخر می‌ماند.
//  تنظیمات: «تنظیمات ← فنی و پشتیبان»؛ پنل: /admin/system/backups؛ اجرا: Web/Infrastructure/Ops/OpsWorker.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// نوبت‌های پشتیبان. مثال: runsPerDay=2، startHour=3 → ساعت ۳ و ۱۵ به وقت ایران؛ ساعت ۱۰ صبح نوبت فعلی «امروز-۰۳» است.
/// </summary>
public static class BackupSchedule
{
    /// <summary>نوبت فعلی: (کلید یکتا مثل «2026-10-26-03»، شروع نوبت به UTC).</summary>
    public static (string Key, DateTimeOffset StartUtc) CurrentSlot(DateTimeOffset nowUtc, int runsPerDay, int startHour)
    {
        runsPerDay = Math.Clamp(runsPerDay, 1, 4);
        startHour = Math.Clamp(startHour, 0, 23);
        var local = IranClock.ToLocal(nowUtc);
        var step = 24 / runsPerDay;
        // ساعت‌های امروز: start, start+step, …  (به پیمانهٔ ۲۴). آخرین ساعتی که گذشته، نوبت فعلی است.
        var hours = Enumerable.Range(0, runsPerDay).Select(i => (startHour + i * step) % 24).OrderBy(h => h).ToList();
        var date = local.Date;
        var current = hours.LastOrDefault(h => h <= local.Hour, -1);
        if (current < 0)
        {
            // هنوز به اولین نوبت امروز نرسیده‌ایم ← آخرین نوبت دیروز.
            current = hours[^1];
            date = date.AddDays(-1);
        }
        var startLocal = new DateTimeOffset(date.Year, date.Month, date.Day, current, 0, 0, local.Offset);
        return ($"{date:yyyy-MM-dd}-{current:00}", startLocal.ToUniversalTime());
    }

    /// <summary>زمان نوبت بعدی (UTC) برای نمایش در پنل.</summary>
    public static DateTimeOffset NextRun(DateTimeOffset nowUtc, int runsPerDay, int startHour)
    {
        var (_, start) = CurrentSlot(nowUtc, runsPerDay, startHour);
        var step = TimeSpan.FromHours(24 / Math.Clamp(runsPerDay, 1, 4));
        var next = start + step;
        // اگر ۲۴ بر تعداد بخش‌پذیر نباشد (نه در ۱..۴)، فاصلهٔ آخرین نوبت تا اولین نوبت روز بعد هم همین است.
        while (next <= nowUtc)
            next += step;
        return next;
    }

    /// <summary>نام فایل. مثال: «fratak-db-20261026-033000.bak».</summary>
    public static string FileName(BackupKind kind, DateTimeOffset nowUtc) =>
        $"fratak-{(kind == BackupKind.Database ? "db" : "uploads")}-{IranClock.ToLocal(nowUtc):yyyyMMdd-HHmmss}.{(kind == BackupKind.Database ? "bak" : "zip")}";

    /// <summary>
    /// مسیر کامل با جداکنندهٔ همان سیستم‌عامل پوشه (پوشهٔ SQL Server ممکن است ویندوزی باشد در حالی که سایت روی لینوکس است).
    /// مثال: («D:\\Backups», «x.bak») → «D:\\Backups\\x.bak»؛ («/var/backups/», «x.bak») → «/var/backups/x.bak».
    /// </summary>
    public static string Combine(string folder, string fileName)
    {
        var separator = folder.Contains('\\') && !folder.StartsWith('/') ? '\\' : '/';
        return folder.TrimEnd('/', '\\') + separator + fileName;
    }

    /// <summary>
    /// پوشهٔ واردشده امن است؟ فقط مسیر مطلق، بدون «..» و بدون نویسه‌های خطرناک SQL/پوسته.
    /// مثال معتبر: «/var/opt/mssql/backups» یا «D:\Backups»؛ نامعتبر: «../etc» یا «C:\x';DROP».
    /// </summary>
    /// <summary>پوشهٔ یک مسیر فایل با هر دو جداکننده؛ مثال: «C:\\Data\\x.mdf» → «C:\\Data»، «/var/opt/mssql/data/x.mdf» → «/var/opt/mssql/data».</summary>
    public static string? FolderOf(string? filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            return null;
        var cut = filePath.LastIndexOfAny(['\\', '/']);
        return cut <= 0 ? null : filePath[..cut];
    }

    /// <summary>اولین پوشهٔ غیرخالی و معتبر از نامزدها (به ترتیب ترجیح)؛ null اگر هیچ‌کدام نبود.</summary>
    public static string? FirstFolder(params string?[] candidates) =>
        candidates.Select(c => c?.Trim()).FirstOrDefault(c => !string.IsNullOrEmpty(c) && IsValidFolder(c));

    public static bool IsValidFolder(string? folder)
    {
        if (string.IsNullOrWhiteSpace(folder))
            return true;
        var value = folder.Trim();
        if (value.Length > 400 || value.Contains("..") || value.IndexOfAny(['\'', '"', ';', '`', '|', '&', '<', '>', '*', '?', '\0', '\n', '\r']) >= 0)
            return false;
        var unix = value.StartsWith('/');
        var windows = value.Length >= 3 && char.IsAsciiLetter(value[0]) && value[1] == ':' && value[2] == '\\';
        var unc = value.StartsWith(@"\\", StringComparison.Ordinal) && value.Length > 3;
        return unix || windows || unc;
    }
}

/// <summary>
/// ساخت فایل zip پوشهٔ آپلودها. پیاده‌سازی: Web/Infrastructure/Ops/UploadsArchiver.cs؛ پیش‌فرض Core «غیرفعال».
/// خروجی: (مسیر فایل، حجم بایت) یا خطا.
/// </summary>
public interface IUploadsArchiver
{
    bool IsAvailable { get; }

    Task<(string? Path, long Size, string? Error)> CreateAsync(string fileName, CancellationToken cancellationToken);

    /// <summary>فایل‌های پشتیبان آپلود فقط در پوشهٔ مخصوص خودشان پاک می‌شوند.</summary>
    bool Delete(string path);
}

/// <summary>پیش‌فرض بدون دسترسی به wwwroot (مثلاً در تست‌ها).</summary>
public sealed class NullUploadsArchiver : IUploadsArchiver
{
    public bool IsAvailable => false;

    public Task<(string? Path, long Size, string? Error)> CreateAsync(string fileName, CancellationToken cancellationToken) =>
        Task.FromResult<(string?, long, string?)>((null, 0, "پشتیبان آپلودها در این محیط در دسترس نیست."));

    public bool Delete(string path) => false;
}

/// <summary>نتیجهٔ یک اجرای پشتیبان (هر دو نوع).</summary>
public sealed record BackupRunResult(bool Ran, bool Ok, string Message, IReadOnlyList<BackupRecord> Records);

/// <summary>خلاصهٔ صفحهٔ پشتیبان.</summary>
public sealed record BackupOverview(
    bool Enabled, int RunsPerDay, int StartHour, int KeepCount, string? Folder, string? DefaultFolder, bool VerifyEnabled, bool UploadsEnabled,
    bool UploadsAvailable, DateTimeOffset? NextRunAt, BackupRecord? LastSuccess, BackupRecord? LastFailure, long TotalBytes, string? Edition);

/// <summary>سرویس پشتیبان‌گیری. محل استفاده: OpsWorker (زمان‌بندی) و /admin/system/backups.</summary>
public interface IBackupService
{
    Task<BackupOverview> GetOverviewAsync(CancellationToken cancellationToken = default);

    Task<List<BackupRecord>> ListAsync(int take, CancellationToken cancellationToken = default);

    Task<BackupRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>اجرای دستی (بی‌توجه به روشن/خاموش بودن زمان‌بندی). includeUploads = zip آپلودها هم ساخته شود.</summary>
    Task<BackupRunResult> RunNowAsync(string? staffName, bool includeUploads, CancellationToken cancellationToken = default);

    /// <summary>اگر نوبت زمان‌بندی رسیده و هنوز انجام نشده، پشتیبان می‌گیرد. خروجی Ran=false یعنی کاری لازم نبود.</summary>
    Task<BackupRunResult> RunDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default);

    /// <summary>آیا برای این نوبت (کلید BackupSchedule.CurrentSlot) پشتیبان پایگاه داده ثبت شده است؟ بررسی ارزان BackupWorker.</summary>
    Task<bool> HasSlotAsync(string slotKey, CancellationToken cancellationToken = default);

    /// <summary>حذف یک نسخه (فایل هم اگر در دسترس سایت باشد).</summary>
    Task<OperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

public sealed class BackupService(
    FratakShopDbContext context,
    IStoreSettingService settings,
    IUploadsArchiver uploads,
    IStaffNotificationService notifications,
    ILogger<BackupService> logger) : IBackupService
{
    /// <summary>یک پشتیبان در هر لحظه در هر سرور (دستی و زمان‌بندی‌شده با هم اجرا نشوند).</summary>
    private static readonly SemaphoreSlim Gate = new(1, 1);

    /// <summary>پشتیبان پایگاه دادهٔ بزرگ ممکن است چند دقیقه طول بکشد.</summary>
    private static readonly TimeSpan CommandTimeout = TimeSpan.FromMinutes(60);

    public async Task<BackupOverview> GetOverviewAsync(CancellationToken cancellationToken = default)
    {
        var s = await settings.GetAsync(cancellationToken);
        var lastSuccess = await context.BackupRecords.AsNoTracking().Where(b => b.Kind == BackupKind.Database && b.Status == BackupStatus.Succeeded)
            .OrderByDescending(b => b.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        var lastFailure = await context.BackupRecords.AsNoTracking().Where(b => b.Status == BackupStatus.Failed)
            .OrderByDescending(b => b.CreatedAt).FirstOrDefaultAsync(cancellationToken);
        var total = await context.BackupRecords.AsNoTracking().Where(b => b.Status == BackupStatus.Succeeded).SumAsync(b => b.SizeBytes ?? 0, cancellationToken);
        var (defaultFolder, edition) = await ServerInfoAsync(cancellationToken);
        return new BackupOverview(s.IsBackupEnabled, s.BackupRunsPerDay, s.BackupStartHour, s.BackupKeepCount, s.BackupFolder, defaultFolder,
            s.IsBackupVerifyEnabled, s.IsUploadsBackupEnabled, uploads.IsAvailable,
            s.IsBackupEnabled ? BackupSchedule.NextRun(DateTimeOffset.UtcNow, s.BackupRunsPerDay, s.BackupStartHour) : null,
            lastSuccess, lastFailure, total, edition);
    }

    public Task<List<BackupRecord>> ListAsync(int take, CancellationToken cancellationToken = default) =>
        context.BackupRecords.AsNoTracking().OrderByDescending(b => b.CreatedAt).Take(Math.Clamp(take, 1, 500)).ToListAsync(cancellationToken);

    public Task<BackupRecord?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        context.BackupRecords.AsNoTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);

    public async Task<BackupRunResult> RunNowAsync(string? staffName, bool includeUploads, CancellationToken cancellationToken = default)
    {
        if (!await Gate.WaitAsync(TimeSpan.Zero, cancellationToken))
            return new BackupRunResult(false, false, "یک پشتیبان‌گیری در حال اجراست؛ چند دقیقهٔ دیگر دوباره امتحان کنید.", []);
        try
        {
            var s = await settings.GetAsync(cancellationToken);
            var records = new List<BackupRecord> { await BackupDatabaseAsync(s.BackupFolder, s.IsBackupVerifyEnabled, true, staffName, null, cancellationToken) };
            if (includeUploads && uploads.IsAvailable)
                records.Add(await BackupUploadsAsync(true, staffName, null, cancellationToken));
            await ApplyRetentionAsync(s.BackupKeepCount, cancellationToken);
            return Summarize(records);
        }
        finally
        {
            Gate.Release();
        }
    }

    public async Task<BackupRunResult> RunDueAsync(DateTimeOffset now, CancellationToken cancellationToken = default)
    {
        var s = await settings.GetAsync(cancellationToken);
        if (!s.IsBackupEnabled)
            return new BackupRunResult(false, true, "پشتیبان‌گیری خودکار خاموش است.", []);
        var (slot, _) = BackupSchedule.CurrentSlot(now, s.BackupRunsPerDay, s.BackupStartHour);
        if (await context.BackupRecords.AnyAsync(b => b.Kind == BackupKind.Database && b.SlotKey == slot, cancellationToken))
            return new BackupRunResult(false, true, "نوبت فعلی انجام شده است.", []);

        if (!await Gate.WaitAsync(TimeSpan.Zero, cancellationToken))
            return new BackupRunResult(false, true, "پشتیبان دیگری در حال اجراست.", []);
        try
        {
            var records = new List<BackupRecord>();
            var db = await BackupDatabaseAsync(s.BackupFolder, s.IsBackupVerifyEnabled, false, null, slot, cancellationToken);
            if (db.Id == Guid.Empty)
                return new BackupRunResult(false, true, "سرور دیگری این نوبت را انجام می‌دهد.", []);
            records.Add(db);
            if (s.IsUploadsBackupEnabled && uploads.IsAvailable)
                records.Add(await BackupUploadsAsync(false, null, slot, cancellationToken));
            await ApplyRetentionAsync(s.BackupKeepCount, cancellationToken);

            var result = Summarize(records);
            if (!result.Ok)
            {
                // شکست پشتیبان خودکار مهم است: کارکنان فنی باخبر شوند (اعلان پنل + پوش).
                await notifications.NotifyAsync(new StaffAlert(StaffNotificationKind.Alert, "پشتیبان‌گیری خودکار ناموفق بود", result.Message,
                    "/admin/system/backups", [AdminPermissions.SystemManage]));
            }
            return result;
        }
        finally
        {
            Gate.Release();
        }
    }

    public Task<bool> HasSlotAsync(string slotKey, CancellationToken cancellationToken = default) =>
        context.BackupRecords.AnyAsync(b => b.Kind == BackupKind.Database && b.SlotKey == slotKey, cancellationToken);

    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var record = await context.BackupRecords.AsTracking().FirstOrDefaultAsync(b => b.Id == id, cancellationToken);
        if (record is null)
            return OperationResult.NotFound("نسخهٔ پشتیبان پیدا نشد.");
        if (record.Status == BackupStatus.Running && record.CreatedAt > DateTimeOffset.UtcNow.AddHours(-2))
            return OperationResult.Error("این پشتیبان هنوز در حال اجراست.");
        var note = DeleteFile(record);
        record.Status = BackupStatus.Removed;
        record.Message = note;
        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success(note ?? "نسخهٔ پشتیبان حذف شد.");
    }

    // ── پایگاه داده ─────────────────────────────────────────────────────

    /// <summary>
    /// اجرای BACKUP DATABASE. ردیف «در حال اجرا» اول ذخیره می‌شود (با کلید نوبت؛ اگر سرور دیگری زودتر ثبت کرده باشد خطای یکتایی
    /// می‌دهد و این سرور کنار می‌کشد → ردیف با Id خالی برمی‌گردد).
    /// </summary>
    private async Task<BackupRecord> BackupDatabaseAsync(string? folderSetting, bool verify, bool manual, string? staffName, string? slot, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        var record = new BackupRecord { Kind = BackupKind.Database, IsManual = manual, StartedBy = staffName, SlotKey = slot };
        context.BackupRecords.Add(record);
        try
        {
            await context.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException) when (slot is not null)
        {
            context.ChangeTracker.Clear();
            return new BackupRecord { Id = Guid.Empty };
        }

        try
        {
            var (defaultFolder, edition) = await ServerInfoAsync(cancellationToken);
            var folder = string.IsNullOrWhiteSpace(folderSetting) ? defaultFolder : folderSetting.Trim();
            if (string.IsNullOrWhiteSpace(folder))
                throw new BackupException("پوشهٔ پیش‌فرض پشتیبان SQL Server مشخص نیست؛ در «تنظیمات ← فنی و پشتیبان» پوشه را وارد کنید.");
            if (!BackupSchedule.IsValidFolder(folder))
                throw new BackupException("مسیر پوشهٔ پشتیبان معتبر نیست.");

            var path = BackupSchedule.Combine(folder, BackupSchedule.FileName(BackupKind.Database, now));
            record.FilePath = path;
            // فشرده‌سازی در نسخهٔ Express پشتیبانی نمی‌شود (EngineEdition = 4).
            var compress = edition is not null && !edition.Contains("Express", StringComparison.OrdinalIgnoreCase);
            var options = "COPY_ONLY, INIT, FORMAT, CHECKSUM" + (compress ? ", COMPRESSION" : "");
            // نام پایگاه داده با QUOTENAME و مسیر به‌صورت پارامتر؛ هیچ ورودی کاربر مستقیم در متن SQL نمی‌رود.
            await ExecuteAsync(
                $"DECLARE @sql nvarchar(max) = N'BACKUP DATABASE ' + QUOTENAME(DB_NAME()) + N' TO DISK = @path WITH {options}, NAME = N''Fratak-Shop backup'''; " +
                "EXEC sp_executesql @sql, N'@path nvarchar(600)', @path = {0};", path, cancellationToken);

            if (verify)
            {
                await ExecuteAsync("RESTORE VERIFYONLY FROM DISK = {0} WITH CHECKSUM;", path, cancellationToken);
                record.IsVerified = true;
            }

            record.IsLocal = SafeExists(path);
            record.SizeBytes = record.IsLocal ? new FileInfo(path).Length : await BackupSizeAsync(path, cancellationToken);
            record.Status = BackupStatus.Succeeded;
            record.Message = record.IsLocal ? null : "فایل روی سرور SQL Server است (سرور سایت به آن دسترسی مستقیم ندارد).";
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            record.Status = BackupStatus.Failed;
            record.Message = Explain(ex);
            logger.LogError(ex, "Database backup failed");
        }

        record.FinishedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(CancellationToken.None);
        return record;
    }

    private async Task<BackupRecord> BackupUploadsAsync(bool manual, string? staffName, string? slot, CancellationToken cancellationToken)
    {
        var record = new BackupRecord { Kind = BackupKind.Uploads, IsManual = manual, StartedBy = staffName, SlotKey = slot, IsLocal = true };
        context.BackupRecords.Add(record);
        await context.SaveChangesAsync(cancellationToken);
        try
        {
            var (path, size, error) = await uploads.CreateAsync(BackupSchedule.FileName(BackupKind.Uploads, DateTimeOffset.UtcNow), cancellationToken);
            record.FilePath = path;
            record.SizeBytes = path is null ? null : size;
            record.Status = path is null ? BackupStatus.Failed : BackupStatus.Succeeded;
            record.Message = error;
        }
        catch (Exception ex) when (ex is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            record.Status = BackupStatus.Failed;
            record.Message = Trim("ساخت فایل zip ناموفق بود: " + ex.Message);
            logger.LogError(ex, "Uploads backup failed");
        }
        record.FinishedAt = DateTimeOffset.UtcNow;
        await context.SaveChangesAsync(CancellationToken.None);
        return record;
    }

    /// <summary>فقط keep نسخهٔ موفق آخر از هر نوع می‌ماند؛ فایل بقیه پاک و ردیفشان «کنار گذاشته شد» می‌شود.</summary>
    private async Task ApplyRetentionAsync(int keep, CancellationToken cancellationToken)
    {
        keep = Math.Clamp(keep, 1, 60);
        foreach (var kind in new[] { BackupKind.Database, BackupKind.Uploads })
        {
            var old = await context.BackupRecords.AsTracking().Where(b => b.Kind == kind && b.Status == BackupStatus.Succeeded)
                .OrderByDescending(b => b.CreatedAt).Skip(keep).ToListAsync(cancellationToken);
            foreach (var record in old)
            {
                record.Message = DeleteFile(record);
                record.Status = BackupStatus.Removed;
            }
        }
        // ردیف‌های ناموفق/حذف‌شدهٔ بیش از ۹۰ روز فقط شلوغی‌اند.
        var cutoff = DateTimeOffset.UtcNow.AddDays(-90);
        await context.SaveChangesAsync(cancellationToken);
        await context.BackupRecords.Where(b => (b.Status == BackupStatus.Failed || b.Status == BackupStatus.Removed) && b.CreatedAt < cutoff)
            .ExecuteDeleteAsync(cancellationToken);
    }

    /// <summary>پاک کردن فایل؛ خروجی توضیحی برای ردیف (null = بدون مشکل).</summary>
    private string? DeleteFile(BackupRecord record)
    {
        if (string.IsNullOrEmpty(record.FilePath))
            return null;
        if (record.Kind == BackupKind.Uploads)
            return uploads.Delete(record.FilePath) ? null : "فایل zip پیدا نشد یا پاک نشد.";
        try
        {
            if (SafeExists(record.FilePath))
            {
                File.Delete(record.FilePath);
                return null;
            }
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Deleting backup file {Path} failed", record.FilePath);
        }
        return "فایل روی سرور SQL Server است و از سایت قابل پاک کردن نیست؛ در صورت نیاز دستی پاک کنید: " + record.FilePath;
    }

    private static bool SafeExists(string path)
    {
        try
        {
            return Path.IsPathFullyQualified(path) && File.Exists(path);
        }
        catch
        {
            return false;
        }
    }

    private async Task ExecuteAsync(string sql, string path, CancellationToken cancellationToken)
    {
        var previous = context.Database.GetCommandTimeout();
        context.Database.SetCommandTimeout(CommandTimeout);
        try
        {
            // BACKUP داخل تراکنش مجاز نیست؛ ExecuteSqlRaw بدون تراکنش صریح اجرا می‌شود.
            await context.Database.ExecuteSqlRawAsync(sql, [path], cancellationToken);
        }
        finally
        {
            context.Database.SetCommandTimeout(previous);
        }
    }

    /// <summary>پوشهٔ پیش‌فرض پشتیبان SQL Server (۲۰۱۹ به بعد) و نوع نسخه (Express/Standard/…). در صورت خطا null.</summary>
    private async Task<(string? Folder, string? Edition)> ServerInfoAsync(CancellationToken cancellationToken)
    {
        try
        {
            var connection = context.Database.GetDbConnection();
            var opened = connection.State != ConnectionState.Open;
            if (opened)
                await connection.OpenAsync(cancellationToken);
            try
            {
                await using var command = connection.CreateCommand();
                // پوشهٔ پیش‌فرض پشتیبان در LocalDB، بعضی نسخه‌های Express و نصب‌های قدیمی خالی است؛ آن‌وقت پوشهٔ پیش‌فرض داده
                // و در آخر پوشهٔ فایل خود همین پایگاه داده (جایی که سرویس SQL Server قطعاً اجازهٔ نوشتن دارد) استفاده می‌شود.
                command.CommandText = "SELECT CAST(SERVERPROPERTY('InstanceDefaultBackupPath') AS nvarchar(400)), CAST(SERVERPROPERTY('Edition') AS nvarchar(200)), " +
                                      "CAST(SERVERPROPERTY('InstanceDefaultDataPath') AS nvarchar(400)), " +
                                      "(SELECT TOP 1 physical_name FROM sys.database_files WHERE type = 0 ORDER BY file_id)";
                await using var reader = await command.ExecuteReaderAsync(cancellationToken);
                if (await reader.ReadAsync(cancellationToken))
                {
                    string? Text(int index) => reader.IsDBNull(index) ? null : reader.GetString(index);
                    var folder = BackupSchedule.FirstFolder(Text(0), Text(2), BackupSchedule.FolderOf(Text(3)));
                    return (folder, Text(1));
                }
            }
            finally
            {
                if (opened)
                    await connection.CloseAsync();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogWarning(ex, "Reading SQL Server backup properties failed");
        }
        return (null, null);
    }

    /// <summary>حجم فایل از تاریخچهٔ msdb وقتی فایل از سرور سایت دیده نمی‌شود (ممکن است کاربر دیتابیس اجازهٔ خواندن msdb نداشته باشد).</summary>
    private async Task<long?> BackupSizeAsync(string path, CancellationToken cancellationToken)
    {
        try
        {
            var connection = context.Database.GetDbConnection();
            var opened = connection.State != ConnectionState.Open;
            if (opened)
                await connection.OpenAsync(cancellationToken);
            try
            {
                await using var command = connection.CreateCommand();
                command.CommandText = "SELECT TOP 1 COALESCE(bs.compressed_backup_size, bs.backup_size) FROM msdb.dbo.backupset bs " +
                                      "JOIN msdb.dbo.backupmediafamily mf ON bs.media_set_id = mf.media_set_id " +
                                      "WHERE mf.physical_device_name = @path ORDER BY bs.backup_finish_date DESC";
                var parameter = command.CreateParameter();
                parameter.ParameterName = "@path";
                parameter.Value = path;
                command.Parameters.Add(parameter);
                var value = await command.ExecuteScalarAsync(cancellationToken);
                return value is null or DBNull ? null : Convert.ToInt64(value, System.Globalization.CultureInfo.InvariantCulture);
            }
            finally
            {
                if (opened)
                    await connection.CloseAsync();
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "Reading backup size from msdb failed");
            return null;
        }
    }

    /// <summary>پیام فارسی برای خطاهای رایج SQL Server.</summary>
    public static string Explain(Exception ex)
    {
        if (ex is BackupException backup)
            return backup.Message;
        var message = ex.Message;
        if (message.Contains("Operating system error 5", StringComparison.OrdinalIgnoreCase) || message.Contains("Access is denied", StringComparison.OrdinalIgnoreCase))
            return Trim("سرویس SQL Server اجازهٔ نوشتن در پوشهٔ پشتیبان را ندارد. دسترسی نوشتن پوشه را برای کاربر سرویس SQL Server باز کنید. (" + message + ")");
        if (message.Contains("Operating system error 3", StringComparison.OrdinalIgnoreCase) || message.Contains("Cannot open backup device", StringComparison.OrdinalIgnoreCase))
            return Trim("پوشهٔ پشتیبان روی سرور SQL Server وجود ندارد یا در دسترس نیست. (" + message + ")");
        if (message.Contains("permission", StringComparison.OrdinalIgnoreCase))
            return Trim("کاربر پایگاه دادهٔ سایت اجازهٔ پشتیبان‌گیری ندارد (نقش db_backupoperator لازم است). (" + message + ")");
        if (message.Contains("COMPRESSION", StringComparison.OrdinalIgnoreCase))
            return Trim("این نسخهٔ SQL Server فشرده‌سازی پشتیبان را پشتیبانی نمی‌کند. (" + message + ")");
        if (ex is DbException || ex is TimeoutException)
            return Trim("خطای SQL Server: " + message);
        return Trim(ex.GetType().Name + ": " + message);
    }

    private static string Trim(string text) => text.Length <= 1000 ? text : text[..999] + "…";

    private static BackupRunResult Summarize(List<BackupRecord> records)
    {
        var ok = records.All(r => r.Status == BackupStatus.Succeeded);
        var parts = records.Select(r => (r.Kind == BackupKind.Database ? "پایگاه داده" : "آپلودها") + ": " +
            (r.Status == BackupStatus.Succeeded
                ? "موفق" + (r.SizeBytes is { } size ? $" ({FormatSize(size)})" : "") + (r.IsVerified ? "، صحت فایل تأیید شد" : "")
                : "ناموفق – " + r.Message));
        return new BackupRunResult(true, ok, string.Join(" · ", parts), records);
    }

    /// <summary>حجم خوانا. مثال: 52428800 → «۵۰ مگابایت».</summary>
    public static string FormatSize(long bytes) => bytes switch
    {
        >= 1L << 30 => PersianNumbers.Digits((bytes / (double)(1L << 30)).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).Replace('.', '٫')) + " گیگابایت",
        >= 1L << 20 => PersianNumbers.Digits((bytes / (double)(1L << 20)).ToString("0.#", System.Globalization.CultureInfo.InvariantCulture).Replace('.', '٫')) + " مگابایت",
        >= 1L << 10 => PersianNumbers.Digits((bytes / 1024d).ToString("0", System.Globalization.CultureInfo.InvariantCulture)) + " کیلوبایت",
        _ => PersianNumbers.Digits(bytes) + " بایت",
    };
}

/// <summary>خطای قابل نمایش به مدیر (پیام فارسی آماده).</summary>
public sealed class BackupException(string message) : Exception(message);
