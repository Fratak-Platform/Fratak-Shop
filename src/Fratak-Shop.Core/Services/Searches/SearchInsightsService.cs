using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Searches;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Searches;

/// <summary>
/// Smart search: synonyms (هندزفری = هدفون بی‌سیم = ایرباد …), popular searches from real
/// queries (only queries with results, counted once per visitor and hour, hideable / pinnable
/// by the admin), and "did you mean" spelling suggestions from the catalog vocabulary.
/// </summary>
public interface ISearchInsightsService
{
    /// <summary>Term groups for the product query: each word plus its synonyms.</summary>
    Task<IReadOnlyList<IReadOnlyList<string>>> ExpandAsync(string? query);

    /// <summary>Counts a search (once per visitor key and hour).</summary>
    Task LogAsync(string? query, int resultCount, string? visitorKey);

    Task<IReadOnlyList<string>> GetPopularAsync();

    /// <summary>Corrected query when some words look misspelled; null when nothing to suggest.</summary>
    Task<string?> DidYouMeanAsync(string? query);
}

public class SearchInsightsService(FratakShopDbContext context, IStoreSettingService settingService, IMemoryCache cache) : ISearchInsightsService
{
    private const int MaxVocabulary = 20_000;

    public async Task<IReadOnlyList<IReadOnlyList<string>>> ExpandAsync(string? query)
    {
        var normalized = SearchText.Normalize(query).ToLowerInvariant();
        var plain = ProductSearch.PlainGroups(normalized);
        if (plain.Count == 0)
            return plain;

        var settings = await settingService.GetAsync();
        if (!settings.IsSearchSynonymsEnabled)
            return plain;

        var synonyms = await SynonymsAsync();

        // The whole query is one known phrase ("هدفون بی سیم") → a single group.
        if (normalized.Contains(' ') && synonyms.TryGetValue(normalized, out var phraseGroup))
            return [phraseGroup];

        return plain
            .Select(group => synonyms.TryGetValue(group[0], out var alternatives) ? alternatives : group)
            .ToList();
    }

