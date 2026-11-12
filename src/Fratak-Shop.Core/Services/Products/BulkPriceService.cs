using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Products;

// ═══════════════════════════════════════════════════════════════════════════
//  تغییر گروهی قیمت (مرحلهٔ هفتم – فاز ۵)
//  روند کار در صفحهٔ /admin/products/bulk-price:
//   ۱) کارمند محدوده (محصولات انتخاب‌شده یا فیلتر دسته/برند/…) و عملیات را انتخاب می‌کند.
//   ۲) «پیش‌نمایش»: برای هر محصول قیمت قبلی → جدید، اختلاف، سود و هشدارها نشان داده می‌شود (هنوز چیزی ذخیره نشده).
//   ۳) «اعمال»: دوباره از روی دادهٔ همین لحظه محاسبه می‌شود؛ اگر با پیش‌نمایش فرق کند، اعمال نمی‌شود و
//      پیش‌نمایش تازه نشان داده می‌شود (تا چیزی که کارمند ندیده ذخیره نشود).
//   ۴) هر اعمال یک «دسته» در تاریخچه است و با یک کلیک قابل برگرداندن است.
//  محاسبه‌ها در BulkPriceRules (کلاس ایستا و بدون دیتابیس) است تا تست واحد ساده باشد.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>کدام قیمت عوض شود.</summary>
public enum BulkPriceTarget
{
    /// <summary>قیمت فروش اصلی.</summary>
    Price = 0,

    /// <summary>قیمت حراج/تخفیف‌دار (با بازهٔ زمانی اختیاری – حراج زمان‌دار).</summary>
    DiscountPrice = 1,

    /// <summary>قیمت خرید (فقط داخلی؛ برای محاسبهٔ سود).</summary>
    PurchasePrice = 2,

    /// <summary>حذف تخفیف و بازهٔ زمانی آن.</summary>
    RemoveDiscount = 3,
}

/// <summary>نوع تغییر.</summary>
public enum BulkPriceMode
{
    /// <summary>درصدی؛ مثبت = افزایش. مثال: ۱۰ یعنی ۱۰٪ گران‌تر، ‎-5 یعنی ۵٪ ارزان‌تر.</summary>
    Percent = 0,

    /// <summary>مبلغ ثابت (تومان)؛ مثال: 20000 یعنی ۲۰ هزار تومان گران‌تر.</summary>
    Amount = 1,

    /// <summary>قرار دادن یک مبلغ مشخص برای همه.</summary>
    Set = 2,

    /// <summary>فقط برای قیمت فروش: قیمت خرید × (۱ + درصد سود). مثال: خرید ۴۰۰ هزار و سود ۲۵٪ → ۵۰۰ هزار.</summary>
    MarginOverPurchase = 3,

    /// <summary>فقط برای حراج: درصد تخفیف از قیمت فروش. مثال: قیمت ۵۰۰ هزار و ۱۰٪ → حراج ۴۵۰ هزار.</summary>
    PercentOff = 4,
}

/// <summary>جهت گرد کردن.</summary>
public enum BulkPriceRoundDirection
{
    Nearest = 0,
    Up = 1,
    Down = 2,
}

/// <summary>
/// تعریف کامل یک عملیات. <paramref name="Value"/> بسته به حالت درصد یا مبلغ است.
/// <paramref name="RoundStep"/>: ۰ = بدون گرد کردن، یا ۱۰۰ / ۱۰۰۰ / ۱۰۰۰۰ تومان.
/// </summary>
public sealed record BulkPriceOperation(
    BulkPriceTarget Target,
    BulkPriceMode Mode,
    decimal Value,
    int RoundStep = 1000,
    BulkPriceRoundDirection RoundDirection = BulkPriceRoundDirection.Nearest,
    bool ScaleDiscount = true,
    bool SkipLoss = true,
    DateTimeOffset? DiscountStartAt = null,
    DateTimeOffset? DiscountEndAt = null);

/// <summary>محدودهٔ محصولات: یا شناسه‌های انتخاب‌شده، یا فیلتر.</summary>
public sealed record BulkPriceScope(
    IReadOnlyList<Guid>? ProductIds = null,
    Guid? CategoryId = null,
    Guid? BrandId = null,
    Guid? SupplierId = null,
    string? Search = null,
    string? Stock = null,
    string? State = null);

