using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Assistant;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Assistant;

/// <summary>یک ردیف فهرست پنل «دانش دستیار».</summary>
public sealed record AssistantFactRow(Guid Id, AssistantFactKind Kind, string Term, string? Aliases, string Answer, string? Url, string? Topic,
    int Shown, int Helpful, int Unhelpful, double Confidence, bool IsSeed, bool IsActive);

/// <summary>فرم افزودن/ویرایش یک دانستنی (پنل).</summary>
public sealed record AssistantFactInput(AssistantFactKind Kind, string? Term, string? Aliases, string? Answer, string? Url, string? Topic);

/// <summary>
/// دور ۲۸ – جدول «دانش دستیار»: بذر، خواندن کش‌شده، شمارش نمایش، یادگیری شبانه از 👍/👎 مشتری‌ها و مدیریت در پنل.
/// </summary>
public interface IAssistantFactService
{
    /// <summary>دانستنی‌های فعال (کش ۱۰ دقیقه). دیتابیس خالی ← دادهٔ فایل بذر (هیچ قابلیتی منتظر بذر نمی‌ماند).</summary>
    Task<IReadOnlyList<BrainFact>> FactsAsync(CancellationToken cancellationToken = default);

    /// <summary>یک بار نمایش دانستنی (شمارندهٔ Shown). شناسهٔ null (بذر هنوز در دیتابیس نیست) ← کاری نمی‌کند.</summary>
    Task RecordShownAsync(Guid? id, CancellationToken cancellationToken = default);

    /// <summary>ریختن ردیف‌های بذرِ تازه (فقط ردیف‌هایی که نیستند؛ ردیف حذف‌شده یا ویرایش‌شده دوباره ساخته نمی‌شود). خروجی: تعداد افزوده.</summary>
    Task<int> EnsureSeededAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// یادگیری شبانه: جمع 👍/👎 پیام‌هایی که از این جدول جواب گرفته‌اند (Topic «دانش:شناسه») و خاموش کردن دانستنی‌هایی که
    /// بعد از دست‌کم ۵ رأی اطمینانشان زیر ۰٫۳ رفته. خروجی: تعداد دانستنی به‌روزشده.
    /// </summary>
    Task<int> LearnAsync(CancellationToken cancellationToken = default);

    Task<(IReadOnlyList<AssistantFactRow> Items, int Total)> ListAsync(AssistantFactKind? kind, string? search, int page, int pageSize = 50, CancellationToken cancellationToken = default);

    Task<AssistantFactRow?> GetAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>افزودن (id = null) یا ویرایش. ویرایش ردیف بذر آن را «دست‌نوشته» می‌کند (بذر بعدی رویش نمی‌نویسد).</summary>
    Task<OperationResult<Guid>> SaveAsync(Guid? id, AssistantFactInput input, string? actor, CancellationToken cancellationToken = default);

    Task<OperationResult> ToggleAsync(Guid id, CancellationToken cancellationToken = default);

    Task<OperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default);
}

/// <inheritdoc />
public sealed class AssistantFactService(FratakShopDbContext context, IMemoryCache cache) : IAssistantFactService
{
    /// <summary>کلید کش دانستنی‌های فعال.</summary>
    public const string CacheKey = "assistant:facts";

    /// <summary>کمترین تعداد رأی پیش از خاموش کردن خودکار.</summary>
    public const int MinVotesToRetire = 5;

