using System.Text.Json;
using Fratak_Shop.Core.Services.Admin;

namespace Fratak_Shop.Web.Infrastructure.SeoEngine;

/// <summary>
/// کمک‌کار نمای «سئوی هوشمند» و «پیشنهاد هوشمند» (مرحلهٔ شانزدهم).
/// <para>Context: مقدار ویژگی <c>data-smart-context</c> روی فرم‌های ویرایش؛ smart.js از آن می‌فهمد این فرم مال کدام نوع صفحه است
/// و نام، متن، دسته و برند را از کدام فیلد بخواند.
/// مثال: Context("Product", id, new { name = "#p-name", body = "#p-desc" })
/// → <c>{"kind":"Product","id":"…","name":"#p-name","body":"#p-desc"}</c>.</para>
/// </summary>
public static class SmartUi
{
    /// <summary>مجوزهایی که دکمه‌های هوشمند را نشان می‌دهند (همان مجوزهای صفحهٔ /admin/seo/smart).</summary>
    public static readonly string[] Permissions =
    [
        AdminPermissions.ProductsManage, AdminPermissions.CatalogManage, AdminPermissions.BlogWrite,
        AdminPermissions.ContentManage, AdminPermissions.SeoManage,
    ];

    private static readonly JsonSerializerOptions Options = new(JsonSerializerDefaults.Web);

    /// <summary>JSON زمینهٔ فرم. id خالی = صفحهٔ تازه (هنوز ذخیره نشده).</summary>
    public static string Context(string kind, Guid? id, object? selectors = null)
    {
        var values = new Dictionary<string, object?> { ["kind"] = kind, ["id"] = id };
        if (selectors is not null)
            foreach (var property in selectors.GetType().GetProperties())
                values[property.Name] = property.GetValue(selectors);
        return JsonSerializer.Serialize(values, Options);
    }

    /// <summary>نوع صفحهٔ سئوی جدول‌های «مدیریت» (null = این جدول سئوی هوشمند ندارد).</summary>
    public static string? ManageKind(string key) => key switch
    {
        "categories" => "Category",
        "brands" => "Brand",
        "content-pages" => "ContentPage",
        "page-seo" => "StaticPage",
        _ => null,
    };
}
