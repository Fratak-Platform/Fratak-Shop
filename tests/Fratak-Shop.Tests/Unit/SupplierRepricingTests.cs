using Fratak_Shop.Core.Services.Suppliers;
using Fratak_Shop.Data.Entities.Products;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های «قیمت‌گذاری دوباره» بعد از تغییر قیمت خرید تأمین‌کننده (دور ۱۰):
/// فرمول هر روش، قانون‌های ایمنی (کف سود، پایین نیامدن قیمت با افزایش خرید، حذف تخفیف نامعتبر)، گرد کردن،
/// تصمیم خودکار/پیشنهاد و سقف افزایش. همه خالص و بدون دیتابیس.
/// </summary>
[Trait("Category", "Unit")]
public class SupplierRepricingTests
{
    private static SupplierRepricePolicy Policy(SupplierRepriceMode mode, bool onDecrease = false, int markupPercent = 25, decimal markupAmount = 100_000, int minMargin = 5, decimal rounding = 1000, int autoMode = 1, int maxJump = 50)
        => new(autoMode, (int)mode, markupPercent, markupAmount, minMargin, rounding, onDecrease, maxJump);

    // ── فرمول هر روش (خرید ۴۰۰→۴۱۵ هزار، فروش ۵۰۰ هزار) ──

    [Theory]
    [InlineData(SupplierRepriceMode.KeepMarginPercent, 519_000)] // ۵۱۸٬۷۵۰ ← گرد به بالا
    [InlineData(SupplierRepriceMode.KeepProfitAmount, 515_000)]
    [InlineData(SupplierRepriceMode.HigherOfBoth, 519_000)]
    [InlineData(SupplierRepriceMode.FixedMarkupPercent, 519_000)] // ۴۱۵ × ۱٫۲۵
    [InlineData(SupplierRepriceMode.FixedMarkupAmount, 515_000)]  // ۴۱۵ + ۱۰۰
    [InlineData(SupplierRepriceMode.PurchaseOnly, 500_000)]       // کف ۴۳۵٬۷۵۰ کمتر از قیمت فعلی است
    public void Each_mode_follows_its_formula(SupplierRepriceMode mode, int expected)
    {
        var result = SupplierRepricing.Calculate(400_000, 415_000, 500_000, null, Policy(mode));
        Assert.Equal(expected, result.Price);
        Assert.Null(result.DiscountPrice);
        Assert.Equal(expected != 500_000, result.Changed);
    }

    [Fact]
    public void Increase_never_lowers_the_sale_price()
    {
        // سود فعلی ۱۰۰٪ است؛ «سود ثابت ۲۵٪» نباید قیمت را از ۸۰۰ به ۵۱۲٬۵۰۰ پایین بیاورد.
        var result = SupplierRepricing.Calculate(400_000, 410_000, 800_000, null, Policy(SupplierRepriceMode.FixedMarkupPercent));
        Assert.Equal(800_000, result.Price);
        Assert.False(result.Changed);
    }

    [Fact]
    public void Minimum_margin_guard_applies_even_when_only_the_purchase_price_changes()
    {
        // خرید ۴۹۰ با کف ۵٪ = ۵۱۴٬۵۰۰ ← ۵۱۵٬۰۰۰؛ با قیمت قدیم ۵۰۰ هزار تقریباً بی‌سود بود.
        var result = SupplierRepricing.Calculate(400_000, 490_000, 500_000, null, Policy(SupplierRepriceMode.PurchaseOnly));
        Assert.Equal(515_000, result.Price);
        Assert.True(result.Changed);
    }

    [Fact]
    public void Unknown_old_cost_still_respects_the_floor()
    {
        var result = SupplierRepricing.Calculate(null, 415_000, 300_000, null, Policy(SupplierRepriceMode.KeepMarginPercent));
        Assert.Equal(436_000, result.Price); // ۴۱۵ × ۱٫۰۵ = ۴۳۵٬۷۵۰ ← گرد
    }

    // ── کاهش قیمت خرید ──

    [Fact]
    public void Decrease_keeps_the_price_by_default()
    {
        var result = SupplierRepricing.Calculate(400_000, 380_000, 500_000, 450_000, Policy(SupplierRepriceMode.KeepMarginPercent));
        Assert.Equal(500_000, result.Price);
        Assert.Equal(450_000, result.DiscountPrice);
        Assert.False(result.Changed);
    }

    [Fact]
    public void Decrease_lowers_the_price_when_enabled()
    {
        var result = SupplierRepricing.Calculate(400_000, 380_000, 500_000, null, Policy(SupplierRepriceMode.KeepMarginPercent, onDecrease: true));
        Assert.Equal(475_000, result.Price);
        Assert.True(result.Changed);
    }

    // ── تخفیف ──

    [Fact]
    public void Discount_moves_with_the_same_formula()
    {
        var result = SupplierRepricing.Calculate(400_000, 415_000, 500_000, 450_000, Policy(SupplierRepriceMode.KeepMarginPercent));
        Assert.Equal(519_000, result.Price);
        Assert.Equal(467_000, result.DiscountPrice); // ۴۵۰ × ۴۱۵/۴۰۰ = ۴۶۶٬۸۷۵ ← گرد
    }

