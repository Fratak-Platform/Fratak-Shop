using System.Data;
using Fratak_Shop.Core.Services.Seo.Engine;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.SeoEngine;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace Fratak_Shop.Core.Services.Seo.Knowledge;

/// <summary>
/// بازخورد یک پیشنهاد برای یادگیری جدول دانش (مرحلهٔ شانزدهم).
/// <para>مثال: کارشناس پیشنهاد سئوی «هدفون سونی» را با کلمهٔ کلیدی «هدفون سونی WH-1000XM5» و عنوان
/// «قیمت و خرید هدفون سونی …» پذیرفت ← Accepted = true، Keyword و Patterns پر، KeywordGroup = «product:هدفون».</para>
/// </summary>
public sealed class SeoKnowledgeFeedback
{
    public bool Accepted { get; init; }

    /// <summary>کلمهٔ کلیدی پیشنهاد (اگر بود).</summary>
    public string? Keyword { get; init; }

    /// <summary>گروه کلمه. مثال: «product:هدفون»، «blog»، «category».</summary>
    public string KeywordGroup { get; init; } = "";

    /// <summary>عنوان/متن‌های کوتاهی که پذیرفته یا رد شدند (الگوی موفق برای همان نوع صفحه).</summary>
    public IReadOnlyList<string> Patterns { get; init; } = [];

    /// <summary>نوع صفحهٔ الگوها. مثال: «product».</summary>
    public string PatternTopic { get; init; } = "";

    /// <summary>شناسهٔ قاعده‌های اعمال‌شده (<see cref="KnowledgeRule.Id"/>). مثال: «7:باطری».</summary>
    public IReadOnlyList<string> RuleIds { get; init; } = [];
}

/// <summary>فیلتر فهرست جدول دانش در پنل. Page از ۱.</summary>
public sealed record SeoKnowledgeQuery(SeoKnowledgeKind? Kind, SeoKnowledgeSource? Source, string? Search, bool? Active, int Page = 1, int Take = 50);

/// <summary>یک ردیف جدول دانش برای پنل.</summary>
public sealed record SeoKnowledgeItem(Guid Id, SeoKnowledgeKind Kind, string Term, string? Value, string Topic, double Weight,
    int Uses, int Successes, int Rejections, SeoKnowledgeSource Source, bool IsActive, DateTimeOffset UpdatedAt);

/// <summary>یک صفحه از فهرست جدول دانش.</summary>
public sealed record SeoKnowledgeList(IReadOnlyList<SeoKnowledgeItem> Items, int Total, int Page, int Take);

/// <summary>آمار جدول دانش برای پنل. ByKind = (نوع، کل، فعال، آموخته).</summary>
public sealed record SeoKnowledgeStats(int Total, int Learned, int Research, int Manual, int Disabled, int SeedVersion, bool Seeded,
    IReadOnlyList<(SeoKnowledgeKind Kind, int Total, int Active)> ByKind, DateTimeOffset? LastLearnedAt);

/// <summary>
/// دسترسی به جدول دانش سئو: نمای سریع در حافظه، یادگیری از تحقیق و تأیید/رد کارشناس، آمار.
/// <para>چرا این کار «جریمهٔ گوگل» نمی‌آورد: دانش فقط برای انتخاب بهتر (کلمهٔ کلیدی واقعی‌تر، املای درست، حذف اغراق)
/// به کار می‌رود؛ هیچ متنی از رقبا کپی نمی‌شود (فقط عبارت‌های کوتاه جست‌وجو و واژه‌های پرتکرار) و همهٔ خروجی‌ها از
/// <see cref="SpamGuard"/> می‌گذرند (تراکم کلمه، تکرار، عبارت‌های اسپم، متن پنهان).</para>
/// استفاده: SeoEngineService (سئوی خودکار و درجا)، SmartTextService (پیشنهاد هوشمند متن)، صفحهٔ /admin/seo/knowledge.
/// </summary>
public interface ISeoKnowledgeService
{
    /// <summary>نمای فعلی دانش (کش‌شده؛ حداکثر هر چند دقیقه یک بار از دیتابیس تازه می‌شود).</summary>
    Task<SeoKnowledgeBase> GetAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// ثبت عبارت‌هایی که تحقیق اینترنتی برای یک موضوع پیدا کرد (پیشنهاد جست‌وجو، واژه‌های مشترک رقبا).
    /// تکرار یک عبارت در تحقیق‌های بعدی، وزن (Uses) آن را بالا می‌برد. حداکثر ۴۰ عبارت در هر بار.
    /// </summary>
    Task RecordResearchAsync(string topic, IEnumerable<string> phrases, CancellationToken cancellationToken = default);

