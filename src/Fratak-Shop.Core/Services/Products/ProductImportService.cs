using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Utilities;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Products;

// ═══════════════════════════════════════════════════════════════════════════
//  به‌روزرسانی گروهی محصولات از فایل اکسل/CSV (مرحلهٔ هفتم – فاز ۵)
//  روند: فایل خروجی «اکسل» فهرست محصولات را بگیرید ← قیمت/موجودی را در اکسل عوض کنید ← بارگذاری کنید ←
//  پیش‌نمایش تغییرها (قبلی → جدید، خطاها، هشدار زیان) ← تأیید.
//  • محصول با «کد کالا» (SKU) پیدا می‌شود؛ محصول تازه ساخته نمی‌شود (ساخت محصول فرم کامل لازم دارد).
//  • سلول خالی یعنی «تغییر نده». برای حذف حراج عدد ۰ بنویسید.
//  • تغییر قیمت‌ها یک «دسته» در تاریخچهٔ تغییر گروهی قیمت است (قابل برگرداندن) و تغییر موجودی در دفتر موجودی
//    با دلیل «ورود از فایل» ثبت می‌شود.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// یک ردیف خوانده‌شده از فایل. null یعنی «این ستون در فایل نبود یا خالی بود ← تغییر نده».
/// </summary>
public sealed record ProductImportLine(int RowNumber, string Sku, decimal? Price, decimal? DiscountPrice, int? Stock, decimal? PurchasePrice, bool? IsActive);

/// <summary>نتیجهٔ خواندن فایل: ردیف‌های سالم + خطاهای قالب (مثلاً عدد نامعتبر).</summary>
public sealed record ProductImportParse(IReadOnlyList<ProductImportLine> Lines, IReadOnlyList<string> Errors, IReadOnlyList<string> Columns);

/// <summary>یک تغییر فیلد برای پیش‌نمایش؛ مثال: («قیمت», "400,000", "415,000").</summary>
public sealed record ProductImportFieldChange(string Field, string Before, string After);

/// <summary>ردیف پیش‌نمایش؛ اگر <see cref="Error"/> پر باشد اعمال نمی‌شود.</summary>
public sealed record ProductImportPreviewRow(int RowNumber, string Sku, Guid? ProductId, string? Name,
    IReadOnlyList<ProductImportFieldChange> Changes, string? Error, string? Warning);

public sealed record ProductImportPreview(IReadOnlyList<ProductImportPreviewRow> Rows, int Changed, int Unchanged, int Failed);

/// <summary>
/// تبدیل جدول خام (ردیف‌های متنی) به ردیف‌های ورود. بدون دیتابیس؛ قابل تست.
/// </summary>
public static class ProductImportRules
{
    /// <summary>بیشترین تعداد ردیف در یک فایل.</summary>
    public const int MaxLines = 5000;

    // نام سرستون‌ها: همان ستون‌های خروجی فهرست محصولات + معادل انگلیسی.
    private static readonly Dictionary<string, string> HeaderMap = new(StringComparer.OrdinalIgnoreCase)
    {
        ["کد کالا"] = "sku", ["sku"] = "sku", ["کد"] = "sku",
        ["قیمت"] = "price", ["price"] = "price", ["قیمت فروش"] = "price",
        ["قیمت با تخفیف"] = "discount", ["قیمت حراج"] = "discount", ["discount_price"] = "discount", ["sale_price"] = "discount",
        ["موجودی"] = "stock", ["stock"] = "stock", ["quantity"] = "stock",
        ["قیمت خرید"] = "purchase", ["purchase_price"] = "purchase", ["cost"] = "purchase",
        ["وضعیت"] = "active", ["active"] = "active", ["status"] = "active",
    };