/// <summary>قیمت‌های فعلی یک محصول (ورودی محاسبه).</summary>
public sealed record PriceSnapshot(
    decimal Price,
    decimal? DiscountPrice,
    DateTimeOffset? DiscountStartAt,
    DateTimeOffset? DiscountEndAt,
    decimal? PurchasePrice);

/// <summary>نتیجهٔ محاسبه برای یک محصول.</summary>
public sealed record PriceComputation(PriceSnapshot New, string? SkipReason, IReadOnlyList<string> Warnings)
{
    public bool IsSkipped => SkipReason is not null;
}

/// <summary>یک ردیف پیش‌نمایش برای نمایش در جدول.</summary>
public sealed record BulkPricePreviewRow(
    Guid ProductId,
    string Name,
    string Sku,
    string? ImageName,
    int Stock,
    PriceSnapshot Old,
    PriceSnapshot New,
    decimal OldFinal,
    decimal NewFinal,
    decimal? NewMarginPercent,
    string? SkipReason,
    IReadOnlyList<string> Warnings)
{
    public bool IsSkipped => SkipReason is not null;
}

/// <summary>
/// پیش‌نمایش کامل. <see cref="Token"/> اثر انگشت تغییرهاست؛ هنگام اعمال باید همین باشد.
/// </summary>
public sealed record BulkPricePreview(
    IReadOnlyList<BulkPricePreviewRow> Rows,
    int Matched,
    int Changed,
    int Skipped,
    int WithWarnings,
    bool Truncated,
    string Token,
    string Title);

/// <summary>یک دسته در تاریخچه.</summary>
public sealed record PriceBatchRow(
    Guid Id,
    string Title,
    PriceChangeSource Source,
    int AffectedCount,
    string? CreatedByName,
    DateTimeOffset CreatedAt,
    DateTimeOffset? RevertedAt,
    string? RevertedByName,
    string? RevertNote);

/// <summary>
/// قانون‌های محاسبهٔ قیمت (بدون دیتابیس). همهٔ مبالغ به تومان‌اند.
/// </summary>
public static class BulkPriceRules
{
    /// <summary>بیشترین تعداد محصول در یک عملیات (برای سرعت و جلوگیری از اشتباه بزرگ).</summary>
    public const int MaxProducts = 2000;

    /// <summary>مقدارهای مجاز گام گرد کردن.</summary>
    public static readonly int[] RoundSteps = [0, 100, 1000, 10000];

    /// <summary>
    /// بررسی معتبر بودن عملیات؛ پیام خطا یا null.
    /// مثال: درصد ‎-95 → «کاهش بیش از ۹۰٪ مجاز نیست».
    /// </summary>
    public static string? Validate(BulkPriceOperation op)
    {
        if (!Enum.IsDefined(op.Target) || !Enum.IsDefined(op.Mode) || !Enum.IsDefined(op.RoundDirection))
            return "عملیات نامعتبر است.";
        if (!RoundSteps.Contains(op.RoundStep))
            return "گام گرد کردن نامعتبر است.";
        if (op.Target == BulkPriceTarget.RemoveDiscount)
            return null;

        var allowed = op.Target switch
        {
            BulkPriceTarget.Price => new[] { BulkPriceMode.Percent, BulkPriceMode.Amount, BulkPriceMode.Set, BulkPriceMode.MarginOverPurchase },
            BulkPriceTarget.DiscountPrice => new[] { BulkPriceMode.PercentOff, BulkPriceMode.Amount, BulkPriceMode.Set },
            _ => new[] { BulkPriceMode.Percent, BulkPriceMode.Amount, BulkPriceMode.Set },
        };
        if (!allowed.Contains(op.Mode))
            return "این نوع تغییر برای قیمت انتخاب‌شده قابل استفاده نیست.";

        switch (op.Mode)
        {
            case BulkPriceMode.Percent when op.Value == 0 || op.Value < -90 || op.Value > 500:
                return "درصد تغییر باید غیر صفر و بین ‎-90 تا 500 باشد.";
            case BulkPriceMode.Amount when op.Target == BulkPriceTarget.DiscountPrice && (op.Value <= 0 || op.Value > 1_000_000_000):
                return "مبلغ تخفیف باید بزرگ‌تر از صفر باشد.";
            case BulkPriceMode.Amount when op.Value == 0 || Math.Abs(op.Value) > 1_000_000_000:
                return "مبلغ تغییر باید غیر صفر باشد.";
            case BulkPriceMode.Set when op.Value <= 0 || op.Value > 100_000_000_000:
                return "مبلغ باید بزرگ‌تر از صفر باشد.";
            case BulkPriceMode.MarginOverPurchase when op.Value < 0 || op.Value > 1000:
                return "درصد سود باید بین ۰ تا ۱۰۰۰ باشد.";
            case BulkPriceMode.PercentOff when op.Value <= 0 || op.Value >= 90:
                return "درصد تخفیف باید بیشتر از ۰ و کمتر از ۹۰ باشد.";
        }

        if (op.Target == BulkPriceTarget.DiscountPrice && op.DiscountStartAt is { } start && op.DiscountEndAt is { } end && end <= start)
            return "پایان حراج باید بعد از شروع آن باشد.";
        return null;
    }