    [Fact]
    public void Discount_reaching_the_price_is_removed()
    {
        // کف ۵۰۴٬۰۰۰ هم قیمت و هم تخفیف را به یک عدد می‌رساند ← تخفیف بی‌معناست و حذف می‌شود.
        var result = SupplierRepricing.Calculate(400_000, 480_000, 500_000, 420_000, Policy(SupplierRepriceMode.PurchaseOnly));
        Assert.Equal(504_000, result.Price);
        Assert.Null(result.DiscountPrice);
    }

    [Fact]
    public void Invalid_input_changes_nothing()
    {
        var result = SupplierRepricing.Calculate(400_000, 0, 500_000, null, SupplierRepricePolicy.Default);
        Assert.False(result.Changed);
        Assert.Equal(500_000, result.Price);
    }

    [Theory]
    [InlineData(518_750, 1000, 519_000)]
    [InlineData(519_000, 1000, 519_000)]
    [InlineData(1.2, 0, 2)]
    [InlineData(101, 50, 150)]
    public void Rounding_goes_up_to_the_step(decimal value, decimal step, decimal expected)
        => Assert.Equal(expected, SupplierRepricing.RoundUp(value, step));

    [Fact]
    public void Unknown_mode_falls_back_to_keeping_the_margin()
    {
        var result = SupplierRepricing.Calculate(400_000, 415_000, 500_000, null, Policy((SupplierRepriceMode)99));
        Assert.Equal(519_000, result.Price);
    }

    // ── خودکار یا پیشنهاد ──

    [Theory]
    [InlineData(1, 400_000, 415_000, true)]
    [InlineData(1, 400_000, 380_000, false)]
    [InlineData(2, 400_000, 380_000, true)]
    [InlineData(0, 400_000, 415_000, false)]
    [InlineData(1, null, 415_000, false)]  // قیمت قبلی نامعلوم ← کارمند تصمیم می‌گیرد
    [InlineData(2, 400_000, 400_000, false)]
    public void Automatic_depends_on_the_mode(int autoMode, int? oldCost, int newCost, bool expected)
        => Assert.Equal(expected, Policy(SupplierRepriceMode.KeepMarginPercent, autoMode: autoMode).IsAutomatic(oldCost, newCost));

    [Theory]
    [InlineData(50, 400_000, 700_000, true)]
    [InlineData(50, 400_000, 600_000, false)] // دقیقاً ۵۰٪ از سقف بیشتر نیست
    [InlineData(0, 400_000, 4_000_000, false)]
    public void Jump_cap(int maxJump, int oldCost, int newCost, bool expected)
        => Assert.Equal(expected, Policy(SupplierRepriceMode.KeepMarginPercent, maxJump: maxJump).ExceedsJump(oldCost, newCost));

    [Fact]
    public void Rules_auto_apply_a_normal_increase()
    {
        var policy = new SupplierSyncPolicy(true, 20, 10, SupplierRepricePolicy.Default);
        var decisions = SupplierSyncRules.Evaluate(new SupplierSnapshot(SupplierItemStatus.InStock, 415_000m, 50), new SupplierProductView(5, 400_000m, false, 0), policy);
        var price = Assert.Single(decisions, d => d.Kind == SupplierChangeKind.PriceChange);
        Assert.Equal(SupplierDecisionAction.AutoApply, price.Action);
        Assert.Equal(415_000m, price.NewValue);
    }

    [Fact]
    public void Rules_pause_the_product_on_a_huge_jump()
    {
        var policy = new SupplierSyncPolicy(true, 20, 10, SupplierRepricePolicy.Default);
        var decisions = SupplierSyncRules.Evaluate(new SupplierSnapshot(SupplierItemStatus.InStock, 700_000m, 50), new SupplierProductView(5, 400_000m, false, 0), policy);

        Assert.Equal(SupplierDecisionAction.Propose, Assert.Single(decisions, d => d.Kind == SupplierChangeKind.PriceChange).Action);
        var stop = Assert.Single(decisions, d => d.Kind == SupplierChangeKind.OutOfStock);
        Assert.Equal(SupplierDecisionAction.AutoApply, stop.Action);
        Assert.Equal(0m, stop.NewValue);
        // هر نوع فقط یک تصمیم؛ پیشنهاد افزایش موجودی کنار رفته است.
        Assert.Equal(decisions.Count, decisions.Select(d => d.Kind).Distinct().Count());
        Assert.Equal(SupplierDecisionAction.Supersede, Assert.Single(decisions, d => d.Kind == SupplierChangeKind.StockIncrease).Action);
    }

    [Fact]
    public void Rules_without_a_policy_keep_proposing()
    {
        var decisions = SupplierSyncRules.Evaluate(new SupplierSnapshot(SupplierItemStatus.InStock, 415_000m, 50), new SupplierProductView(5, 400_000m, false, 0), new SupplierSyncPolicy(true, 20, 10));
        Assert.Equal(SupplierDecisionAction.Propose, Assert.Single(decisions, d => d.Kind == SupplierChangeKind.PriceChange).Action);
    }
}