    /// <summary>یادگیری از پذیرش/رد کارشناس (کلمهٔ کلیدی، الگوی عنوان، قاعده‌های نگارشی).</summary>
    Task RecordOutcomeAsync(SeoKnowledgeFeedback feedback, CancellationToken cancellationToken = default);

    Task<SeoKnowledgeStats> GetStatsAsync(CancellationToken cancellationToken = default);

    /// <summary>فهرست صفحه‌بندی‌شدهٔ ردیف‌ها برای پنل (جست‌وجو در عبارت/مقدار/موضوع).</summary>
    Task<SeoKnowledgeList> ListAsync(SeoKnowledgeQuery query, CancellationToken cancellationToken = default);

    /// <summary>فعال/غیرفعال کردن یک ردیف (ردیف غیرفعال در هیچ پیشنهادی استفاده نمی‌شود). false = پیدا نشد.</summary>
    Task<bool> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default);

    /// <summary>
    /// افزودن یا به‌روزرسانی ردیف دستی (منبع «دستی»). مثال: Kind=Correction، Term=«باطری»، Value=«باتری».
    /// پاسخ: پیام خطا یا null.
    /// </summary>
    Task<string?> SaveManualAsync(SeoKnowledgeKind kind, string term, string? value, string? topic, double weight, CancellationToken cancellationToken = default);

    /// <summary>کش را باطل می‌کند تا تغییرهای پنل فوراً دیده شوند.</summary>
    void Invalidate();
}

/// <summary>
/// نگه‌دارندهٔ نمای حافظه‌ای دانش (Singleton؛ یکی برای کل سرور). بارگذاری دوباره: هر ۳۰ دقیقه، یا ۲ دقیقه بعد از یادگیری تازه،
/// یا فوراً بعد از <see cref="Invalidate"/>.
/// </summary>
public sealed class SeoKnowledgeCache
{
    private readonly SemaphoreSlim _gate = new(1, 1);
    private volatile SeoKnowledgeBase? _current;
    private DateTimeOffset _loadedAt;
    private volatile bool _dirty;

    public static readonly TimeSpan MaxAge = TimeSpan.FromMinutes(30);
    public static readonly TimeSpan DirtyAge = TimeSpan.FromMinutes(2);

    /// <summary>یادگیری تازه ثبت شد (بارگذاری دوباره با کمی تأخیر تا هر درخواست باعث بارگذاری نشود).</summary>
    public void MarkDirty() => _dirty = true;

    public void Invalidate() => _current = null;

    public async Task<SeoKnowledgeBase> GetAsync(Func<CancellationToken, Task<SeoKnowledgeBase>> load, CancellationToken cancellationToken)
    {
        var current = _current;
        var age = DateTimeOffset.UtcNow - _loadedAt;
        if (current is not null && age < MaxAge && !(_dirty && age > DirtyAge))
            return current;

        await _gate.WaitAsync(cancellationToken);
        try
        {
            current = _current;
            age = DateTimeOffset.UtcNow - _loadedAt;
            if (current is not null && age < MaxAge && !(_dirty && age > DirtyAge))
                return current;
            _dirty = false;
            var loaded = await load(cancellationToken);
            _loadedAt = DateTimeOffset.UtcNow;
            _current = loaded;
            return loaded;
        }
        finally
        {
            _gate.Release();
        }
    }
}

/// <inheritdoc cref="ISeoKnowledgeService"/>
/// <remarks>
/// هر عملیات DbContext جداگانهٔ خودش را باز می‌کند (scope تازه). چرا: یادگیری وسط کار موتور سئو صدا زده می‌شود و اگر
/// همان DbContext موتور را ذخیره می‌کرد، تغییرهای نیمه‌کارهٔ موتور هم ناخواسته ذخیره می‌شدند. خطای یادگیری هم هرگز به
/// موتور یا صفحهٔ پنل نمی‌رسد (یادگیری فقط کمکی است). به همین دلیل سرویس Singleton است.
/// </remarks>
public sealed class SeoKnowledgeService : ISeoKnowledgeService
{
    private readonly Func<(FratakShopDbContext Context, IDisposable? Lifetime)> open;
    private readonly SeoKnowledgeCache cache;
    private readonly ILogger<SeoKnowledgeService> logger;