    /// <summary>
    /// گرد کردن مبلغ. مثال: 412_300 با گام ۱۰۰۰ → نزدیک‌ترین 412_000، رو به بالا 413_000.
    /// گام صفر = فقط حذف اعشار.
    /// </summary>
    public static decimal Round(decimal value, int step, BulkPriceRoundDirection direction)
    {
        if (value <= 0)
            return 0;
        if (step <= 0)
            return decimal.Round(value, 0, MidpointRounding.AwayFromZero);

        var units = value / step;
        var rounded = direction switch
        {
            BulkPriceRoundDirection.Up => decimal.Ceiling(units),
            BulkPriceRoundDirection.Down => decimal.Floor(units),
            _ => decimal.Round(units, 0, MidpointRounding.AwayFromZero),
        };
        // گرد کردن رو به پایین نباید مبلغ مثبت را صفر کند.
        if (rounded <= 0)
            rounded = 1;
        return rounded * step;
    }

    /// <summary>قیمتی که مشتری الان می‌پردازد (حراج داخل بازه، وگرنه قیمت اصلی).</summary>
    public static decimal Final(PriceSnapshot p, DateTimeOffset now) =>
        FratakShopDbContext.FinalPrice(p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt, now);

    /// <summary>
    /// قیمتی که سود با آن سنجیده می‌شود: اگر حراج آینده/جاری دارد، کمترین قیمت ممکن (قیمت حراج)، وگرنه قیمت اصلی.
    /// چرا؟ حراج زمان‌دار ممکن است هنوز شروع نشده باشد ولی وقتی شروع شود نباید زیان بدهد.
    /// </summary>
    public static decimal LowestSellingPrice(PriceSnapshot p, DateTimeOffset now)
    {
        var discountStillUsable = p.DiscountPrice is { } d && d < p.Price && (p.DiscountEndAt is null || p.DiscountEndAt >= now);
        return discountStillUsable ? p.DiscountPrice!.Value : p.Price;
    }

    /// <summary>درصد سود نسبت به قیمت خرید؛ مثال: خرید ۴۰۰ و فروش ۵۰۰ → ۲۵٪. بدون قیمت خرید → null.</summary>
    public static decimal? MarginPercent(decimal sellingPrice, decimal? purchasePrice) =>
        purchasePrice is > 0 ? decimal.Round((sellingPrice - purchasePrice.Value) / purchasePrice.Value * 100, 1) : null;

