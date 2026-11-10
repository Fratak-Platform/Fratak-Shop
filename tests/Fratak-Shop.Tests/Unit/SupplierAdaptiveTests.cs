using Fratak_Shop.Core.Services.Suppliers;
using Fratak_Shop.Data.Entities.Products;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// دور ۲۳: آزمون‌های «بررسی تطبیقی تأمین‌کننده» (مورد ۷۰) و «اعتبار تأمین‌کننده» (مورد ۷۴).
/// از بهترین حالت (کالای پرتغییر همیشه بررسی می‌شود) تا بدترین (صفحهٔ خراب، دادهٔ ناکافی).
/// </summary>
[Trait("Category", "Unit")]
public class SupplierAdaptiveTests
{
    private static readonly DateTimeOffset Now = new(2026, 10, 22, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public void Never_checked_product_is_always_checked()
    {
        Assert.False(SupplierAdaptiveSync.Skip(SupplierItemStatus.Unknown, null, 0, null, 5, Now));
        Assert.Equal(TimeSpan.Zero, SupplierAdaptiveSync.MinInterval(SupplierItemStatus.NotFound, null, 9, null, 0, Now));
    }

    [Fact]
    public void Selling_product_with_recent_change_is_checked_every_run()
    {
        Assert.False(SupplierAdaptiveSync.Skip(SupplierItemStatus.InStock, Now.AddMinutes(-30), 0, Now.AddDays(-2), 4, Now));
    }

    [Fact]
    public void Stable_selling_product_waits_six_hours()
    {
        Assert.True(SupplierAdaptiveSync.Skip(SupplierItemStatus.InStock, Now.AddHours(-2), 0, Now.AddDays(-30), 4, Now));
        Assert.True(SupplierAdaptiveSync.Skip(SupplierItemStatus.InStock, Now.AddHours(-2), 0, null, 4, Now));
        Assert.False(SupplierAdaptiveSync.Skip(SupplierItemStatus.InStock, Now.AddHours(-7), 0, null, 4, Now));
    }

    [Fact]
    public void Broken_or_missing_pages_back_off()
    {
        Assert.Equal(TimeSpan.FromHours(12), SupplierAdaptiveSync.MinInterval(SupplierItemStatus.InStock, Now.AddHours(-1), 3, Now, 4, Now));
        Assert.Equal(TimeSpan.FromHours(24), SupplierAdaptiveSync.MinInterval(SupplierItemStatus.NotFound, Now.AddHours(-1), 0, Now, 0, Now));
        Assert.True(SupplierAdaptiveSync.Skip(SupplierItemStatus.NotFound, Now.AddHours(-23), 0, null, 0, Now));
        Assert.False(SupplierAdaptiveSync.Skip(SupplierItemStatus.NotFound, Now.AddHours(-25), 0, null, 0, Now));
    }

    [Fact]
    public void Out_of_stock_everywhere_is_checked_twice_a_day()
    {
        Assert.Equal(TimeSpan.FromHours(12), SupplierAdaptiveSync.MinInterval(SupplierItemStatus.OutOfStock, Now.AddHours(-1), 0, null, 0, Now));
        // منبع موجود شد ولی ما هنوز موجودی نداریم ← همیشه بررسی (فرصت فروش).
        Assert.Equal(TimeSpan.Zero, SupplierAdaptiveSync.MinInterval(SupplierItemStatus.InStock, Now.AddHours(-1), 0, null, 0, Now));
    }

    [Fact]
    public void Reliability_rewards_available_problem_free_stable_suppliers()
    {
        var perfect = SupplierReliability.Score(10, 0, 0, 0, 0);
        var flaky = SupplierReliability.Score(4, 2, 2, 2, 10);
        Assert.Equal(100, perfect);
        Assert.True(flaky < 60, $"flaky = {flaky}");
        Assert.Equal("خوب", SupplierReliability.Verdict(perfect, 10));
        Assert.Equal("ضعیف", SupplierReliability.Verdict(flaky, 10));
    }

    [Fact]
    public void Reliability_never_judges_with_too_little_data()
    {
        Assert.Equal("دادهٔ کم", SupplierReliability.Verdict(100, SupplierReliability.MinProducts - 1));
        Assert.Equal(0, SupplierReliability.Score(0, 0, 0, 0, 0));
        // تغییر قیمت خیلی زیاد امتیاز را منفی نمی‌کند (سقف دارد).
        Assert.InRange(SupplierReliability.Score(0, 0, 5, 5, 100000), 0, 100);
    }
}
