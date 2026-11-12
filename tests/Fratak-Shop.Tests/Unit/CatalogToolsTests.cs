using System.Text;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Web.Infrastructure.Admin;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های ابزارهای کاتالوگ (مرحلهٔ هفتم – فاز ۵) بدون دیتابیس:
/// محاسبهٔ تغییر گروهی قیمت (گرد کردن، حراج، زیان، اعتبارسنجی، اثر انگشت)، پیش‌بینی اتمام موجودی،
/// امتیاز کامل بودن، خواندن فایل ورود (CSV و xlsx خروجی خود پنل) و تاریخ/ساعت شمسی حراج زمان‌دار.
/// </summary>
public class CatalogToolsTests
{
    private static readonly DateTimeOffset Now = new(2025, 11, 1, 9, 0, 0, TimeSpan.Zero);

    private static PriceSnapshot Snap(decimal price, decimal? discount = null, decimal? purchase = null) =>
        new(price, discount, null, null, purchase);

    // ── گرد کردن ─────────────────────────────────────────────────────────

    [Theory]
    [InlineData(412_300, 1000, BulkPriceRoundDirection.Nearest, 412_000)]
    [InlineData(412_500, 1000, BulkPriceRoundDirection.Nearest, 413_000)]
    [InlineData(412_300, 1000, BulkPriceRoundDirection.Up, 413_000)]
    [InlineData(412_900, 1000, BulkPriceRoundDirection.Down, 412_000)]
    [InlineData(412_349, 100, BulkPriceRoundDirection.Nearest, 412_300)]
    [InlineData(412_300.4, 0, BulkPriceRoundDirection.Nearest, 412_300)]
    [InlineData(4_990_000, 10000, BulkPriceRoundDirection.Up, 4_990_000)]
    public void Round_follows_step_and_direction(decimal value, int step, BulkPriceRoundDirection direction, decimal expected)
    {
        Assert.Equal(expected, BulkPriceRules.Round(value, step, direction));
    }

    [Fact]
    public void Round_down_never_turns_a_positive_price_into_zero()
    {
        Assert.Equal(1000, BulkPriceRules.Round(400, 1000, BulkPriceRoundDirection.Down));
        Assert.Equal(0, BulkPriceRules.Round(-5, 1000, BulkPriceRoundDirection.Nearest));
    }

    // ── محاسبه ───────────────────────────────────────────────────────────

    [Fact]
    public void Percent_increase_changes_the_price_and_keeps_a_healthy_margin()
    {
        var result = BulkPriceRules.Compute(Snap(400_000, purchase: 300_000),
            new BulkPriceOperation(BulkPriceTarget.Price, BulkPriceMode.Percent, 10), Now);

        Assert.False(result.IsSkipped);
        Assert.Equal(440_000, result.New.Price);
        Assert.Empty(result.Warnings);
    }

    [Fact]
    public void Existing_discount_is_scaled_with_the_price()
    {
        var result = BulkPriceRules.Compute(Snap(400_000, discount: 360_000),
            new BulkPriceOperation(BulkPriceTarget.Price, BulkPriceMode.Percent, 10), Now);

        Assert.Equal(440_000, result.New.Price);
        Assert.Equal(396_000, result.New.DiscountPrice);
    }

    [Fact]
    public void Discount_that_is_no_longer_lower_is_removed_with_a_warning()
    {
        // قیمت ۲۰٪ کم می‌شود ولی حراج ثابت می‌ماند (ScaleDiscount=false) → حراج ۳۸۰ از قیمت ۳۲۰ بیشتر است.
        var result = BulkPriceRules.Compute(Snap(400_000, discount: 380_000),
            new BulkPriceOperation(BulkPriceTarget.Price, BulkPriceMode.Percent, -20, ScaleDiscount: false), Now);

        Assert.Equal(320_000, result.New.Price);
        Assert.Null(result.New.DiscountPrice);
        Assert.Single(result.Warnings);
    }

