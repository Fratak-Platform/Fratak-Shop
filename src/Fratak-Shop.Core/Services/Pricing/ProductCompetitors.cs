using System.Text.Json;
using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Core.Services.Pricing;

// ═══════════════════════════════════════════════════════════════════════════
//  مرحلهٔ پانزدهم – «قیمت رقبا برای هر محصول»
//  هدف: تیم برای هر محصول ببیند رقبا (ترب، دیجی‌کالا، باسلام و سایت‌های دیگر) چند می‌فروشند، کدام قیمت‌ها معتبرند
//  و قیمت ما در ترب چندم است؛ تا بهتر تصمیم بگیرد.
//  کجا استفاده می‌شود:
//   • کارت «قیمت رقبا» در صفحهٔ ویرایش محصول (Areas/Admin/Pages/Products/Edit)؛
//   • صفحهٔ «محصولات › رصد قیمت رقبا» (Areas/Admin/Pages/Products/Competitors)؛
//   • ذخیره بعد از هر «پیشنهاد قیمت فروش» و بعد از قیمت‌گذاری بازار محصولات درون‌ریزی‌شده (MarketPricingService).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// جزئیات آخرین بررسی قیمت رقبای یک محصول (برای کارت صفحهٔ محصول).
/// </summary>
/// <param name="Query">عبارتی که جست‌وجو شد.</param>
/// <param name="MatchedTitle">عنوان کالای انتخاب‌شده در ترب.</param>
/// <param name="CheckedAt">زمان بررسی.</param>
/// <param name="Problems">مشکل منابع در آن بررسی (اگر بود).</param>
/// <param name="Offers">همهٔ پیشنهادها با داوری دوباره (از ارزان به گران).</param>
/// <param name="Lowest">ارزان‌ترین قیمت معتبر.</param>
/// <param name="Median">میانهٔ قیمت‌های معتبر.</param>
/// <param name="TorobLowest">ارزان‌ترین فروشندهٔ معتبر ترب.</param>
/// <param name="TorobSellers">تعداد فروشندگان معتبر ترب.</param>
/// <param name="OurPrice">قیمت فعلی ما (با تخفیف فعال).</param>
/// <param name="TorobRank">رتبهٔ قیمت فعلی ما بین فروشندگان ترب (۱ = ارزان‌ترین)؛ null یعنی ترب فروشندهٔ معتبر نداشت.</param>
/// <param name="CheaperElsewhere">تعداد فروشگاه‌های بیرون از ترب که از قیمت فعلی ما ارزان‌ترند.</param>
public sealed record ProductCompetitorDetail(
    string Query, string? MatchedTitle, DateTimeOffset CheckedAt, string? Problems,
    IReadOnlyList<ReviewedOffer> Offers, decimal? Lowest, decimal? Median, decimal? TorobLowest, int TorobSellers,
    decimal OurPrice, int? TorobRank, int CheaperElsewhere);

/// <summary>یک ردیف صفحهٔ «رصد قیمت رقبا».</summary>
/// <param name="State">«cheapest» (ارزان‌تر از همهٔ فروشندگان ترب/رقبا)، «above» (گران‌تر یا برابر)، «none» (بررسی شد، قیمت معتبری نبود)، «unchecked».</param>
/// <param name="GapPercent">فاصلهٔ قیمت ما با مرجع به درصد (مثبت = گران‌تریم). مثال: ۵٫۲.</param>
public sealed record ProductCompetitorRow(
    Guid ProductId, string Name, decimal OurPrice, bool IsActive, int Stock,
    decimal? TorobLowest, int TorobSellers, decimal? Lowest, int Accepted, int Offers,
    DateTimeOffset? CheckedAt, string State, decimal? GapPercent);

/// <summary>یک صفحه از «رصد قیمت رقبا» به‌همراه شمارنده‌ها.</summary>
public sealed record ProductCompetitorPage(
    IReadOnlyList<ProductCompetitorRow> Rows, int Total, int Page, int Pages,
    int Checked, int Above, int Cheapest, int Unchecked);

/// <summary>
/// ذخیره و خواندن «آخرین قیمت رقبای هر محصول».
/// </summary>
public interface IProductCompetitorService
{
    /// <summary>
    /// نتیجهٔ یک جست‌وجو را برای محصول ذخیره می‌کند (جایگزین بررسی قبلی). بهترین تلاش است: خطای پایگاه داده به بیرون نمی‌رسد.
    /// <para>مثال: بعد از «پیشنهاد قیمت فروش» برای «هدفون هایلو T15» ← کارت محصول: ترب ۲۸۵ هزار از ۱۲ فروشنده.</para>
    /// </summary>
    Task SaveAsync(Guid productId, string query, CompetitorLookup lookup, decimal? ourPrice, IReadOnlyCollection<string>? ownShops, CancellationToken cancellationToken = default);

