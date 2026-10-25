using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Seeders;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Admin.Manage;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های منطق خالص ابزارهای رابط کاربری پنل (مرحلهٔ هفتم – P2): کلید راهنما، پوشش راهنمای پیش‌فرض،
/// اعتبارسنجی آدرس سنجاق/فیلتر و تشخیص فیلدهای مبلغ در جدول‌های مدیریتی.
/// </summary>
public class AdminUxTests
{
    [Fact]
    public void HelpKeys_GoFromSpecificToGeneral_AndReplaceIds()
    {
        var keys = AdminHelpService.ResolveKeys("/Admin/Orders/5f0e3c1a-1b2c-4d5e-8f90-a1b2c3d4e5f6/?tab=items");
        Assert.Equal(new[] { "/admin/orders/*", "/admin/orders", "/admin" }, keys);

        var manage = AdminHelpService.ResolveKeys("/admin/manage/coupons/edit/5f0e3c1a-1b2c-4d5e-8f90-a1b2c3d4e5f6");
        Assert.Equal(new[] { "/admin/manage/coupons/edit/*", "/admin/manage/coupons/edit", "/admin/manage/coupons", "/admin/manage", "/admin" }, manage);
    }

    [Theory]
    [InlineData("")]
    [InlineData("/")]
    [InlineData("/profile")]
    public void HelpKeys_OutsideAdmin_FallBackToDashboard(string path)
    {
        Assert.Equal(new[] { "/admin" }, AdminHelpService.ResolveKeys(path));
    }

    [Fact]
    public void HelpDefaults_HaveUniqueValidPaths_AndSafeBodies()
    {
        var paths = AdminHelpDefaults.All.Select(h => h.Path).ToList();
        Assert.Equal(paths.Count, paths.Distinct().Count());
        foreach (var help in AdminHelpDefaults.All)
        {
            Assert.Matches("^/admin(/[a-z0-9*-]+)*$", help.Path);
            Assert.False(string.IsNullOrWhiteSpace(help.Title));
            Assert.InRange(help.Title.Length, 1, 150);
            Assert.InRange(help.Body.Length, 20, 20000);
            Assert.DoesNotContain("<script", help.Body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("javascript:", help.Body, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>هر صفحهٔ منو باید راهنمای اختصاصی (یا راهنمای بخش والدش) داشته باشد، نه فقط راهنمای داشبورد.</summary>
    [Fact]
    public void HelpDefaults_CoverEveryNavigationPage()
    {
        var defaults = AdminHelpDefaults.All.Select(h => h.Path).ToHashSet();
        foreach (var item in AdminNav.Groups.SelectMany(g => g.Items))
        {
            var resolved = AdminHelpService.ResolveKeys(item.Url).FirstOrDefault(defaults.Contains);
            Assert.NotNull(resolved);
            if (item.Url != "/admin")
                Assert.True(resolved != "/admin", $"راهنمای «{item.Title}» ({item.Url}) تعریف نشده است.");
        }
    }

    [Theory]
    [InlineData("/admin/orders?status=Paid", true)]
    [InlineData("/admin", true)]
    [InlineData("https://evil.example/admin", false)]
    [InlineData("//evil.example/admin", false)]
    [InlineData("/profile", false)]
    [InlineData("/admin\\..\\x", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void Shortcuts_AcceptOnlyInternalAdminUrls(string? url, bool expected)
    {
        Assert.Equal(expected, AdminShortcutService.IsAdminUrl(url));
    }

    [Theory]
    [InlineData("/Admin/Orders/?x=1", "/admin/orders")]
    [InlineData("/admin/", "/admin")]
    [InlineData("/admin/finance#top", "/admin/finance")]
    public void Shortcuts_NormalizeFilterPath(string path, string expected)
    {
        Assert.Equal(expected, AdminShortcutService.NormalizePath(path));
    }

    [Theory]
    [InlineData("MinOrderAmount", MKind.Decimal, true)]
    [InlineData("Cost", MKind.Decimal, true)]
    [InlineData("FreeShippingThreshold", MKind.Decimal, true)]
    [InlineData("Value", MKind.Decimal, false)]
    [InlineData("PointsMultiplier", MKind.Decimal, false)]
    [InlineData("MinOrderAmount", MKind.Int, false)]
    public void ManageFields_DetectMoneyByName(string name, MKind kind, bool expected)
    {
        Assert.Equal(expected, new MField(name, "x", kind).IsMoney);
    }

    /// <summary>جدول‌های مدیریتی: همهٔ فیلدهای «حداقل/حداکثر مبلغ» و هزینه‌ها با قالب مبلغ نمایش داده شوند.</summary>
    [Fact]
    public void ManageTables_AmountFields_AreMoney()
    {
        var amountFields = ManageRegistry.All.SelectMany(d => d.Fields)
            .Where(f => f.Kind == MKind.Decimal && f.Name.Contains("Amount", StringComparison.Ordinal)).ToList();
        Assert.NotEmpty(amountFields);
        Assert.All(amountFields, f => Assert.True(f.IsMoney, f.Name));
    }
}