    [Fact]
    public void Loss_making_price_change_is_always_skipped_even_without_the_option()
    {
        // دور ۲۹ (#۱۳ – «همیشه سود»): تیک SkipLoss دیگر اجازهٔ عبور زیان را نمی‌دهد.
        var old = Snap(400_000, purchase: 300_000);

        var skipped = BulkPriceRules.Compute(old, new BulkPriceOperation(BulkPriceTarget.Price, BulkPriceMode.Percent, -50, SkipLoss: true), Now);
        Assert.True(skipped.IsSkipped);
        Assert.StartsWith("زیان", skipped.SkipReason);
        Assert.Equal(old, skipped.New);

        var alsoSkipped = BulkPriceRules.Compute(old, new BulkPriceOperation(BulkPriceTarget.Price, BulkPriceMode.Percent, -50, SkipLoss: false), Now);
        Assert.True(alsoSkipped.IsSkipped);
        Assert.StartsWith("زیان", alsoSkipped.SkipReason);
        Assert.Equal(old, alsoSkipped.New);
    }

    [Fact]
    public void Break_even_discount_is_blocked_as_no_profit()
    {
        // خرید ۳۰۰ هزار، قیمت ۶۰۰ هزار، تخفیف ۵۰٪ ← ۳۰۰ هزار = سر‌به‌سر ← رد.
        var old = Snap(600_000, purchase: 300_000);
        var result = BulkPriceRules.Compute(old, new BulkPriceOperation(BulkPriceTarget.DiscountPrice, BulkPriceMode.PercentOff, 50), Now);

        Assert.True(result.IsSkipped);
        Assert.StartsWith("زیان یا بدون سود", result.SkipReason, StringComparison.Ordinal);
        Assert.Equal(old, result.New);
    }

    [Fact]
    public void Discount_one_step_above_purchase_is_allowed()
    {
        // ۴۹٪ تخفیف ← ۳۰۶ هزار > ۳۰۰ هزار ← مجاز (سود کم ولی مثبت).
        var result = BulkPriceRules.Compute(Snap(600_000, purchase: 300_000),
            new BulkPriceOperation(BulkPriceTarget.DiscountPrice, BulkPriceMode.PercentOff, 49), Now);

        Assert.False(result.IsSkipped);
        Assert.Equal(306_000, result.New.DiscountPrice);
    }

    [Fact]
    public void Raising_the_purchase_price_warns_unless_skip_loss_is_checked()
    {
        // تغییر «قیمت خرید» واقعیت خرید است ← بدون تیک فقط هشدار، با تیک رد.
        var old = Snap(400_000, purchase: 300_000);

        var warned = BulkPriceRules.Compute(old, new BulkPriceOperation(BulkPriceTarget.PurchasePrice, BulkPriceMode.Percent, 40, SkipLoss: false), Now);
        Assert.False(warned.IsSkipped);
        Assert.Equal(420_000, warned.New.PurchasePrice);
        Assert.Contains(warned.Warnings, w => w.StartsWith("زیان", StringComparison.Ordinal));

        var skipped = BulkPriceRules.Compute(old, new BulkPriceOperation(BulkPriceTarget.PurchasePrice, BulkPriceMode.Percent, 40, SkipLoss: true), Now);
        Assert.True(skipped.IsSkipped);
        Assert.StartsWith("زیان", skipped.SkipReason);
    }

    [Fact]
    public void Purchase_price_equal_to_the_selling_price_is_flagged_as_no_profit()
    {
        var result = BulkPriceRules.Compute(Snap(400_000, purchase: 300_000),
            new BulkPriceOperation(BulkPriceTarget.PurchasePrice, BulkPriceMode.Set, 400_000, SkipLoss: true), Now);

        Assert.True(result.IsSkipped);
        Assert.StartsWith("زیان یا بدون سود", result.SkipReason, StringComparison.Ordinal);
    }

    [Fact]
    public void Margin_over_purchase_needs_a_purchase_price()
    {
        var op = new BulkPriceOperation(BulkPriceTarget.Price, BulkPriceMode.MarginOverPurchase, 25);

        Assert.Equal(500_000, BulkPriceRules.Compute(Snap(450_000, purchase: 400_000), op, Now).New.Price);
        Assert.True(BulkPriceRules.Compute(Snap(450_000), op, Now).IsSkipped);
    }