    /// <summary>
    /// خواندن ردیف‌ها. اولین ردیف = سرستون. ستون «کد کالا» الزامی است و حداقل یک ستون قابل تغییر لازم است.
    /// مثال سرستون: «نام | کد کالا | قیمت | موجودی» (ستون‌های دیگر نادیده گرفته می‌شوند).
    /// </summary>
    public static ProductImportParse Parse(IReadOnlyList<string[]> table)
    {
        var errors = new List<string>();
        var lines = new List<ProductImportLine>();
        if (table.Count == 0)
            return new ProductImportParse(lines, ["فایل خالی است."], []);

        var columns = new Dictionary<string, int>();
        for (var i = 0; i < table[0].Length; i++)
        {
            var header = NormalizeHeader(table[0][i]);
            if (HeaderMap.TryGetValue(header, out var key) && !columns.ContainsKey(key))
                columns[key] = i;
        }

        if (!columns.ContainsKey("sku"))
            return new ProductImportParse(lines, ["ستون «کد کالا» در ردیف اول پیدا نشد."], [.. columns.Keys]);
        if (columns.Count == 1)
            return new ProductImportParse(lines, ["هیچ ستون قابل تغییری (قیمت، قیمت با تخفیف، موجودی، قیمت خرید، وضعیت) پیدا نشد."], [.. columns.Keys]);

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        for (var r = 1; r < table.Count; r++)
        {
            if (lines.Count >= MaxLines)
            {
                errors.Add($"فقط {MaxLines:N0} ردیف اول خوانده شد.");
                break;
            }

            var row = table[r];
            var rowNumber = r + 1;
            string Cell(string key) => columns.TryGetValue(key, out var index) && index < row.Length ? row[index] : "";

            var sku = SearchText.NormalizeDigits(Cell("sku")).Trim().TrimStart('\'');
            if (sku.Length == 0)
                continue;
            if (!seen.Add(sku))
            {
                errors.Add($"ردیف {rowNumber}: کد کالای «{sku}» تکراری است؛ فقط اولین ردیف استفاده می‌شود.");
                continue;
            }

            var rowErrors = new List<string>();
            decimal? Money(string key, string title)
            {
                if (!SpreadsheetReader.TryParseNumber(Cell(key), out var value) || value is < 0)
                {
                    rowErrors.Add($"{title} نامعتبر است");
                    return null;
                }
                return value is null ? null : decimal.Round(value.Value, 0, MidpointRounding.AwayFromZero);
            }

            var price = Money("price", "قیمت");
            var discount = Money("discount", "قیمت با تخفیف");
            var purchase = Money("purchase", "قیمت خرید");
            int? stock = null;
            if (!SpreadsheetReader.TryParseNumber(Cell("stock"), out var stockValue) || stockValue is < 0 or > 1_000_000 || (stockValue is { } sv && sv != decimal.Truncate(sv)))
                rowErrors.Add("موجودی نامعتبر است");
            else if (stockValue is { } s)
                stock = (int)s;

            bool? active = null;
            var activeText = Cell("active").Trim();
            if (activeText.Length > 0)
            {
                active = ParseActive(activeText);
                if (active is null)
                    rowErrors.Add("وضعیت باید «فعال» یا «غیرفعال» باشد");
            }

            if (price is 0)
                rowErrors.Add("قیمت نمی‌تواند صفر باشد");

            if (rowErrors.Count > 0)
            {
                errors.Add($"ردیف {rowNumber} ({sku}): {string.Join("، ", rowErrors)}.");
                continue;
            }

            lines.Add(new ProductImportLine(rowNumber, sku, price, discount, stock, purchase, active));
        }

        return new ProductImportParse(lines, errors, [.. columns.Keys]);
    }

    /// <summary>«فعال/بله/1/true» → true؛ «غیرفعال/خیر/0/false» → false؛ بقیه null.</summary>
    public static bool? ParseActive(string text) => SearchText.NormalizeDigits(text.Trim()).ToLowerInvariant() switch
    {
        "فعال" or "بله" or "1" or "true" or "yes" or "active" => true,
        "غیرفعال" or "غير فعال" or "غیر فعال" or "خیر" or "0" or "false" or "no" or "inactive" => false,
        _ => null,
    };