    /// <summary>
    /// محاسبهٔ قیمت‌های تازهٔ یک محصول. هیچ چیزی ذخیره نمی‌کند.
    /// <para>دلیل‌های رد شدن (SkipReason): بدون تغییر، قیمت خرید ندارد (حالت سود)، حراج نامعتبر، زیان (اگر SkipLoss).</para>
    /// </summary>
    public static PriceComputation Compute(PriceSnapshot old, BulkPriceOperation op, DateTimeOffset now)
    {
        var warnings = new List<string>();
        var price = old.Price;
        var discount = old.DiscountPrice;
        var start = old.DiscountStartAt;
        var end = old.DiscountEndAt;
        var purchase = old.PurchasePrice;

        switch (op.Target)
        {
            case BulkPriceTarget.Price:
            {
                decimal? raw = op.Mode switch
                {
                    BulkPriceMode.Percent => old.Price * (1 + op.Value / 100m),
                    BulkPriceMode.Amount => old.Price + op.Value,
                    BulkPriceMode.Set => op.Value,
                    BulkPriceMode.MarginOverPurchase => old.PurchasePrice is > 0 ? old.PurchasePrice.Value * (1 + op.Value / 100m) : null,
                    _ => null,
                };
                if (raw is null)
                    return new PriceComputation(old, "قیمت خرید ثبت نشده؛ سود قابل محاسبه نیست.", warnings);
                if (raw <= 0)
                    return new PriceComputation(old, "قیمت حاصل صفر یا منفی می‌شود.", warnings);

                price = Round(raw.Value, op.RoundStep, op.RoundDirection);

                // حراج فعلی: یا به همان نسبت تغییر می‌کند یا اگر دیگر کمتر از قیمت نباشد هشدار می‌دهیم.
                if (old.DiscountPrice is { } oldDiscount && old.Price > 0)
                {
                    if (op.ScaleDiscount)
                        discount = Round(oldDiscount * (price / old.Price), op.RoundStep, op.RoundDirection);
                    if (discount >= price)
                    {
                        discount = null;
                        start = null;
                        end = null;
                        warnings.Add("قیمت حراج از قیمت تازه کمتر نبود و حذف شد.");
                    }
                }
                break;
            }

            case BulkPriceTarget.DiscountPrice:
            {
                var raw = op.Mode switch
                {
                    BulkPriceMode.PercentOff => old.Price * (1 - op.Value / 100m),
                    BulkPriceMode.Amount => old.Price - op.Value,
                    _ => op.Value,
                };
                var rounded = Round(raw, op.RoundStep, op.RoundDirection);
                if (rounded <= 0 || rounded >= old.Price)
                    return new PriceComputation(old, "قیمت حراج باید کمتر از قیمت فروش و بیشتر از صفر باشد.", warnings);
                discount = rounded;
                start = op.DiscountStartAt;
                end = op.DiscountEndAt;
                break;
            }

            case BulkPriceTarget.PurchasePrice:
            {
                if (op.Mode != BulkPriceMode.Set && old.PurchasePrice is null)
                    return new PriceComputation(old, "قیمت خرید ثبت نشده است.", warnings);
                var raw = op.Mode switch
                {
                    BulkPriceMode.Percent => old.PurchasePrice!.Value * (1 + op.Value / 100m),
                    BulkPriceMode.Amount => old.PurchasePrice!.Value + op.Value,
                    _ => op.Value,
                };
                if (raw <= 0)
                    return new PriceComputation(old, "قیمت خرید حاصل صفر یا منفی می‌شود.", warnings);
                purchase = Round(raw, op.RoundStep, op.RoundDirection);
                break;
            }

            case BulkPriceTarget.RemoveDiscount:
                if (old.DiscountPrice is null && old.DiscountStartAt is null && old.DiscountEndAt is null)
                    return new PriceComputation(old, "تخفیفی ندارد.", warnings);
                discount = null;
                start = null;
                end = null;
                break;
        }

        var result = new PriceSnapshot(price, discount, start, end, purchase);
        if (result == old)
            return new PriceComputation(old, "بدون تغییر.", warnings);

        // دور ۲۹ (#۱۳) – «همیشه سود»: وقتی قیمت فروش یا تخفیف عوض می‌شود، کمترین قیمت فروش باید «بیشتر» از قیمت خرید بماند.
        // زیان (کمتر) و سر‌به‌سر (مساوی) هر دو «همیشه» رد می‌شوند – تیک SkipLoss دیگر اجازهٔ عبور نمی‌دهد.
        // مثال: خرید ۳۰۰ هزار، تخفیف ۵۰٪ روی قیمت ۶۰۰ هزار ← ۳۰۰ هزار = سر‌به‌سر ← رد با پیام «زیان یا بدون سود».
        // استثنا: تغییر خود «قیمت خرید» (واقعیت خرید است، نباید جلوی ثبتش را گرفت) ← فقط هشدار، یا رد اگر SkipLoss.
        if (result.PurchasePrice is > 0 && LowestSellingPrice(result, now) <= result.PurchasePrice)
        {
            var lowest = LowestSellingPrice(result, now);
            var text = lowest < result.PurchasePrice
                ? $"زیان: قیمت فروش ({lowest:N0}) کمتر از قیمت خرید ({result.PurchasePrice:N0}) می‌شود."
                : $"زیان یا بدون سود: قیمت فروش ({lowest:N0}) برابر قیمت خرید ({result.PurchasePrice:N0}) می‌شود.";
            if (op.Target != BulkPriceTarget.PurchasePrice || op.SkipLoss)
                return new PriceComputation(old, text, warnings);
            warnings.Add(text);
        }

        var change = old.Price > 0 ? Math.Abs(result.Price - old.Price) / old.Price : 0;
        if (change >= 0.5m)
            warnings.Add("تغییر قیمت فروش بیش از ۵۰٪ است؛ دوباره بررسی کنید.");

        return new PriceComputation(result, null, warnings);
    }