    /// <summary>سازندهٔ DI: هر عملیات یک scope تازه می‌سازد.</summary>
    public SeoKnowledgeService(IServiceScopeFactory scopes, SeoKnowledgeCache cache, ILogger<SeoKnowledgeService> logger)
        : this(() =>
        {
            var scope = scopes.CreateScope();
            return (scope.ServiceProvider.GetRequiredService<FratakShopDbContext>(), scope);
        }, cache, logger)
    {
    }

    /// <summary>سازندهٔ تست: تابعی که DbContext تازه (و چیزی که باید بعد از کار dispose شود) می‌دهد.</summary>
    public SeoKnowledgeService(Func<(FratakShopDbContext Context, IDisposable? Lifetime)> open, SeoKnowledgeCache cache, ILogger<SeoKnowledgeService> logger)
    {
        this.open = open;
        this.cache = cache;
        this.logger = logger;
    }

    /// <summary>حداکثر عبارت آموختهٔ هر موضوع (قدیمی‌ترین کم‌وزن‌ها جای تازه‌ها را می‌دهند).</summary>
    public const int MaxPhrasesPerTopic = 60;

    public Task<SeoKnowledgeBase> GetAsync(CancellationToken cancellationToken = default) => cache.GetAsync(LoadAsync, cancellationToken);

    public void Invalidate() => cache.Invalidate();

    private async Task<SeoKnowledgeBase> LoadAsync(CancellationToken cancellationToken)
    {
        try
        {
            var (context, lifetime) = open();
            using var _ = lifetime;
            var rows = await context.SeoKnowledgeEntries.AsNoTracking()
                .Where(e => e.IsActive && e.Kind != SeoKnowledgeKind.Meta)
                .Select(e => new KnowledgeRow(e.Kind, e.Term, e.Value, e.Topic, e.Weight, e.Successes, e.Rejections, e.Source))
                .ToListAsync(cancellationToken);
            var seeded = rows.Count(r => r.Source == SeoKnowledgeSource.Seed) >= 1000;
            if (!seeded)
            {
                // هنوز بذر نخورده (اولین دقیقه‌های راه‌اندازی): دادهٔ اولیه از فایل + هرچه تا الان آموخته شده.
                var learned = rows.Where(r => r.Source != SeoKnowledgeSource.Seed);
                rows = SeoKnowledgeSeed.StoredRows.Select(r => new KnowledgeRow(r.Kind, r.Term, r.Value, r.Topic, r.Weight)).Concat(learned).ToList();
            }
            return new SeoKnowledgeBase(rows);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // دیتابیس در دسترس نیست یا جدول هنوز ساخته نشده: کار بدون دانش آموخته ادامه پیدا می‌کند.
            logger.LogWarning(ex, "SEO knowledge could not be loaded from the database; using the embedded seed.");
            return SeoKnowledgeBase.FromSeed();
        }
    }

