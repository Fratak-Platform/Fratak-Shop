using Fratak_Shop.Core.Services.Pricing;
using Fratak_Shop.Core.Services.Suppliers;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های واحد کف سود دسته (پیشنهاد ۶۹، دور ۲۳): ارث از والد، کف سراسری همیشه حداقل، دادهٔ خراب (حلقه، عدد منفی/خیلی بزرگ)
/// و اثر واقعی روی قیمت‌گذاری دوبارهٔ تأمین‌کننده و پیشنهاد قیمت.
/// </summary>
[Trait("Category", "Unit")]
public class CategoryMarginTests
{
    private static readonly Guid Mobile = Guid.NewGuid();
    private static readonly Guid Samsung = Guid.NewGuid();
    private static readonly Guid Accessories = Guid.NewGuid();
    private static readonly Guid Cables = Guid.NewGuid();

    private static Dictionary<Guid, CategoryMarginNode> Tree() => new()
    {
        [Mobile] = new(null, 8),
        [Samsung] = new(Mobile, null),     // ارث از «موبایل»
        [Accessories] = new(null, 3),      // کمتر از سراسری ← بی‌اثر
        [Cables] = new(Accessories, 12),   // بیشتر از والد ← خودش
    };

    [Fact]
    public void Child_inherits_the_parent_floor_and_the_highest_floor_wins()
    {
        var tree = Tree();
        Assert.Equal(8, CategoryMargins.Effective(5, Mobile, tree));
        Assert.Equal(8, CategoryMargins.Effective(5, Samsung, tree));
        Assert.Equal(12, CategoryMargins.Effective(5, Cables, tree));
        Assert.Equal(10, CategoryMargins.Effective(10, Samsung, tree)); // سراسری بالاتر از دسته
    }

    [Fact]
    public void Category_floor_never_lowers_the_global_floor()
    {
        Assert.Equal(5, CategoryMargins.Effective(5, Accessories, Tree()));
    }

    [Fact]
    public void Unknown_or_missing_category_uses_the_global_floor()
    {
        Assert.Equal(5, CategoryMargins.Effective(5, null, Tree()));
        Assert.Equal(5, CategoryMargins.Effective(5, Guid.NewGuid(), Tree()));
        Assert.Equal(5, CategoryMargins.Effective(5, Mobile, new Dictionary<Guid, CategoryMarginNode>()));
    }

    [Fact]
    public void Broken_data_is_safe_cycles_and_out_of_range_values()
    {
        var a = Guid.NewGuid();
        var b = Guid.NewGuid();
        var cycle = new Dictionary<Guid, CategoryMarginNode> { [a] = new(b, null), [b] = new(a, 7) };
        Assert.Equal(7, CategoryMargins.Effective(5, a, cycle)); // حلقه ← بدون گیر کردن

        var self = Guid.NewGuid();
        Assert.Equal(5, CategoryMargins.Effective(5, self, new Dictionary<Guid, CategoryMarginNode> { [self] = new(self, -40) }));

        var huge = Guid.NewGuid();
        Assert.Equal(CategoryMargins.Max, CategoryMargins.Effective(5, huge, new Dictionary<Guid, CategoryMarginNode> { [huge] = new(null, 100_000) }));
        Assert.Equal(0, CategoryMargins.Effective(-20, null, Tree()));
    }

    [Fact]
    public void Supplier_reprice_respects_the_category_floor()
    {
        // خرید از ۱۰۰ هزار به ۲۰۰ هزار، سود ثابت ۰٪ ← کف سراسری ۵٪ = ۲۱۰ هزار؛ کف دستهٔ ۲۰٪ = ۲۴۰ هزار.
        var policy = SupplierRepricePolicy.Default with { Mode = (int)SupplierRepriceMode.FixedMarkupPercent, MarkupPercent = 0, MinMarginPercent = 5 };
        var global = SupplierRepricing.Calculate(100_000m, 200_000m, 100_000m, null, policy);
        var category = SupplierRepricing.Calculate(100_000m, 200_000m, 100_000m, null,
            policy with { MinMarginPercent = CategoryMargins.Effective(5, Samsung, new Dictionary<Guid, CategoryMarginNode> { [Samsung] = new(null, 20) }) });
        Assert.Equal(210_000m, global.Price);
        Assert.Equal(240_000m, category.Price);
    }

    [Fact]
    public void Price_suggestion_minimum_rises_with_the_category_floor()
    {
        var costs = new PricingCosts(0, 0, 0, 0, 0, 0, 10, 5, 2, 1000);
        var normal = PriceAdvisor.Suggest(100_000m, 0, costs, Array.Empty<CompetitorOffer>());
        var strict = PriceAdvisor.Suggest(100_000m, 0, costs with { MinMarginPercent = 25 }, Array.Empty<CompetitorOffer>());
        Assert.Equal(105_000m, normal.MinPrice);
        Assert.Equal(125_000m, strict.MinPrice);
        Assert.True(strict.TargetPrice >= strict.MinPrice); // هدف هیچ‌وقت زیر کف نیست
    }
}