    private static string NormalizeHeader(string header) =>
        header.Replace("\u200c", " ").Replace('ي', 'ی').Replace('ك', 'ک').Trim().Trim('*').Trim();
}

public interface IProductImportService
{
    /// <summary>پیش‌نمایش: مقایسهٔ ردیف‌ها با مقدارهای فعلی دیتابیس (چیزی ذخیره نمی‌شود).</summary>
    Task<ProductImportPreview> PreviewAsync(IReadOnlyList<ProductImportLine> lines, CancellationToken cancellationToken = default);

    /// <summary>اعمال ردیف‌های بدون خطا؛ دوباره با دادهٔ همین لحظه محاسبه می‌شود.</summary>
    Task<OperationResult> ApplyAsync(IReadOnlyList<ProductImportLine> lines, string fileName, Guid? userId, string? userName);
}

public sealed class ProductImportService(FratakShopDbContext context, IBulkPriceService bulkPrices) : IProductImportService
{
    public async Task<ProductImportPreview> PreviewAsync(IReadOnlyList<ProductImportLine> lines, CancellationToken cancellationToken = default)
    {
        var products = await LoadAsync(lines, tracking: false, cancellationToken);
        var rows = new List<ProductImportPreviewRow>(lines.Count);
        foreach (var line in lines)
        {
            products.TryGetValue(line.Sku, out var product);
            var (changes, error, warning) = Compare(line, product);
            rows.Add(new ProductImportPreviewRow(line.RowNumber, line.Sku, product?.Id, product?.Name, changes, error, warning));
        }

        return new ProductImportPreview(
            rows.OrderBy(r => r.Error is null).ThenByDescending(r => r.Changes.Count > 0).ThenBy(r => r.RowNumber).ToList(),
            rows.Count(r => r.Error is null && r.Changes.Count > 0),
            rows.Count(r => r.Error is null && r.Changes.Count == 0),
            rows.Count(r => r.Error is not null));
    }

    public async Task<OperationResult> ApplyAsync(IReadOnlyList<ProductImportLine> lines, string fileName, Guid? userId, string? userName)
    {
        var products = await LoadAsync(lines, tracking: true, CancellationToken.None);
        var priceChanged = new List<(Product Product, PriceSnapshot Old)>();
        var changed = 0;
        var failed = 0;

        foreach (var line in lines)
        {
            if (!products.TryGetValue(line.Sku, out var product))
            {
                failed++;
                continue;
            }
            var (changes, error, _) = Compare(line, product);
            if (error is not null)
            {
                failed++;
                continue;
            }
            if (changes.Count == 0)
                continue;

            var old = new PriceSnapshot(product.Price, product.DiscountPrice, product.DiscountStartAt, product.DiscountEndAt, product.PurchasePrice);
            Apply(line, product);
            if (old != new PriceSnapshot(product.Price, product.DiscountPrice, product.DiscountStartAt, product.DiscountEndAt, product.PurchasePrice))
                priceChanged.Add((product, old));
            changed++;
        }

        if (changed == 0)
            return OperationResult.Error("هیچ تغییری برای اعمال نبود" + (failed > 0 ? $" ({failed:N0} ردیف خطا داشت)." : "."));

        var shortName = Path.GetFileName(fileName);
        if (priceChanged.Count > 0)
            bulkPrices.Track($"ورود از فایل «{shortName}»", PriceChangeSource.Import, priceChanged, userId, userName);

        // یک SaveChanges: همه یا هیچ؛ تغییر موجودی‌ها با دلیل «ورود از فایل» در دفتر ثبت می‌شود.
        using (StockContext.Begin(StockMovementReason.Import, "ورود از فایل", reference: shortName))
            await context.SaveChangesAsync();

        return OperationResult.Success($"{changed:N0} محصول به‌روزرسانی شد" + (failed > 0 ? $"؛ {failed:N0} ردیف به دلیل خطا اعمال نشد." : ".")
                                       + (priceChanged.Count > 0 ? " تغییر قیمت‌ها از تاریخچهٔ «تغییر گروهی قیمت» قابل برگرداندن است." : ""));
    }