    /// <summary>آخرین بررسی یک محصول با داوری دوباره و مقایسه با قیمت فعلی؛ null یعنی هنوز بررسی نشده.</summary>
    Task<ProductCompetitorDetail?> GetAsync(Guid productId, CancellationToken cancellationToken = default);

    /// <summary>
    /// فهرست محصولات با خلاصهٔ قیمت رقبا. filter: «above» (گران‌تر از ترب/رقبا)، «cheapest»، «unchecked»، «stale» (بیش از ۷ روز)، هر چیز دیگر = همه.
    /// </summary>
    Task<ProductCompetitorPage> ListAsync(string? filter, string? search, int page, CancellationToken cancellationToken = default);

    /// <summary>
    /// بررسی دوبارهٔ قیمت رقبای یک محصول همین الان (با نام محصول) و ذخیره. از کش ۶ ساعته استفاده می‌کند تا منابع محدودمان نکنند.
    /// </summary>
    Task<OperationResult> RefreshAsync(Guid productId, IReadOnlyCollection<string>? ownShops, CancellationToken cancellationToken = default);
}

/// <summary>پیاده‌سازی <see cref="IProductCompetitorService"/>.</summary>
public sealed class ProductCompetitorService(
    FratakShopDbContext context,
    IServiceScopeFactory scopeFactory,
    ICompetitorPriceService competitors,
    IStoreSettingService settings) : IProductCompetitorService
{
    private static readonly JsonSerializerOptions Json = new(JsonSerializerDefaults.Web);

    /// <summary>هر صفحهٔ «رصد قیمت رقبا» چند ردیف دارد.</summary>
    public const int PageSize = 30;

    /// <summary>بعد از این مدت بررسی «کهنه» حساب می‌شود.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromDays(7);

    /// <summary>
    /// نام‌هایی که «فروشگاه خودمان» حساب می‌شوند: نام فروشگاه در تنظیمات + نام‌ها/دامنه‌های اضافه (مثلاً دامنهٔ اصلی سایت).
    /// مثال: («فراتک»، ["shop.fratak.ir"]) ← ["فراتک", "shop.fratak.ir"].
    /// </summary>
    public static IReadOnlyCollection<string> OwnShops(StoreSettingDto store, params string?[] extra)
        => new[] { store.SiteName }.Concat(extra).Where(x => !string.IsNullOrWhiteSpace(x)).Select(x => x!.Trim()).Distinct().ToList();

    public async Task SaveAsync(Guid productId, string query, CompetitorLookup lookup, decimal? ourPrice, IReadOnlyCollection<string>? ownShops, CancellationToken cancellationToken = default)
    {
        if (productId == Guid.Empty || string.IsNullOrWhiteSpace(query))
            return;
        // بررسی ناموفق (هیچ قیمتی و منابع محدود) بررسی خوب قبلی را پاک نمی‌کند.
        if (lookup.Offers.Count == 0 && lookup.Limited)
            return;

        var reviewed = PriceAdvisor.Review(lookup.Offers, new OfferReviewOptions(query, OwnShops: ownShops));
        var accepted = reviewed.Where(r => r.Accepted).Select(r => r.Offer).OrderBy(o => o.Price).ToList();
        var torob = PriceAdvisor.TorobSellers(reviewed);
        try
        {
            using var scope = scopeFactory.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var row = await db.ProductCompetitorSnapshots.AsTracking().FirstOrDefaultAsync(s => s.ProductId == productId, cancellationToken);
            if (row is null)
            {
                row = new ProductCompetitorSnapshot { ProductId = productId };
                db.ProductCompetitorSnapshots.Add(row);
            }
            row.Query = Clip(query.Trim(), 200)!;
            row.MatchedTitle = Clip(lookup.MatchedTitle, 200);
            row.OffersJson = JsonSerializer.Serialize(lookup.Offers.Take(80).ToList(), Json);
            row.OfferCount = lookup.Offers.Count;
            row.AcceptedCount = accepted.Count;
            row.LowestPrice = accepted.Count > 0 ? accepted[0].Price : null;
            row.MedianPrice = accepted.Count > 0 ? PriceAdvisor.Median(accepted.Select(o => o.Price).ToList()) : null;
            row.TorobLowestPrice = torob.Count > 0 ? torob[0].Price : null;
            row.TorobSellerCount = torob.Count;
            row.OurPrice = ourPrice;
            row.Problems = lookup.Problems.Count > 0 ? Clip(string.Join(" | ", lookup.Problems), 1000) : null;
            row.CheckedAt = lookup.CachedAt ?? DateTimeOffset.UtcNow;
            await db.SaveChangesAsync(cancellationToken);
        }
        catch (Exception exception) when (exception is DbUpdateException or InvalidOperationException)
        {
            // دو ذخیرهٔ هم‌زمان برای یک محصول (ایندکس یکتا) یا قطعی لحظه‌ای: بررسی بعدی ذخیره می‌شود.
        }
    }

    public async Task<ProductCompetitorDetail?> GetAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var row = await context.ProductCompetitorSnapshots.AsNoTracking()
            .Where(s => s.ProductId == productId)
            .Select(s => new { s.Query, s.MatchedTitle, s.CheckedAt, s.Problems, s.OffersJson })
            .FirstOrDefaultAsync(cancellationToken);
        if (row is null)
            return null;

        var now = DateTimeOffset.UtcNow;
        var product = await context.Products.AsNoTracking().Where(p => p.Id == productId)
            .Select(p => new { p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt })
            .FirstOrDefaultAsync(cancellationToken);
        var ourPrice = product is null ? 0 : Effective(product.Price, product.DiscountPrice, product.DiscountStartAt, product.DiscountEndAt, now);

        List<CompetitorOffer> offers;
        try
        {
            offers = JsonSerializer.Deserialize<List<CompetitorOffer>>(row.OffersJson, Json) ?? [];
        }
        catch (JsonException)
        {
            offers = [];
        }

        var store = await settings.GetAsync(cancellationToken);
        var reviewed = PriceAdvisor.Review(offers, new OfferReviewOptions(row.Query, OwnShops: OwnShops(store)));
        var accepted = reviewed.Where(r => r.Accepted).Select(r => r.Offer).OrderBy(o => o.Price).ToList();
        var torob = PriceAdvisor.TorobSellers(reviewed);
        int? rank = torob.Count > 0 && ourPrice > 0 ? 1 + torob.Count(o => o.Price <= ourPrice) : null;
        var elsewhere = ourPrice > 0 ? accepted.Count(o => o.Source != CompetitorPriceService.Torob && o.Price <= ourPrice) : 0;

        return new ProductCompetitorDetail(row.Query, row.MatchedTitle, row.CheckedAt, row.Problems, reviewed,
            accepted.Count > 0 ? accepted[0].Price : null,
            accepted.Count > 0 ? PriceAdvisor.Median(accepted.Select(o => o.Price).ToList()) : null,
            torob.Count > 0 ? torob[0].Price : null, torob.Count, ourPrice, rank, elsewhere);
    }

    public async Task<ProductCompetitorPage> ListAsync(string? filter, string? search, int page, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var staleBefore = now - StaleAfter;
        var rows =
            from p in context.Products.AsNoTracking()
            join s in context.ProductCompetitorSnapshots.AsNoTracking() on p.Id equals s.ProductId into snaps
            from s in snaps.DefaultIfEmpty()
            select new
            {
                p.Id,
                p.Name,
                p.IsActive,
                p.StockQuantity,
                // قیمت مؤثر ما: تخفیف فعال (در بازهٔ زمانی و کمتر از قیمت) یا قیمت اصلی.
                Our = p.DiscountPrice != null && p.DiscountPrice < p.Price
                      && (p.DiscountStartAt == null || p.DiscountStartAt <= now) && (p.DiscountEndAt == null || p.DiscountEndAt > now)
                    ? p.DiscountPrice.Value
                    : p.Price,
                Snap = s,
                // مرجع: ارزان‌ترین فروشندهٔ ترب، اگر نبود ارزان‌ترین رقیب معتبر.
                Reference = s == null ? null : s.TorobLowestPrice ?? s.LowestPrice,
            };

        var term = search?.Trim();
        if (!string.IsNullOrEmpty(term))
        {
            if (term.Length > 100)
                term = term[..100];
            rows = rows.Where(r => r.Name.Contains(term));
        }

        // شمارنده‌ها (چهار شمارش سادهٔ SQL؛ روی همان جست‌وجو، پیش از فیلتر وضعیت).
        var checkedCount = await rows.CountAsync(r => r.Snap != null, cancellationToken);
        var aboveCount = await rows.CountAsync(r => r.Reference != null && r.Our >= r.Reference, cancellationToken);
        var cheapestCount = await rows.CountAsync(r => r.Reference != null && r.Our < r.Reference, cancellationToken);
        var uncheckedCount = await rows.CountAsync(r => r.Snap == null, cancellationToken);

        rows = filter switch
        {
            "above" => rows.Where(r => r.Reference != null && r.Our >= r.Reference),
            "cheapest" => rows.Where(r => r.Reference != null && r.Our < r.Reference),
            "unchecked" => rows.Where(r => r.Snap == null),
            "stale" => rows.Where(r => r.Snap != null && r.Snap.CheckedAt < staleBefore),
            _ => rows,
        };

        var total = await rows.CountAsync(cancellationToken);
        var pages = Math.Max(1, (int)Math.Ceiling(total / (double)PageSize));
        page = Math.Clamp(page, 1, pages);
        var ordered = filter == "above"
            ? rows.OrderByDescending(r => (r.Our - r.Reference!.Value) / r.Reference.Value)
            : rows.OrderBy(r => r.Snap == null).ThenByDescending(r => r.Snap!.CheckedAt).ThenBy(r => r.Name);
        var list = await ordered
            .Skip((page - 1) * PageSize).Take(PageSize)
            .Select(r => new
            {
                r.Id, r.Name, r.Our, r.IsActive, r.StockQuantity, r.Reference,
                TorobLowest = r.Snap == null ? null : r.Snap.TorobLowestPrice,
                TorobSellers = r.Snap == null ? 0 : r.Snap.TorobSellerCount,
                Lowest = r.Snap == null ? null : r.Snap.LowestPrice,
                Accepted = r.Snap == null ? 0 : r.Snap.AcceptedCount,
                Offers = r.Snap == null ? 0 : r.Snap.OfferCount,
                CheckedAt = r.Snap == null ? (DateTimeOffset?)null : r.Snap.CheckedAt,
            })
            .ToListAsync(cancellationToken);

        var result = list.Select(r =>
        {
            var state = r.CheckedAt is null ? "unchecked" : r.Reference is null ? "none" : r.Our < r.Reference ? "cheapest" : "above";
            decimal? gap = r.Reference is > 0 ? Math.Round((r.Our - r.Reference.Value) * 100 / r.Reference.Value, 1) : null;
            return new ProductCompetitorRow(r.Id, r.Name, r.Our, r.IsActive, r.StockQuantity, r.TorobLowest, r.TorobSellers, r.Lowest,
                r.Accepted, r.Offers, r.CheckedAt, state, gap);
        }).ToList();

        return new ProductCompetitorPage(result, total, page, pages, checkedCount, aboveCount, cheapestCount, uncheckedCount);
    }

    public async Task<OperationResult> RefreshAsync(Guid productId, IReadOnlyCollection<string>? ownShops, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var product = await context.Products.AsNoTracking().Where(p => p.Id == productId)
            .Select(p => new { p.Name, p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (product is null)
            return OperationResult.NotFound("محصول پیدا نشد.");

        var store = await settings.GetAsync(cancellationToken);
        if (!store.IsCompetitorPriceLookupEnabled)
            return OperationResult.Error("جست‌وجوی قیمت رقبا در تنظیمات («پیشنهاد قیمت فروش») خاموش است.");

        // اگر قبلاً عبارت دقیق‌تری جست‌وجو شده (مثلاً از دیالوگ پیشنهاد قیمت)، همان را به کار می‌بریم.
        var query = await context.ProductCompetitorSnapshots.AsNoTracking().Where(s => s.ProductId == productId).Select(s => s.Query).FirstOrDefaultAsync(cancellationToken)
                    ?? product.Name;
        var lookup = await competitors.SearchAsync(query, cancellationToken);
        var our = Effective(product.Price, product.DiscountPrice, product.DiscountStartAt, product.DiscountEndAt, now);
        await SaveAsync(productId, query, lookup, our, ownShops ?? OwnShops(store), cancellationToken);

        if (lookup.Offers.Count == 0)
            return OperationResult.Error("قیمتی از رقبا پیدا نشد. " + string.Join(" | ", lookup.Problems.Take(3)));
        var accepted = PriceAdvisor.Review(lookup.Offers, new OfferReviewOptions(query, OwnShops: ownShops ?? OwnShops(store))).Count(r => r.Accepted);
        return OperationResult.Success($"قیمت رقبای «{product.Name}» به‌روز شد: {lookup.Offers.Count} قیمت، {accepted} معتبر.");
    }

    /// <summary>قیمت مؤثر: تخفیف فعال یا قیمت اصلی. مثال: (۳۰۰، ۲۷۰، دیروز، فردا) ← ۲۷۰.</summary>
    public static decimal Effective(decimal price, decimal? discount, DateTimeOffset? start, DateTimeOffset? end, DateTimeOffset now)
        => discount is { } d && d > 0 && d < price && (start is null || start <= now) && (end is null || end > now) ? d : price;

    private static string? Clip(string? text, int max)
        => string.IsNullOrWhiteSpace(text) ? null : text.Length <= max ? text.Trim() : text[..max].Trim();
}