    /// <summary>
    /// اثر انگشت فهرست تغییرها (شناسه + مقدارهای تازه). اگر بین پیش‌نمایش و اعمال چیزی عوض شود، این هم عوض می‌شود.
    /// </summary>
    public static string Fingerprint(IEnumerable<(Guid ProductId, PriceSnapshot New)> changes)
    {
        var builder = new StringBuilder();
        foreach (var (id, p) in changes.OrderBy(c => c.ProductId))
        {
            builder.Append(id.ToString("N")).Append('|')
                .Append(p.Price.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(p.DiscountPrice?.ToString(CultureInfo.InvariantCulture)).Append('|')
                .Append(p.DiscountStartAt?.UtcTicks).Append('|')
                .Append(p.DiscountEndAt?.UtcTicks).Append('|')
                .Append(p.PurchasePrice?.ToString(CultureInfo.InvariantCulture)).Append(';');
        }
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString())))[..24];
    }

    /// <summary>
    /// عنوان خوانا برای تاریخچه. مثال: «افزایش ۱۰٪ قیمت فروش (گرد به ۱٬۰۰۰ تومان)».
    /// </summary>
    public static string Describe(BulkPriceOperation op)
    {
        var value = op.Value.ToString("#,0.##", CultureInfo.InvariantCulture);
        var abs = Math.Abs(op.Value).ToString("#,0.##", CultureInfo.InvariantCulture);
        var text = (op.Target, op.Mode) switch
        {
            (BulkPriceTarget.RemoveDiscount, _) => "حذف تخفیف/حراج",
            (BulkPriceTarget.Price, BulkPriceMode.Percent) => $"{(op.Value > 0 ? "افزایش" : "کاهش")} {abs}٪ قیمت فروش",
            (BulkPriceTarget.Price, BulkPriceMode.Amount) => $"{(op.Value > 0 ? "افزایش" : "کاهش")} {abs} تومان قیمت فروش",
            (BulkPriceTarget.Price, BulkPriceMode.Set) => $"قیمت فروش = {value} تومان",
            (BulkPriceTarget.Price, BulkPriceMode.MarginOverPurchase) => $"قیمت فروش = قیمت خرید + {value}٪ سود",
            (BulkPriceTarget.DiscountPrice, BulkPriceMode.PercentOff) => $"حراج {value}٪",
            (BulkPriceTarget.DiscountPrice, BulkPriceMode.Amount) => $"حراج {value} تومان کمتر",
            (BulkPriceTarget.DiscountPrice, _) => $"قیمت حراج = {value} تومان",
            (BulkPriceTarget.PurchasePrice, BulkPriceMode.Percent) => $"{(op.Value > 0 ? "افزایش" : "کاهش")} {abs}٪ قیمت خرید",
            (BulkPriceTarget.PurchasePrice, BulkPriceMode.Amount) => $"{(op.Value > 0 ? "افزایش" : "کاهش")} {abs} تومان قیمت خرید",
            (BulkPriceTarget.PurchasePrice, _) => $"قیمت خرید = {value} تومان",
            _ => "تغییر قیمت",
        };
        if (op.Target == BulkPriceTarget.DiscountPrice && (op.DiscountStartAt is not null || op.DiscountEndAt is not null))
            text += " (زمان‌دار)";
        if (op.Target != BulkPriceTarget.RemoveDiscount && op.RoundStep > 0)
            text += $" – گرد به {op.RoundStep:N0} تومان";
        return text;
    }
}

/// <summary>سرویس تغییر گروهی قیمت (پیش‌نمایش، اعمال، تاریخچه، برگرداندن).</summary>
public interface IBulkPriceService
{
    /// <summary>پیش‌نمایش بدون ذخیره؛ اگر عملیات نامعتبر باشد خطا برمی‌گردد.</summary>
    Task<OperationResult<BulkPricePreview>> PreviewAsync(BulkPriceScope scope, BulkPriceOperation operation, CancellationToken cancellationToken = default);