    public async Task RecordResearchAsync(string topic, IEnumerable<string> phrases, CancellationToken cancellationToken = default)
    {
        var topicKey = Cut(PersianText.Normalize(topic), 100);
        if (topicKey.Length < 2)
            return;
        var clean = phrases
            .Select(p => PersianText.Clean(p).Trim('،', ',', '-', ':', '؛', '.', ' '))
            .Where(p => p.Length is >= 2 and <= 120 && PersianText.Tokenize(p).Count is >= 1 and <= 8 && !SpamGuard.IsSpammy(p))
            .DistinctBy(PersianText.Normalize)
            .Take(40)
            .ToList();
        if (clean.Count == 0)
            return;

        try
        {
            var (context, lifetime) = open();
            using var _ = lifetime;
            var existing = await context.SeoKnowledgeEntries.AsTracking()
                .Where(e => e.Kind == SeoKnowledgeKind.Phrase && e.Topic == topicKey)
                .ToListAsync(cancellationToken);
            var byTerm = existing.ToDictionary(e => PersianText.Normalize(e.Term), StringComparer.Ordinal);
            var now = DateTimeOffset.UtcNow;
            var added = 0;
            foreach (var phrase in clean)
            {
                var key = PersianText.Normalize(phrase);
                if (byTerm.TryGetValue(key, out var row))
                {
                    row.Weight = Math.Min(50, row.Weight + 1);
                    row.LastUsedAt = now;
                    continue;
                }
                var entry = new SeoKnowledgeEntry
                {
                    Kind = SeoKnowledgeKind.Phrase, Term = Cut(phrase, 200), Topic = topicKey, Weight = 1,
                    Source = SeoKnowledgeSource.Research, LastUsedAt = now,
                };
                context.SeoKnowledgeEntries.Add(entry);
                byTerm[key] = entry;
                added++;
            }

            // سقف هر موضوع: کم‌وزن‌ترین و قدیمی‌ترین عبارت‌های تحقیقی قبلی (نه آموخته از کارشناس) کنار می‌روند.
            var overflow = existing.Count + added - MaxPhrasesPerTopic;
            if (overflow > 0)
                foreach (var old in existing.Where(e => e.Source == SeoKnowledgeSource.Research && e.Successes == 0 && e.LastUsedAt != now)
                             .OrderBy(e => e.Weight).ThenBy(e => e.LastUsedAt).Take(overflow))
                    context.SeoKnowledgeEntries.Remove(old);

            await context.SaveChangesAsync(cancellationToken);
            cache.MarkDirty();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // دو درخواست هم‌زمان با یک عبارت یا قطعی دیتابیس: یادگیری فقط کمکی است؛ خطا نادیده گرفته می‌شود.
            logger.LogDebug(ex, "SEO knowledge research save skipped for {Topic}", topicKey);
        }
    }

    public async Task RecordOutcomeAsync(SeoKnowledgeFeedback feedback, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        try
        {
            var (context, lifetime) = open();
            using var _ = lifetime;
            if (!string.IsNullOrWhiteSpace(feedback.Keyword))
                await BumpAsync(context, SeoKnowledgeKind.Keyword, PersianText.Clean(feedback.Keyword), Cut(feedback.KeywordGroup, 100), feedback.Accepted, now, cancellationToken);
            foreach (var pattern in feedback.Patterns.Where(p => !string.IsNullOrWhiteSpace(p)).Take(4))
                await BumpAsync(context, SeoKnowledgeKind.Pattern, PersianText.Clean(pattern), Cut(feedback.PatternTopic, 100), feedback.Accepted, now, cancellationToken);

            var ids = feedback.RuleIds.Where(r => r.Contains(':')).Distinct(StringComparer.Ordinal).Take(60).ToList();
            foreach (var id in ids)
            {
                var split = id.IndexOf(':');
                if (!int.TryParse(id[..split], out var kindNumber) || !Enum.IsDefined(typeof(SeoKnowledgeKind), (byte)kindNumber))
                    continue;
                var kind = (SeoKnowledgeKind)(byte)kindNumber;
                var term = id[(split + 1)..];
                var rows = await context.SeoKnowledgeEntries.AsTracking().Where(e => e.Kind == kind && e.Term == term).ToListAsync(cancellationToken);
                foreach (var row in rows)
                {
                    if (feedback.Accepted) row.Successes++; else row.Rejections++;
                    row.Uses++;
                    row.LastUsedAt = now;
                }
            }
            await context.SaveChangesAsync(cancellationToken);
            cache.MarkDirty();
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            logger.LogDebug(ex, "SEO knowledge feedback save skipped");
        }
    }

    private static async Task BumpAsync(FratakShopDbContext context, SeoKnowledgeKind kind, string term, string topic, bool accepted, DateTimeOffset now, CancellationToken cancellationToken)
    {
        term = Cut(term, 200);
        if (term.Length < 2)
            return;
        var row = await context.SeoKnowledgeEntries.AsTracking().FirstOrDefaultAsync(e => e.Kind == kind && e.Term == term && e.Topic == topic, cancellationToken)
                  ?? context.SeoKnowledgeEntries.Local.FirstOrDefault(e => e.Kind == kind && e.Term == term && e.Topic == topic);
        if (row is null)
        {
            row = new SeoKnowledgeEntry { Kind = kind, Term = term, Topic = topic, Weight = 1, Source = SeoKnowledgeSource.Learned };
            context.SeoKnowledgeEntries.Add(row);
        }
        if (accepted) row.Successes++; else row.Rejections++;
        row.Uses++;
        row.LastUsedAt = now;
    }