    [Fact]
    public void Timed_sale_sets_discount_and_window()
    {
        var start = Now.AddHours(2);
        var end = Now.AddDays(2);
        var result = BulkPriceRules.Compute(Snap(500_000),
            new BulkPriceOperation(BulkPriceTarget.DiscountPrice, BulkPriceMode.PercentOff, 20, DiscountStartAt: start, DiscountEndAt: end), Now);

        Assert.Equal(400_000, result.New.DiscountPrice);
        Assert.Equal(start, result.New.DiscountStartAt);
        Assert.Equal(end, result.New.DiscountEndAt);
        // حراج هنوز شروع نشده؛ مشتری فعلاً قیمت اصلی را می‌پردازد ولی سود با قیمت حراج سنجیده می‌شود.
        Assert.Equal(500_000, BulkPriceRules.Final(result.New, Now));
        Assert.Equal(400_000, BulkPriceRules.LowestSellingPrice(result.New, Now));
    }

    [Fact]
    public void Unchanged_and_impossible_rows_are_skipped_with_a_reason()
    {
        Assert.Equal("بدون تغییر.", BulkPriceRules.Compute(Snap(400_000), new BulkPriceOperation(BulkPriceTarget.Price, BulkPriceMode.Set, 400_000), Now).SkipReason);
        Assert.True(BulkPriceRules.Compute(Snap(400_000), new BulkPriceOperation(BulkPriceTarget.RemoveDiscount, BulkPriceMode.Set, 0), Now).IsSkipped);
        Assert.True(BulkPriceRules.Compute(Snap(400_000), new BulkPriceOperation(BulkPriceTarget.DiscountPrice, BulkPriceMode.Set, 450_000), Now).IsSkipped);
        Assert.True(BulkPriceRules.Compute(Snap(400_000), new BulkPriceOperation(BulkPriceTarget.PurchasePrice, BulkPriceMode.Percent, 5), Now).IsSkipped);
    }

    [Fact]
    public void Validate_rejects_bad_operations()
    {
        Assert.Null(BulkPriceRules.Validate(new BulkPriceOperation(BulkPriceTarget.Price, BulkPriceMode.Percent, 10)));
        Assert.NotNull(BulkPriceRules.Validate(new BulkPriceOperation(BulkPriceTarget.Price, BulkPriceMode.Percent, -95)));
        Assert.NotNull(BulkPriceRules.Validate(new BulkPriceOperation(BulkPriceTarget.Price, BulkPriceMode.Percent, 0)));
        Assert.NotNull(BulkPriceRules.Validate(new BulkPriceOperation(BulkPriceTarget.DiscountPrice, BulkPriceMode.Percent, 10)));
        Assert.NotNull(BulkPriceRules.Validate(new BulkPriceOperation(BulkPriceTarget.Price, BulkPriceMode.Percent, 10, RoundStep: 500)));
        Assert.NotNull(BulkPriceRules.Validate(new BulkPriceOperation(BulkPriceTarget.DiscountPrice, BulkPriceMode.PercentOff, 10,
            DiscountStartAt: Now, DiscountEndAt: Now.AddHours(-1))));
        Assert.Null(BulkPriceRules.Validate(new BulkPriceOperation(BulkPriceTarget.RemoveDiscount, BulkPriceMode.Percent, 0)));
    }

    [Fact]
    public void Fingerprint_is_order_independent_and_detects_changes()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var first = BulkPriceRules.Fingerprint([(a, Snap(100_000)), (b, Snap(200_000))]);
        var reordered = BulkPriceRules.Fingerprint([(b, Snap(200_000)), (a, Snap(100_000))]);
        var changed = BulkPriceRules.Fingerprint([(a, Snap(100_000)), (b, Snap(201_000))]);

