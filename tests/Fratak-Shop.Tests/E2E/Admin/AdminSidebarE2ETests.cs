using Fratak_Shop.Tests.E2E.Infrastructure;

namespace Fratak_Shop.Tests.E2E.Admin;

/// <summary>
/// دور ۳۱ (#۷) – منوی کناری پنل از HTML واقعی صفحه:
/// <list type="bullet">
/// <item>فقط گروهی که صفحهٔ فعلی در آن است باز است (details[open])؛ همهٔ گروه‌های دیگر بسته‌اند.</item>
/// <item>ردیف همان صفحه آبی است (is-active) و فقط یک ردیف آبی است (به‌جز سنجاق‌ها).</item>
/// <item>زیرصفحه‌ها (مثل «افزودن/ویرایش محصول») گروه و ردیف والد را باز و آبی می‌کنند.</item>
/// <item>رفتن به صفحهٔ گروه دیگر ← گروه قبلی بسته و گروه تازه باز می‌شود. داشبورد ← همهٔ گروه‌ها بسته.</item>
/// </list>
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Round31")]
public class AdminSidebarE2ETests(ShopApp app)
{
    private const string SmartGroup = "هوشمند (خودکار)";
    private const string ProductsGroup = "محصولات و انبار";

    /// <summary>گروه‌های باز و ردیف‌های آبی منو (بخش سنجاق‌ها حساب نمی‌شود، چون details نیست).</summary>
    private static (List<string> Open, List<string> Active) Nav(HtmlPage page)
    {
        var open = page.All(".a-nav details[data-nav-group][open]").Select(d => d.GetAttribute("data-nav-group") ?? "").ToList();
        var active = page.All(".a-nav details[data-nav-group] .a-nav-link.is-active").Select(a => a.GetAttribute("href") ?? "").ToList();
        return (open, active);
    }

    [SqlFact]
    public async Task Opening_a_page_opens_only_its_group_and_highlights_its_row()
    {
        using var owner = app.CreateClient();
        await owner.LoginAsync(ShopApp.OwnerPhone);

        // «هوشمند (خودکار)» ← «ویدیوهای پیشنهادی کالا»
        var videos = Nav(await owner.GetPageAsync("/admin/smart/videos"));
        Assert.Equal(new[] { SmartGroup }, videos.Open);
        Assert.Equal(new[] { "/admin/smart/videos" }, videos.Active);

        // بعد «محصولات و انبار» ← «فهرست محصولات»: گروه هوشمند بسته و گروه محصولات باز می‌شود.
        var products = Nav(await owner.GetPageAsync("/admin/products"));
        Assert.Equal(new[] { ProductsGroup }, products.Open);
        Assert.Equal(new[] { "/admin/products" }, products.Active);
        Assert.DoesNotContain(SmartGroup, products.Open);
    }

    [SqlFact]
    public async Task Sub_pages_open_the_parent_group_and_the_most_specific_row()
    {
        using var owner = app.CreateClient();
        await owner.LoginAsync(ShopApp.OwnerPhone);

        // صفحهٔ افزودن محصول در منو ردیف ندارد ← ردیف والد «فهرست محصولات».
        var edit = Nav(await owner.GetPageAsync("/admin/products/edit"));
        Assert.Equal(new[] { ProductsGroup }, edit.Open);
        Assert.Equal(new[] { "/admin/products" }, edit.Active);

        // «موجودی انبار» ردیف خودش را دارد ← فقط همان آبی است، نه «فهرست محصولات».
        var inventory = Nav(await owner.GetPageAsync("/admin/products/inventory"));
        Assert.Equal(new[] { ProductsGroup }, inventory.Open);
        Assert.Equal(new[] { "/admin/products/inventory" }, inventory.Active);
    }

    [SqlFact]
    public async Task Dashboard_keeps_every_group_closed()
    {
        using var owner = app.CreateClient();
        await owner.LoginAsync(ShopApp.OwnerPhone);

        var page = await owner.GetPageAsync("/admin");
        var (open, active) = Nav(page);

        Assert.Empty(open);
        Assert.Empty(active);
        Assert.NotEmpty(page.All(".a-nav details[data-nav-group]")); // گروه‌ها هستند، فقط بسته‌اند
    }
}