    public async Task<SeoKnowledgeStats> GetStatsAsync(CancellationToken cancellationToken = default)
    {
        var (context, lifetime) = open();
        using var _ = lifetime;
        var groups = await context.SeoKnowledgeEntries.AsNoTracking()
            .Where(e => e.Kind != SeoKnowledgeKind.Meta)
            .GroupBy(e => new { e.Kind, e.Source, e.IsActive })
            .Select(g => new { g.Key.Kind, g.Key.Source, g.Key.IsActive, Count = g.Count() })
            .ToListAsync(cancellationToken);
        var lastLearned = await context.SeoKnowledgeEntries.AsNoTracking()
            .Where(e => e.Source == SeoKnowledgeSource.Learned || e.Source == SeoKnowledgeSource.Research)
            .MaxAsync(e => (DateTimeOffset?)e.UpdatedAt, cancellationToken);
        var version = await SeoKnowledgeSeeder.CurrentVersionAsync(context, cancellationToken);
        var byKind = groups.GroupBy(g => g.Kind)
            .Select(g => (g.Key, g.Sum(x => x.Count), g.Where(x => x.IsActive).Sum(x => x.Count)))
            .OrderBy(x => x.Key)
            .ToList();
        return new SeoKnowledgeStats(
            groups.Sum(g => g.Count),
            groups.Where(g => g.Source == SeoKnowledgeSource.Learned).Sum(g => g.Count),
            groups.Where(g => g.Source == SeoKnowledgeSource.Research).Sum(g => g.Count),
            groups.Where(g => g.Source == SeoKnowledgeSource.Manual).Sum(g => g.Count),
            groups.Where(g => !g.IsActive).Sum(g => g.Count),
            version,
            version >= SeoKnowledgeSeed.SeedVersion,
            byKind,
            lastLearned);
    }

    public async Task<SeoKnowledgeList> ListAsync(SeoKnowledgeQuery query, CancellationToken cancellationToken = default)
    {
        var take = Math.Clamp(query.Take, 10, 200);
        var (context, lifetime) = open();
        using var _ = lifetime;
        var rows = context.SeoKnowledgeEntries.AsNoTracking().Where(e => e.Kind != SeoKnowledgeKind.Meta);
        if (query.Kind is { } kind)
            rows = rows.Where(e => e.Kind == kind);
        if (query.Source is { } source)
            rows = rows.Where(e => e.Source == source);
        if (query.Active is { } active)
            rows = rows.Where(e => e.IsActive == active);
        var search = PersianText.Clean(query.Search);
        if (search.Length > 0)
        {
            search = Cut(search, 100);
            rows = rows.Where(e => e.Term.Contains(search) || (e.Value != null && e.Value.Contains(search)) || e.Topic.Contains(search));
        }
        var total = await rows.CountAsync(cancellationToken);
        var page = Math.Clamp(query.Page, 1, Math.Max(1, (int)Math.Ceiling(total / (double)take)));
        var items = await rows
            .OrderByDescending(e => e.Source == SeoKnowledgeSource.Learned || e.Source == SeoKnowledgeSource.Manual)
            .ThenByDescending(e => e.UpdatedAt)
            .ThenBy(e => e.Term)
            .Skip((page - 1) * take)
            .Take(take)
            .Select(e => new SeoKnowledgeItem(e.Id, e.Kind, e.Term, e.Value, e.Topic, e.Weight, e.Uses, e.Successes, e.Rejections, e.Source, e.IsActive, e.UpdatedAt))
            .ToListAsync(cancellationToken);
        return new SeoKnowledgeList(items, total, page, take);
    }

    public async Task<bool> SetActiveAsync(Guid id, bool active, CancellationToken cancellationToken = default)
    {
        var (context, lifetime) = open();
        using var _ = lifetime;
        var row = await context.SeoKnowledgeEntries.AsTracking().FirstOrDefaultAsync(e => e.Id == id && e.Kind != SeoKnowledgeKind.Meta, cancellationToken);
        if (row is null)
            return false;
        row.IsActive = active;
        await context.SaveChangesAsync(cancellationToken);
        cache.Invalidate();
        return true;
    }