        Assert.Equal(first, reordered);
        Assert.NotEqual(first, changed);
    }

    // ── پیش‌بینی موجودی ──────────────────────────────────────────────────

    [Theory]
    [InlineData(6, 45, 30, StockForecastLevel.Critical, 4.0, 26)]
    [InlineData(10, 30, 30, StockForecastLevel.Soon, 10.0, 11)]
    [InlineData(100, 30, 30, StockForecastLevel.Ok, 100.0, 0)]
    [InlineData(0, 30, 30, StockForecastLevel.OutOfStock, 0.0, 21)]
    public void Forecast_levels_and_reorder(int stock, int sold, int window, StockForecastLevel level, double daysLeft, int reorder)
    {
        var forecast = StockForecastRules.Estimate(stock, sold, window);

        Assert.Equal(level, forecast.Level);
        Assert.Equal((decimal)daysLeft, forecast.DaysLeft);
        Assert.Equal(reorder, forecast.SuggestedReorder);
    }

    [Fact]
    public void Forecast_without_sales_has_no_estimate()
    {
        var forecast = StockForecastRules.Estimate(10, 0, 30);

        Assert.Equal(StockForecastLevel.NoSales, forecast.Level);
        Assert.Null(forecast.DaysLeft);
        Assert.Equal(0, forecast.SuggestedReorder);
    }

    // ── کامل بودن ────────────────────────────────────────────────────────

    [Fact]
    public void Completeness_weights_sum_to_100()
    {
        var complete = ProductCompleteness.Evaluate(new ProductCompletenessInput(30, true, 80, 900, 4, 4, true, 6, 45, 140, true, true, true));
        var empty = ProductCompleteness.Evaluate(new ProductCompletenessInput(0, false, 0, 0, 0, 0, false, 0, 0, 0, false, false, false));

        Assert.Equal(100, complete.Items.Sum(i => i.Weight));
        Assert.Equal(100, complete.Score);
        Assert.Equal("good", complete.Level);
        Assert.Equal(0, empty.Score);
        Assert.Equal("poor", empty.Level);
        Assert.All(empty.Items, i => Assert.False(i.Done));
        Assert.Equal(complete.Items.Count, complete.Items.Select(i => i.Key).Distinct().Count());
    }

    [Fact]
    public void Completeness_lists_missing_items_first()
    {
        // فقط عکس ندارد: «۳ عکس» (۱۵) + «عکس اصلی» (۱۰) + «متن جایگزین» (۵) کم می‌شود → ۷۰ (متوسط).
        var result = ProductCompleteness.Evaluate(new ProductCompletenessInput(30, true, 80, 900, 0, 0, true, 6, 45, 140, true, true, true));

        Assert.Equal(100 - 15 - 10 - 5, result.Score);
        Assert.Equal("fair", result.Level);
        Assert.Equal(new[] { "images", "image", "alt" }, result.Items.TakeWhile(i => !i.Done).Select(i => i.Key).ToArray());
    }

    // ── ورود از فایل ─────────────────────────────────────────────────────

    [Fact]
    public void Import_parse_reads_valid_rows_and_reports_bad_ones()
    {
        var table = new List<string[]>
        {
            new[] { "نام", "کد کالا", "قیمت", "قیمت با تخفیف", "موجودی", "وضعیت" },
            new[] { "شامپو", "SKU-1", "۴۱۵٬۰۰۰", "", "12", "فعال" },
            new[] { "کرم", "SKU-2", "abc", "", "", "" },
            new[] { "تکراری", "sku-1", "100000", "", "", "" },
            new[] { "حذف حراج", "SKU-3", "", "0", "", "غیرفعال" },
            new[] { "", "", "", "", "", "" },
            new[] { "اعشاری", "SKU-4", "", "", "1.5", "" },
        };

        var parsed = ProductImportRules.Parse(table);

        Assert.Equal(new[] { "SKU-1", "SKU-3" }, parsed.Lines.Select(l => l.Sku).ToArray());
        var first = parsed.Lines[0];
        Assert.Equal(415_000, first.Price);
        Assert.Null(first.DiscountPrice);
        Assert.Equal(12, first.Stock);
        Assert.True(first.IsActive);
        Assert.Equal(2, first.RowNumber);
        Assert.Equal(0, parsed.Lines[1].DiscountPrice);
        Assert.False(parsed.Lines[1].IsActive);
        Assert.Equal(3, parsed.Errors.Count); // قیمت نامعتبر، کد تکراری، موجودی اعشاری
        Assert.Contains("sku", parsed.Columns);
        Assert.DoesNotContain("purchase", parsed.Columns);
    }

    [Fact]
    public void Import_parse_requires_sku_and_an_editable_column()
    {
        Assert.Empty(ProductImportRules.Parse([new[] { "نام", "قیمت" }, new[] { "x", "1" }]).Lines);
        Assert.Single(ProductImportRules.Parse([new[] { "نام", "قیمت" }]).Errors);
        Assert.Single(ProductImportRules.Parse([new[] { "کد کالا", "نام" }, new[] { "A", "x" }]).Errors);
        Assert.Single(ProductImportRules.Parse([]).Errors);
    }

    [Theory]
    [InlineData("۴۱۵٬۰۰۰ تومان", true, 415000d)]
    [InlineData("1,250,000", true, 1250000d)]
    [InlineData("  ", true, null)]
    [InlineData("12a", false, null)]
    [InlineData("۱۲٫۵", true, 12.5)]
    public void TryParseNumber_accepts_persian_and_separators(string text, bool ok, double? expected)
    {
        Assert.Equal(ok, SpreadsheetReader.TryParseNumber(text, out var value));
        Assert.Equal((decimal?)expected, value);
    }

    [Fact]
    public void Csv_with_bom_quotes_and_semicolons_is_read()
    {
        var csv = "\uFEFFکد کالا;قیمت;توضیح\r\nA-1;\"1,250\";\"متن \"\"نقل‌قول\"\"\"\r\n\r\nB-2;300;\n";
        var rows = SpreadsheetReader.ReadCsv(new MemoryStream(Encoding.UTF8.GetBytes(csv)));

        Assert.Equal(3, rows.Count);
        Assert.Equal("کد کالا", rows[0][0]);
        Assert.Equal(new[] { "A-1", "1,250", "متن \"نقل‌قول\"" }, rows[1]);
        Assert.Equal("B-2", rows[2][0]);
    }

    [Fact]
    public void Panel_xlsx_export_round_trips_through_the_import()
    {
        // همان فایلی که «دریافت فایل نمونه» می‌دهد باید بدون تغییر دوباره خوانده شود.
        var table = new ExportTable("ورود محصولات", "کد کالا", "نام", "قیمت", "قیمت با تخفیف", "موجودی", "وضعیت");
        table.Add("A-1", "شامپو", 415_000m, null, 12, "فعال");
        table.Add("B-2", "کرم <ویژه> & نرم", 99_500m, 89_000m, 0, "غیرفعال");

        var rows = SpreadsheetReader.Read(new MemoryStream(Exporter.Xlsx(table)), "products.xlsx");
        var parsed = ProductImportRules.Parse(rows);

        Assert.Empty(parsed.Errors);
        Assert.Equal(2, parsed.Lines.Count);
        Assert.Equal(new ProductImportLine(2, "A-1", 415_000, null, 12, null, true), parsed.Lines[0]);
        Assert.Equal(new ProductImportLine(3, "B-2", 99_500, 89_000, 0, null, false), parsed.Lines[1]);
        Assert.Equal("کرم <ویژه> & نرم", rows[2][1]);
    }

    [Fact]
    public void Unknown_or_broken_files_raise_a_friendly_error()
    {
        Assert.Throws<InvalidDataException>(() => SpreadsheetReader.Read(new MemoryStream([1, 2, 3]), "x.xlsx"));
        Assert.Throws<InvalidDataException>(() => SpreadsheetReader.Read(new MemoryStream([1, 2, 3]), "x.xls"));
    }

    // ── تاریخ و ساعت حراج ─────────────────────────────────────────────────

    [Fact]
    public void Persian_date_time_parses_and_formats_in_iran_time()
    {
        var day = PersianDateInput.Parse("1404/08/10")!.Value;

        Assert.Equal(IranClock.At(day, new TimeOnly(18, 30)), PersianDateInput.ParseDateTime("1404/08/10 18:30"));
        Assert.Equal(IranClock.At(day, new TimeOnly(18, 30)), PersianDateInput.ParseDateTime("۱۴۰۴/۰۸/۱۰ ۱۸:۳۰"));
        Assert.Equal(IranClock.At(day, TimeOnly.MinValue), PersianDateInput.ParseDateTime("1404/08/10"));
        Assert.Equal(IranClock.At(day, new TimeOnly(23, 59, 59)), PersianDateInput.ParseDateTime("1404/08/10", new TimeOnly(23, 59, 59)));
        Assert.Null(PersianDateInput.ParseDateTime("1404/08/10 25:00"));
        Assert.Null(PersianDateInput.ParseDateTime("نامعتبر"));
        Assert.Null(PersianDateInput.ParseDateTime(" "));
        Assert.Equal("1404/08/10 18:30", PersianDateInput.FormatDateTime(PersianDateInput.ParseDateTime("1404/08/10 18:30")));
        Assert.Equal("", PersianDateInput.FormatDateTime(null));
    }
}