    public async Task<IReadOnlyList<BrainFact>> FactsAsync(CancellationToken cancellationToken = default)
    {
        if (cache.TryGetValue<IReadOnlyList<BrainFact>>(CacheKey, out var cached) && cached is not null)
            return cached;
        var rows = await context.AssistantFacts.AsNoTracking().Where(f => f.IsActive).Take(50_000).ToListAsync(cancellationToken);
        IReadOnlyList<BrainFact> facts = rows.Count == 0 ? AssistantBrain.FromSeedOnly : rows.Select(AssistantBrain.FromEntity).ToList();
        cache.Set(CacheKey, facts, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10), Size = 1 });
        return facts;
    }

    public async Task RecordShownAsync(Guid? id, CancellationToken cancellationToken = default)
    {
        if (id is not { } value)
            return;
        await context.AssistantFacts.Where(f => f.Id == value).ExecuteUpdateAsync(s => s.SetProperty(f => f.Shown, f => f.Shown + 1), cancellationToken);
    }

    public async Task<int> EnsureSeededAsync(CancellationToken cancellationToken = default)
    {
        var existing = await context.AssistantFacts.IgnoreQueryFilters().AsNoTracking()
            .Select(f => new { f.Kind, f.Term }).ToListAsync(cancellationToken);
        var known = existing.Select(e => Key(e.Kind, e.Term)).ToHashSet(StringComparer.Ordinal);
        var added = 0;
        foreach (var row in AssistantBrain.Seed)
        {
            var term = Clip(row.Term, 120)!;
            if (!known.Add(Key(row.Kind, term)))
                continue;
            context.AssistantFacts.Add(new AssistantFact
            {
                Kind = row.Kind,
                Term = term,
                Aliases = Clip(row.Aliases, 600),
                Answer = Clip(row.Answer, 1500)!,
                Topic = Clip(row.Topic, 60),
                Weight = row.Weight,
                IsSeed = true,
                IsActive = true,
                CreatedByName = "بذر دانش دستیار",
            });
            added++;
            if (added % 500 == 0)
                await context.SaveChangesAsync(cancellationToken);
        }
        if (added > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
            cache.Remove(CacheKey);
        }
        return added;
    }

    public async Task<int> LearnAsync(CancellationToken cancellationToken = default)
    {
        var votes = await context.AssistantMessages.AsNoTracking()
            .Where(m => m.Helpful != null && m.Topic != null && m.Topic.StartsWith(AssistantBrain.TopicPrefix))
            .GroupBy(m => m.Topic!)
            .Select(g => new { Topic = g.Key, Up = g.Count(m => m.Helpful == true), Down = g.Count(m => m.Helpful == false) })
            .ToListAsync(cancellationToken);
        var updated = 0;
        foreach (var vote in votes)
        {
            if (!Guid.TryParse(vote.Topic[AssistantBrain.TopicPrefix.Length..], out var id))
                continue;
            var fact = await context.AssistantFacts.AsTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
            if (fact is null || (fact.Helpful == vote.Up && fact.Unhelpful == vote.Down))
                continue;
            fact.Helpful = vote.Up;
            fact.Unhelpful = vote.Down;
            if (ShouldRetire(fact.Weight, vote.Up, vote.Down))
                fact.IsActive = false;
            updated++;
        }
        if (updated > 0)
        {
            await context.SaveChangesAsync(cancellationToken);
            cache.Remove(CacheKey);
        }
        return updated;
    }

    /// <summary>
    /// خاموش شود؟ فقط بعد از دست‌کم <see cref="MinVotesToRetire"/> رأی و اطمینان زیر <see cref="AssistantBrain.MinConfidence"/>.
    /// مثال: (۱، ۰ 👍، ۶ 👎) ← true؛ (۱، ۰، ۳) ← false (رأی کم).
    /// </summary>
    public static bool ShouldRetire(double weight, int helpful, int unhelpful) =>
        helpful + unhelpful >= MinVotesToRetire && AssistantBrain.Confidence(weight, helpful, unhelpful) < AssistantBrain.MinConfidence;

    public async Task<(IReadOnlyList<AssistantFactRow> Items, int Total)> ListAsync(AssistantFactKind? kind, string? search, int page, int pageSize = 50,
        CancellationToken cancellationToken = default)
    {
        var query = context.AssistantFacts.AsNoTracking();
        if (kind is { } k)
            query = query.Where(f => f.Kind == k);
        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            term = term.Length > 60 ? term[..60] : term;
            query = query.Where(f => f.Term.Contains(term) || (f.Aliases != null && f.Aliases.Contains(term)) || f.Answer.Contains(term));
        }
        var total = await query.CountAsync(cancellationToken);
        pageSize = Math.Clamp(pageSize, 10, 200);
        page = Math.Max(1, page);
        var rows = await query.OrderBy(f => f.Kind).ThenBy(f => f.Term).Skip((page - 1) * pageSize).Take(pageSize).ToListAsync(cancellationToken);
        return (rows.Select(Row).ToList(), total);
    }

    public async Task<AssistantFactRow?> GetAsync(Guid id, CancellationToken cancellationToken = default) =>
        await context.AssistantFacts.AsNoTracking().Where(f => f.Id == id).Select(f => f).FirstOrDefaultAsync(cancellationToken) is { } fact ? Row(fact) : null;

    public async Task<OperationResult<Guid>> SaveAsync(Guid? id, AssistantFactInput input, string? actor, CancellationToken cancellationToken = default)
    {
        var term = input.Term?.Trim();
        var answer = input.Answer?.Trim();
        if (string.IsNullOrWhiteSpace(term) || term.Length < 2 || term.Length > 120)
            return OperationResult<Guid>.Error("واژه یا عبارت ۲ تا ۱۲۰ نویسه باشد.");
        if (string.IsNullOrWhiteSpace(answer) || answer.Length < 2 || answer.Length > 1500)
            return OperationResult<Guid>.Error("جواب ۲ تا ۱۵۰۰ نویسه باشد.");
        if (!Enum.IsDefined(input.Kind))
            return OperationResult<Guid>.Error("نوع دانستنی معتبر نیست.");
        var url = input.Url?.Trim();
        if (!string.IsNullOrEmpty(url) && (!url.StartsWith('/') || url.StartsWith("//", StringComparison.Ordinal) || url.Contains('\\') || url.Length > 300))
            return OperationResult<Guid>.Error("لینک فقط مسیر داخلی سایت باشد (با «/» شروع شود).");
        var duplicate = await context.AssistantFacts.AnyAsync(f => f.Kind == input.Kind && f.Term == term && f.Id != (id ?? Guid.Empty), cancellationToken);
        if (duplicate)
            return OperationResult<Guid>.Error("این واژه با همین نوع قبلاً ثبت شده است.");

        AssistantFact fact;
        if (id is { } existingId)
        {
            var found = await context.AssistantFacts.AsTracking().FirstOrDefaultAsync(f => f.Id == existingId, cancellationToken);
            if (found is null)
                return OperationResult<Guid>.NotFound("دانستنی پیدا نشد.");
            fact = found;
        }
        else
        {
            fact = new AssistantFact { IsActive = true, Weight = 1, CreatedByName = Clip(actor, 100) };
            context.AssistantFacts.Add(fact);
        }
        fact.Kind = input.Kind;
        fact.Term = term;
        fact.Aliases = Clip(Aliases(input.Aliases), 600);
        fact.Answer = answer;
        fact.Url = string.IsNullOrEmpty(url) ? null : url;
        fact.Topic = Clip(input.Topic?.Trim(), 60);
        fact.IsSeed = false;
        await context.SaveChangesAsync(cancellationToken);
        cache.Remove(CacheKey);
        return OperationResult<Guid>.Success(fact.Id, "دانستنی ذخیره شد؛ دستیار از همین حالا از آن استفاده می‌کند.");
    }

    public async Task<OperationResult> ToggleAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var fact = await context.AssistantFacts.AsTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (fact is null)
            return OperationResult.NotFound("دانستنی پیدا نشد.");
        fact.IsActive = !fact.IsActive;
        await context.SaveChangesAsync(cancellationToken);
        cache.Remove(CacheKey);
        return OperationResult.Success(fact.IsActive ? "روشن شد." : "خاموش شد.");
    }

    public async Task<OperationResult> DeleteAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var fact = await context.AssistantFacts.AsTracking().FirstOrDefaultAsync(f => f.Id == id, cancellationToken);
        if (fact is null)
            return OperationResult.NotFound("دانستنی پیدا نشد.");
        context.AssistantFacts.Remove(fact);
        await context.SaveChangesAsync(cancellationToken);
        cache.Remove(CacheKey);
        return OperationResult.Success("حذف شد.");
    }

    /// <summary>شکل‌های دیگر: جداشده با «|»، «،» یا خط جدید ← «|» بدون تکرار و خالی. مثال: «a، b|a» ← «a|b».</summary>
    public static string? Aliases(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return null;
        var parts = text.Split(['|', '،', ',', '\n', '\r'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(p => p.Length >= 2).Distinct(StringComparer.Ordinal).ToList();
        return parts.Count == 0 ? null : string.Join('|', parts);
    }

    private static AssistantFactRow Row(AssistantFact f) =>
        new(f.Id, f.Kind, f.Term, f.Aliases, f.Answer, f.Url, f.Topic, f.Shown, f.Helpful, f.Unhelpful,
            AssistantBrain.Confidence(f.Weight, f.Helpful, f.Unhelpful), f.IsSeed, f.IsActive);

    private static string Key(AssistantFactKind kind, string term) => (byte)kind + ":" + term;

    private static string? Clip(string? text, int max) => text is null ? null : text.Length > max ? text[..max] : text;
}