    public async Task<string?> SaveManualAsync(SeoKnowledgeKind kind, string term, string? value, string? topic, double weight, CancellationToken cancellationToken = default)
    {
        if (!Enum.IsDefined(kind) || kind == SeoKnowledgeKind.Meta)
            return "نوع ردیف نامعتبر است.";
        term = PersianText.Clean(term);
        if (term.Length is < 2 or > 200)
            return "عبارت باید بین ۲ تا ۲۰۰ نویسه باشد.";
        value = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
        if (value is { Length: > 1000 })
            return "مقدار حداکثر ۱۰۰۰ نویسه است.";
        topic = Cut(PersianText.Clean(topic), 100);

        var (context, lifetime) = open();
        using var _ = lifetime;
        var row = await context.SeoKnowledgeEntries.AsTracking().FirstOrDefaultAsync(e => e.Kind == kind && e.Term == term && e.Topic == topic, cancellationToken);
        if (row is null)
        {
            row = new SeoKnowledgeEntry { Kind = kind, Term = term, Topic = topic };
            context.SeoKnowledgeEntries.Add(row);
        }
        row.Value = value;
        row.Weight = Math.Clamp(weight, 0.1, 1.5);
        row.Source = SeoKnowledgeSource.Manual;
        row.IsActive = true;
        await context.SaveChangesAsync(cancellationToken);
        cache.Invalidate();
        return null;
    }

    private static string Cut(string value, int max) => value.Length <= max ? value : value[..max];
}

/// <summary>
/// ریختن دادهٔ اولیه در جدول SeoKnowledgeEntries (حدود ۳۳ هزار ردیف). فقط وقتی نسخهٔ داده عوض شده باشد کاری انجام می‌دهد؛
/// ردیف‌های موجود (و شمارنده‌های یادگیری‌شان) دست نمی‌خورند و فقط ردیف‌های تازه اضافه می‌شوند.
/// <para>روی SQL Server با SqlBulkCopy (کمتر از چند ثانیه)؛ روی هر پایگاه دیگر با EF در دسته‌های ۲۰۰۰تایی.</para>
/// <para>استفاده: سرویس پس‌زمینهٔ Web/Infrastructure/SeoEngine/SeoKnowledgeSeedWorker چند ثانیه بعد از راه‌اندازی.</para>
/// </summary>
public static class SeoKnowledgeSeeder
{
    public const string VersionTerm = "seed-version";