    public async Task LogAsync(string? query, int resultCount, string? visitorKey)
    {
        var display = SearchText.Normalize(query);
        if (display.Length is < 2 or > 100)
            return;

        var normalized = display.ToLowerInvariant();
        var dedupeKey = $"core:search-log:{visitorKey}:{normalized}";
        if (visitorKey is not null)
        {
            if (cache.TryGetValue(dedupeKey, out _))
                return;
            cache.Set(dedupeKey, true, TimeSpan.FromHours(1));
        }

        var now = DateTimeOffset.UtcNow;
        var updated = await context.SearchQueryLogs
            .Where(q => q.NormalizedQuery == normalized)
            .ExecuteUpdateAsync(s => s
                .SetProperty(q => q.SearchCount, q => q.SearchCount + 1)
                .SetProperty(q => q.LastResultCount, resultCount)
                .SetProperty(q => q.LastSearchedAt, now)
                .SetProperty(q => q.UpdatedAt, now));

        if (updated > 0)
            return;

        context.SearchQueryLogs.Add(new SearchQueryLog
        {
            NormalizedQuery = normalized,
            Query = display,
            SearchCount = 1,
            LastResultCount = resultCount,
            LastSearchedAt = now,
        });

        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            context.ChangeTracker.Clear(); // the same new query from two visitors at once
        }
    }

    public async Task<IReadOnlyList<string>> GetPopularAsync()
    {
        var settings = await settingService.GetAsync();
        if (!settings.IsPopularSearchesEnabled || settings.PopularSearchesCount <= 0)
            return [];

        var all = await cache.GetOrCreateAsync(CacheInvalidation.PopularSearchesKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(15);
            var since = DateTimeOffset.UtcNow.AddDays(-90);
            var minCount = settings.PopularSearchMinCount;

            return await context.SearchQueryLogs
                .Where(q => !q.IsHidden && (q.IsPinned || (q.SearchCount >= minCount && q.LastResultCount > 0 && q.LastSearchedAt >= since)))
                .OrderByDescending(q => q.IsPinned)
                .ThenByDescending(q => q.SearchCount)
                .Take(30)
                .Select(q => q.Query)
                .ToListAsync();
        }) ?? [];

        return all.Take(settings.PopularSearchesCount).ToList();
    }

    public async Task<string?> DidYouMeanAsync(string? query)
    {
        var settings = await settingService.GetAsync();
        if (!settings.IsDidYouMeanEnabled)
            return null;

        var terms = SearchText.Terms(query).Select(t => t.ToLowerInvariant()).ToList();
        if (terms.Count == 0)
            return null;

        var vocabulary = await VocabularyAsync();
        var changed = false;
        var corrected = new List<string>(terms.Count);

        foreach (var term in terms)
        {
            if (vocabulary.ContainsKey(term) || term.All(char.IsDigit))
            {
                corrected.Add(term);
                continue;
            }

            var tolerance = Fuzzy.Tolerance(term.Length);
            string? best = null;
            var bestDistance = int.MaxValue;
            var bestFrequency = 0;

            foreach (var (word, frequency) in vocabulary)
            {
                if (Math.Abs(word.Length - term.Length) > tolerance)
                    continue;

                var distance = Fuzzy.Distance(term, word, tolerance);
                if (distance > tolerance)
                    continue;

                if (distance < bestDistance || (distance == bestDistance && frequency > bestFrequency))
                    (best, bestDistance, bestFrequency) = (word, distance, frequency);
            }

            if (best is not null && best != term)
            {
                corrected.Add(best);
                changed = true;
            }
            else
            {
                corrected.Add(term);
            }
        }

        return changed ? string.Join(' ', corrected) : null;
    }

    /// <summary>Word → all equivalent forms (the word included), from the active synonym sets.</summary>
    private async Task<Dictionary<string, IReadOnlyList<string>>> SynonymsAsync() =>
        await cache.GetOrCreateAsync(CacheInvalidation.SearchSynonymsKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = CacheKeys.DefaultLifetime;
            var sets = await context.SearchSynonyms.Where(s => s.IsActive).Select(s => s.Terms).ToListAsync();
            // دور ۲۳: هم‌معنی‌های داخلی نام دسته‌ها (موس = ماوس …) کنار هم‌معنی‌های پنل؛ واژهٔ مشترک اجتماع هر دو را می‌گیرد.
            return BuildSynonymMap(sets.Concat(CategorySynonyms.Sets));
        }) ?? [];

    /// <summary>Parses "a, b, c" sets into a lookup (public for the tests).</summary>
    public static Dictionary<string, IReadOnlyList<string>> BuildSynonymMap(IEnumerable<string> sets)
    {
        var map = new Dictionary<string, IReadOnlyList<string>>(StringComparer.OrdinalIgnoreCase);
        foreach (var set in sets)
        {
            var members = set.Split([',', '،', '\n'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(term => SearchText.Normalize(term).ToLowerInvariant())
                .Where(term => term.Length > 0)
                .Distinct()
                .ToList();
            if (members.Count < 2)
                continue;

            foreach (var member in members)
            {
                // A word in two sets gets the union of both.
                map[member] = map.TryGetValue(member, out var existing)
                    ? existing.Union(members).ToList()
                    : members;
            }
        }
        return map;
    }

    /// <summary>Catalog words with how often they occur (product / brand / category names, synonyms).</summary>
    private async Task<Dictionary<string, int>> VocabularyAsync() =>
        await cache.GetOrCreateAsync(CacheInvalidation.SearchVocabularyKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(30);

            var texts = new List<string>();
            texts.AddRange(await context.Products.Where(p => p.IsActive).Select(p => p.Name).Take(5000).ToListAsync());
            texts.AddRange(await context.Products.Where(p => p.IsActive && p.EnglishName != null).Select(p => p.EnglishName!).Take(5000).ToListAsync());
            texts.AddRange(await context.Brands.Where(b => b.IsActive).Select(b => b.Name + " " + (b.EnglishName ?? string.Empty)).ToListAsync());
            texts.AddRange(await context.Categories.Where(c => c.IsActive).Select(c => c.Name).ToListAsync());
            texts.AddRange(await context.SearchSynonyms.Where(s => s.IsActive).Select(s => s.Terms.Replace(",", " ").Replace("،", " ")).ToListAsync());

            var words = new Dictionary<string, int>(StringComparer.Ordinal);
            foreach (var text in texts)
            {
                foreach (var word in SearchText.Normalize(text).ToLowerInvariant()
                             .Split([' ', '-', '/', '(', ')', '«', '»', '+', '.', '،', ','], StringSplitOptions.RemoveEmptyEntries))
                {
                    if (word.Length < 2 || word.All(char.IsDigit))
                        continue;
                    words[word] = words.GetValueOrDefault(word) + 1;
                    if (words.Count >= MaxVocabulary)
                        return words;
                }
            }
            return words;
        }) ?? [];
}