    /// <summary>
    /// مقایسهٔ یک ردیف با محصول فعلی: فهرست تغییرها، خطا (اعمال نمی‌شود) و هشدار (اعمال می‌شود).
    /// </summary>
    private static (List<ProductImportFieldChange> Changes, string? Error, string? Warning) Compare(ProductImportLine line, Product? product)
    {
        var changes = new List<ProductImportFieldChange>();
        if (product is null)
            return (changes, "محصولی با این کد کالا پیدا نشد.", null);

        var price = line.Price ?? product.Price;
        // ۰ در ستون حراج یعنی حذف حراج.
        var discount = line.DiscountPrice is null ? product.DiscountPrice : line.DiscountPrice == 0 ? null : line.DiscountPrice;
        var purchase = line.PurchasePrice ?? product.PurchasePrice;

        if (discount is { } d && d >= price)
            return (changes, $"قیمت با تخفیف ({d:N0}) باید کمتر از قیمت ({price:N0}) باشد.", null);

        if (price != product.Price)
            changes.Add(new("قیمت", $"{product.Price:N0}", $"{price:N0}"));
        if (discount != product.DiscountPrice)
            changes.Add(new("قیمت با تخفیف", product.DiscountPrice is { } od ? $"{od:N0}" : "—", discount is { } nd ? $"{nd:N0}" : "حذف"));
        if (purchase != product.PurchasePrice)
            changes.Add(new("قیمت خرید", product.PurchasePrice is { } op ? $"{op:N0}" : "—", $"{purchase:N0}"));
        if (line.Stock is { } stock && stock != product.StockQuantity)
            changes.Add(new("موجودی", $"{product.StockQuantity:N0}", $"{stock:N0}"));
        if (line.IsActive is { } active && active != product.IsActive)
            changes.Add(new("وضعیت", product.IsActive ? "فعال" : "غیرفعال", active ? "فعال" : "غیرفعال"));

        string? warning = null;
        var lowest = discount is { } sale && (product.DiscountEndAt is null || product.DiscountEndAt >= DateTimeOffset.UtcNow) ? sale : price;
        if (purchase is > 0 && lowest < purchase)
            warning = $"زیان: قیمت فروش ({lowest:N0}) کمتر از قیمت خرید ({purchase:N0}) است.";
        else if (product.Price > 0 && Math.Abs(price - product.Price) / product.Price >= 0.5m)
            warning = "تغییر قیمت بیش از ۵۰٪ است؛ دوباره بررسی کنید.";
        return (changes, null, warning);
    }

    private static void Apply(ProductImportLine line, Product product)
    {
        if (line.Price is { } price)
            product.Price = price;
        if (line.DiscountPrice is { } discount)
        {
            if (discount == 0)
            {
                product.DiscountPrice = null;
                product.DiscountStartAt = null;
                product.DiscountEndAt = null;
            }
            else
            {
                product.DiscountPrice = discount;
            }
        }
        if (line.PurchasePrice is { } purchase)
            product.PurchasePrice = purchase;
        if (line.Stock is { } stock)
            product.StockQuantity = stock;
        if (line.IsActive is { } active)
            product.IsActive = active;
    }

    private async Task<Dictionary<string, Product>> LoadAsync(IReadOnlyList<ProductImportLine> lines, bool tracking, CancellationToken cancellationToken)
    {
        var skus = lines.Select(l => l.Sku).Distinct(StringComparer.OrdinalIgnoreCase).Take(ProductImportRules.MaxLines).ToList();
        var result = new Dictionary<string, Product>(StringComparer.OrdinalIgnoreCase);
        // در دسته‌های ۵۰۰تایی تا کوئری SQL خیلی بزرگ نشود.
        foreach (var chunk in skus.Chunk(500))
        {
            var query = context.Products.Where(p => chunk.Contains(p.SKU));
            if (tracking)
                query = query.AsTracking();
            foreach (var product in await query.ToListAsync(cancellationToken))
                result.TryAdd(product.SKU, product);
        }
        return result;
    }
}