    /// <summary>
    /// اعمال؛ <paramref name="expectedToken"/> باید همان توکن پیش‌نمایش باشد. اگر داده عوض شده باشد
    /// خطا با کد «stale» برمی‌گردد و چیزی ذخیره نمی‌شود.
    /// </summary>
    Task<OperationResult<Guid>> ApplyAsync(BulkPriceScope scope, BulkPriceOperation operation, string expectedToken, Guid? userId, string? userName);

    /// <summary>آخرین دسته‌ها (جدیدترین اول).</summary>
    Task<List<PriceBatchRow>> ListBatchesAsync(int take = 30);

    /// <summary>
    /// برگرداندن کل یک دسته؛ فقط فیلدهایی که از آن زمان دست نخورده‌اند برمی‌گردند (کار بعدی دیگران پاک نمی‌شود).
    /// </summary>
    Task<OperationResult> RevertAsync(Guid batchId, Guid? userId, string? userName);

    /// <summary>
    /// ثبت یک دسته از تغییرهای آماده (برای ورود از اکسل): محصول‌ها باید ردیابی‌شده و تغییر داده شده باشند؛
    /// این متد فقط ردیف‌های تاریخچه را اضافه می‌کند و SaveChanges را صدا نمی‌زند.
    /// </summary>
    PriceChangeBatch? Track(string title, PriceChangeSource source, IEnumerable<(Product Product, PriceSnapshot Old)> changed, Guid? userId, string? userName);
}