    /// <summary>نسخهٔ بذر ثبت‌شده در دیتابیس (۰ = هنوز بذر نخورده).</summary>
    public static async Task<int> CurrentVersionAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        var value = await context.SeoKnowledgeEntries.AsNoTracking()
            .Where(e => e.Kind == SeoKnowledgeKind.Meta && e.Term == VersionTerm)
            .Select(e => e.Value)
            .FirstOrDefaultAsync(cancellationToken);
        return int.TryParse(value?.Split(':')[0], out var version) ? version : 0;
    }

    /// <summary>بذر در صورت نیاز. خروجی = تعداد ردیف‌های تازه (۰ = از قبل به‌روز بود).</summary>
    public static async Task<int> EnsureSeededAsync(FratakShopDbContext context, ILogger logger, CancellationToken cancellationToken = default)
    {
        var stored = SeoKnowledgeSeed.StoredRows.ToList();
        var marker = $"{SeoKnowledgeSeed.SeedVersion}:{stored.Count}";
        var meta = await context.SeoKnowledgeEntries.AsTracking()
            .FirstOrDefaultAsync(e => e.Kind == SeoKnowledgeKind.Meta && e.Term == VersionTerm, cancellationToken);
        if (meta?.Value == marker)
            return 0;

        var existing = (await context.SeoKnowledgeEntries.AsNoTracking()
                .Select(e => new { e.Kind, e.Term, e.Topic })
                .ToListAsync(cancellationToken))
            .Select(e => (e.Kind, e.Term, e.Topic))
            .ToHashSet();
        var now = DateTimeOffset.UtcNow;
        var missing = stored
            .Where(r => !existing.Contains((r.Kind, r.Term, r.Topic)))
            .DistinctBy(r => (r.Kind, r.Term, r.Topic))
            .Select(r => new SeoKnowledgeEntry
            {
                Id = Guid.NewGuid(), CreatedAt = now, UpdatedAt = now, Kind = r.Kind, Term = r.Term, Value = r.Value, Topic = r.Topic,
                Weight = r.Weight, Source = SeoKnowledgeSource.Seed, IsActive = true,
            })
            .ToList();

        if (missing.Count > 0)
        {
            if (context.Database.IsSqlServer())
                await BulkInsertAsync(context, missing, cancellationToken);
            else
            {
                context.ChangeTracker.AutoDetectChangesEnabled = false;
                foreach (var chunk in missing.Chunk(2000))
                {
                    context.SeoKnowledgeEntries.AddRange(chunk);
                    await context.SaveChangesAsync(cancellationToken);
                    context.ChangeTracker.Clear();
                }
                context.ChangeTracker.AutoDetectChangesEnabled = true;
            }
        }

        meta = await context.SeoKnowledgeEntries.AsTracking()
            .FirstOrDefaultAsync(e => e.Kind == SeoKnowledgeKind.Meta && e.Term == VersionTerm, cancellationToken);
        if (meta is null)
        {
            meta = new SeoKnowledgeEntry { Kind = SeoKnowledgeKind.Meta, Term = VersionTerm, Source = SeoKnowledgeSource.Seed };
            context.SeoKnowledgeEntries.Add(meta);
        }
        meta.Value = marker;
        meta.Weight = SeoKnowledgeSeed.SeedVersion;
        await context.SaveChangesAsync(cancellationToken);
        logger.LogInformation("SEO knowledge seed v{Version}: {Added} new rows ({Total} seed rows).", SeoKnowledgeSeed.SeedVersion, missing.Count, stored.Count);
        return missing.Count;
    }

    private static async Task BulkInsertAsync(FratakShopDbContext context, List<SeoKnowledgeEntry> rows, CancellationToken cancellationToken)
    {
        var entity = context.Model.FindEntityType(typeof(SeoKnowledgeEntry))!;
        var table = entity.GetTableName()!;
        var schema = entity.GetSchema();
        var store = Microsoft.EntityFrameworkCore.Metadata.StoreObjectIdentifier.Table(table, schema);
        string Column(string property) => entity.FindProperty(property)!.GetColumnName(store)!;

        var data = new DataTable();
        var columns = new (string Property, Type Type, Func<SeoKnowledgeEntry, object?> Get)[]
        {
            (nameof(SeoKnowledgeEntry.Id), typeof(Guid), e => e.Id),
            (nameof(SeoKnowledgeEntry.CreatedAt), typeof(DateTimeOffset), e => e.CreatedAt),
            (nameof(SeoKnowledgeEntry.UpdatedAt), typeof(DateTimeOffset), e => e.UpdatedAt),
            (nameof(SeoKnowledgeEntry.IsDeleted), typeof(bool), _ => false),
            (nameof(SeoKnowledgeEntry.Kind), typeof(byte), e => (byte)e.Kind),
            (nameof(SeoKnowledgeEntry.Term), typeof(string), e => e.Term),
            (nameof(SeoKnowledgeEntry.Value), typeof(string), e => e.Value),
            (nameof(SeoKnowledgeEntry.Topic), typeof(string), e => e.Topic),
            (nameof(SeoKnowledgeEntry.Weight), typeof(double), e => e.Weight),
            (nameof(SeoKnowledgeEntry.Uses), typeof(int), _ => 0),
            (nameof(SeoKnowledgeEntry.Successes), typeof(int), _ => 0),
            (nameof(SeoKnowledgeEntry.Rejections), typeof(int), _ => 0),
            (nameof(SeoKnowledgeEntry.Source), typeof(byte), e => (byte)e.Source),
            (nameof(SeoKnowledgeEntry.IsActive), typeof(bool), _ => true),
        };
        foreach (var column in columns)
            data.Columns.Add(Column(column.Property), column.Type);
        foreach (var row in rows)
            data.Rows.Add(columns.Select(c => c.Get(row) ?? DBNull.Value).ToArray());

        var connection = (SqlConnection)context.Database.GetDbConnection();
        var opened = connection.State != ConnectionState.Open;
        if (opened)
            await connection.OpenAsync(cancellationToken);
        try
        {
            using var bulk = new SqlBulkCopy(connection, SqlBulkCopyOptions.CheckConstraints, (SqlTransaction?)context.Database.CurrentTransaction?.GetDbTransaction())
            {
                DestinationTableName = schema is null ? $"[{table}]" : $"[{schema}].[{table}]",
                BatchSize = 5000,
                BulkCopyTimeout = 300,
            };
            foreach (DataColumn column in data.Columns)
                bulk.ColumnMappings.Add(column.ColumnName, column.ColumnName);
            await bulk.WriteToServerAsync(data, cancellationToken);
        }
        finally
        {
            if (opened)
                await connection.CloseAsync();
        }
    }
}