public sealed class BulkPriceService(FratakShopDbContext context) : IBulkPriceService
{
    public async Task<OperationResult<BulkPricePreview>> PreviewAsync(BulkPriceScope scope, BulkPriceOperation operation, CancellationToken cancellationToken = default)
    {
        if (BulkPriceRules.Validate(operation) is { } error)
            return OperationResult<BulkPricePreview>.Error(error);

        var now = DateTimeOffset.UtcNow;
        var query = Filter(scope);
        var matched = await query.CountAsync(cancellationToken);
        if (matched == 0)
            return OperationResult<BulkPricePreview>.Error("هیچ محصولی با این محدوده پیدا نشد.");

        var products = await query.OrderBy(p => p.Name).Take(BulkPriceRules.MaxProducts)
            .Select(p => new
            {
                p.Id, p.Name, p.SKU, p.StockQuantity, p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt, p.PurchasePrice,
                Image = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        var rows = new List<BulkPricePreviewRow>(products.Count);
        foreach (var p in products)
        {
            var old = new PriceSnapshot(p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt, p.PurchasePrice);
            var result = BulkPriceRules.Compute(old, operation, now);
            var selling = BulkPriceRules.LowestSellingPrice(result.New, now);
            rows.Add(new BulkPricePreviewRow(p.Id, p.Name, p.SKU, p.Image, p.StockQuantity, old, result.New,
                BulkPriceRules.Final(old, now), BulkPriceRules.Final(result.New, now),
                BulkPriceRules.MarginPercent(selling, result.New.PurchasePrice), result.SkipReason, result.Warnings));
        }

        var token = BulkPriceRules.Fingerprint(rows.Where(r => !r.IsSkipped).Select(r => (r.ProductId, r.New)));
        // ردیف‌های قابل اعمال و دارای هشدار اول نمایش داده می‌شوند.
        var ordered = rows.OrderBy(r => r.IsSkipped).ThenByDescending(r => r.Warnings.Count).ThenBy(r => r.Name).ToList();
        var preview = new BulkPricePreview(ordered, matched, rows.Count(r => !r.IsSkipped), rows.Count(r => r.IsSkipped),
            rows.Count(r => !r.IsSkipped && r.Warnings.Count > 0), matched > BulkPriceRules.MaxProducts, token, BulkPriceRules.Describe(operation));
        return OperationResult<BulkPricePreview>.Success(preview);
    }

    public async Task<OperationResult<Guid>> ApplyAsync(BulkPriceScope scope, BulkPriceOperation operation, string expectedToken, Guid? userId, string? userName)
    {
        if (BulkPriceRules.Validate(operation) is { } error)
            return OperationResult<Guid>.Error(error);

        var now = DateTimeOffset.UtcNow;
        var products = await Filter(scope).AsTracking().OrderBy(p => p.Name).Take(BulkPriceRules.MaxProducts).ToListAsync();

        var changes = new List<(Product Product, PriceSnapshot Old, PriceSnapshot New)>();
        foreach (var product in products)
        {
            var old = Snapshot(product);
            var result = BulkPriceRules.Compute(old, operation, now);
            if (!result.IsSkipped)
                changes.Add((product, old, result.New));
        }

        if (changes.Count == 0)
            return OperationResult<Guid>.Error("هیچ محصولی برای تغییر باقی نماند.");

        // اگر از زمان پیش‌نمایش چیزی عوض شده، چیزی که کارمند ندیده ذخیره نمی‌شود.
        var token = BulkPriceRules.Fingerprint(changes.Select(c => (c.Product.Id, c.New)));
        if (!string.Equals(token, expectedToken, StringComparison.Ordinal))
            return OperationResult<Guid>.Error("از زمان پیش‌نمایش، قیمت یا محدودهٔ محصولات تغییر کرده است. پیش‌نمایش تازه را بررسی و دوباره تأیید کنید.").WithCode("stale");

        foreach (var (product, _, next) in changes)
            ApplySnapshot(product, next);

        var batch = Track(BulkPriceRules.Describe(operation), PriceChangeSource.BulkTool, changes.Select(c => (c.Product, c.Old)), userId, userName)!;

        // یک SaveChanges = یک تراکنش: یا همهٔ محصولات و تاریخچه ذخیره می‌شوند یا هیچ‌کدام.
        await context.SaveChangesAsync();
        return OperationResult<Guid>.Success(batch.Id, $"قیمت {changes.Count:N0} محصول تغییر کرد. در صورت نیاز از «تاریخچه» قابل برگرداندن است.");
    }

    public PriceChangeBatch? Track(string title, PriceChangeSource source, IEnumerable<(Product Product, PriceSnapshot Old)> changed, Guid? userId, string? userName)
    {
        var batch = new PriceChangeBatch
        {
            Id = Guid.NewGuid(),
            Title = Cut(title, 500)!,
            Source = source,
            CreatedByUserId = userId,
            CreatedByName = Cut(userName, 200),
        };
        foreach (var (product, old) in changed)
        {
            var next = Snapshot(product);
            if (next == old)
                continue;
            batch.Items.Add(new PriceChangeItem
            {
                ProductId = product.Id,
                OldPrice = old.Price, NewPrice = next.Price,
                OldDiscountPrice = old.DiscountPrice, NewDiscountPrice = next.DiscountPrice,
                OldDiscountStartAt = old.DiscountStartAt, NewDiscountStartAt = next.DiscountStartAt,
                OldDiscountEndAt = old.DiscountEndAt, NewDiscountEndAt = next.DiscountEndAt,
                OldPurchasePrice = old.PurchasePrice, NewPurchasePrice = next.PurchasePrice,
            });
        }
        if (batch.Items.Count == 0)
            return null;
        batch.AffectedCount = batch.Items.Count;
        context.PriceChangeBatches.Add(batch);
        return batch;
    }

    public Task<List<PriceBatchRow>> ListBatchesAsync(int take = 30) =>
        context.PriceChangeBatches.OrderByDescending(b => b.CreatedAt).Take(Math.Clamp(take, 1, 200))
            .Select(b => new PriceBatchRow(b.Id, b.Title, b.Source, b.AffectedCount, b.CreatedByName, b.CreatedAt, b.RevertedAt, b.RevertedByName, b.RevertNote))
            .ToListAsync();

    public async Task<OperationResult> RevertAsync(Guid batchId, Guid? userId, string? userName)
    {
        var batch = await context.PriceChangeBatches.AsTracking().Include(b => b.Items).FirstOrDefaultAsync(b => b.Id == batchId);
        if (batch is null)
            return OperationResult.NotFound("این دسته پیدا نشد.");
        if (batch.RevertedAt is not null)
            return OperationResult.Error("این دسته قبلاً برگردانده شده است.");

        var ids = batch.Items.Select(i => i.ProductId).ToList();
        var products = await context.Products.AsTracking().Where(p => ids.Contains(p.Id)).ToDictionaryAsync(p => p.Id);

        int reverted = 0, partial = 0, untouched = 0;
        foreach (var item in batch.Items)
        {
            if (!products.TryGetValue(item.ProductId, out var product))
            {
                untouched++;
                continue;
            }

            // هر فیلد فقط اگر هنوز همان مقدار این دسته را دارد برمی‌گردد.
            var fields = 0;
            var done = 0;
            void Field<T>(T oldValue, T newValue, Func<T> get, Action<T> set)
            {
                if (EqualityComparer<T>.Default.Equals(oldValue, newValue))
                    return;
                fields++;
                if (!EqualityComparer<T>.Default.Equals(get(), newValue))
                    return;
                set(oldValue);
                done++;
            }

            Field(item.OldPrice, item.NewPrice, () => product.Price, v => product.Price = v);
            Field(item.OldPurchasePrice, item.NewPurchasePrice, () => product.PurchasePrice, v => product.PurchasePrice = v);
            // حراج و بازه‌اش با هم: اگر هر سه دست نخورده‌اند با هم برمی‌گردند (نیمه‌کاره برگرداندن حراج بی‌معناست).
            var discountChanged = item.OldDiscountPrice != item.NewDiscountPrice || item.OldDiscountStartAt != item.NewDiscountStartAt || item.OldDiscountEndAt != item.NewDiscountEndAt;
            if (discountChanged)
            {
                fields++;
                if (product.DiscountPrice == item.NewDiscountPrice && product.DiscountStartAt == item.NewDiscountStartAt && product.DiscountEndAt == item.NewDiscountEndAt)
                {
                    product.DiscountPrice = item.OldDiscountPrice;
                    product.DiscountStartAt = item.OldDiscountStartAt;
                    product.DiscountEndAt = item.OldDiscountEndAt;
                    done++;
                }
            }

            // اگر قیمت حراج برگشته ولی قیمت اصلی نه (یا برعکس) و حراج دیگر کمتر نیست، حراج حذف می‌شود تا قیمت نامعتبر نماند.
            if (product.DiscountPrice is { } d && d >= product.Price)
            {
                product.DiscountPrice = null;
                product.DiscountStartAt = null;
                product.DiscountEndAt = null;
            }

            if (done == 0) untouched++;
            else if (done < fields) partial++;
            else reverted++;
        }

        var note = $"{reverted:N0} محصول کامل برگشت" + (partial > 0 ? $"، {partial:N0} محصول بخشی" : "")
                   + (untouched > 0 ? $"، {untouched:N0} محصول بعداً تغییر کرده بود و دست نخورد" : "") + ".";
        batch.RevertedAt = DateTimeOffset.UtcNow;
        batch.RevertedByName = Cut(userName, 200);
        batch.RevertNote = Cut(note, 500);
        await context.SaveChangesAsync();
        return OperationResult.Success("دسته برگردانده شد: " + note);
    }

    /// <summary>فیلتر محدوده؛ همان معیارهای صفحهٔ فهرست محصولات.</summary>
    private IQueryable<Product> Filter(BulkPriceScope scope)
    {
        var query = context.Products.AsQueryable();
        if (scope.ProductIds is { Count: > 0 } ids)
        {
            var list = ids.Distinct().Take(BulkPriceRules.MaxProducts).ToList();
            return query.Where(p => list.Contains(p.Id));
        }
        if (!string.IsNullOrWhiteSpace(scope.Search))
        {
            var term = SearchText.NormalizeDigits(scope.Search.Trim());
            query = query.Where(p => p.Name.Contains(term) || p.SKU.Contains(term));
        }
        if (scope.CategoryId is { } categoryId)
            query = query.Where(p => p.CategoryId == categoryId || p.Category.ParentId == categoryId);
        if (scope.BrandId is { } brandId)
            query = query.Where(p => p.BrandId == brandId);
        if (scope.SupplierId is { } supplierId)
            query = query.Where(p => p.SupplierId == supplierId);
        query = scope.Stock switch
        {
            "in" => query.Where(p => p.StockQuantity > 0),
            "out" => query.Where(p => p.StockQuantity <= 0),
            _ => query,
        };
        query = scope.State switch
        {
            "active" => query.Where(p => p.IsActive),
            "inactive" => query.Where(p => !p.IsActive),
            _ => query,
        };
        return query;
    }

    private static PriceSnapshot Snapshot(Product p) => new(p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt, p.PurchasePrice);

    private static void ApplySnapshot(Product product, PriceSnapshot next)
    {
        product.Price = next.Price;
        product.DiscountPrice = next.DiscountPrice;
        product.DiscountStartAt = next.DiscountStartAt;
        product.DiscountEndAt = next.DiscountEndAt;
        product.PurchasePrice = next.PurchasePrice;
    }

    private static string? Cut(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];
}
