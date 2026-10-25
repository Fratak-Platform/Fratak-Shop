using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Entities.Products;

namespace Fratak_Shop.Web.Infrastructure.Admin.Manage;

/// <summary>
/// یک نوع داده در «سطل بازیافت».
/// </summary>
/// <param name="Key">کلید در آدرس، مثلاً <c>products</c> یا کلید جدول مدیریت داده مثل <c>coupons</c>.</param>
/// <param name="LabelProperty">ویژگی متنی که به‌عنوان نام ردیف نشان داده می‌شود (مثلاً Name)؛ رشتهٔ خالی یعنی فقط شناسه.</param>
/// <param name="Permissions">مجوزهای لازم برای دیدن و بازگرداندن (همان مجوز مدیریت/حذف آن داده).</param>
/// <param name="EditUrl">آدرس ویرایش بعد از بازگردانی؛ <c>{id}</c> با شناسه جایگزین می‌شود.</param>
public sealed record TrashSource(string Key, string Title, Type Entity, string LabelProperty, string[] Permissions, string EditUrl, string Icon);

/// <summary>
/// فهرست داده‌هایی که حذف نرم دارند و در صفحهٔ «سطل بازیافت» (<c>/admin/trash</c>) قابل بازگرداندن‌اند:
/// محصولات، مقاله‌ها و همهٔ جدول‌های «مدیریت داده» که حذف دارند (<see cref="ManageRegistry"/>).
/// <para>
/// برای افزودن نوع جدید کافی است یک <see cref="TrashSource"/> به <see cref="Extra"/> اضافه شود؛ جدول‌های جدید ManageRegistry خودکار می‌آیند.
/// </para>
/// </summary>
public static class TrashRegistry
{
    private static readonly TrashSource[] Extra =
    [
        new("products", "محصولات", typeof(Product), nameof(Product.Name), [AdminPermissions.ProductsDelete], "/admin/products/edit/{id}", "Package"),
        new("blog", "مقاله‌ها", typeof(BlogPost), nameof(BlogPost.Title), [AdminPermissions.BlogPublish], "/admin/blog/edit/{id}", "Article"),
    ];

    public static readonly IReadOnlyList<TrashSource> All = Extra
        .Concat(ManageRegistry.All.Where(d => d.CanDelete).Select(d => new TrashSource(
            d.Key, d.Title, d.Entity, LabelOf(d), d.Permissions, $"/admin/manage/{d.Key}/edit/{{id}}", d.Icon)))
        .ToList();

    /// <summary>همهٔ مجوزهایی که حداقل یکی از نوع‌ها را باز می‌کنند (برای [AdminPermission] صفحه و منو).</summary>
    public static readonly string[] AnyPermission =
    [
        AdminPermissions.ProductsDelete, AdminPermissions.BlogPublish, AdminPermissions.CatalogManage, AdminPermissions.ContentManage,
        AdminPermissions.LoyaltyManage, AdminPermissions.MarketingManage, AdminPermissions.ProductsManage, AdminPermissions.ReturnsManage,
        AdminPermissions.SeoManage, AdminPermissions.ShippingManage, AdminPermissions.SuppliersManage, AdminPermissions.TicketsManage,
    ];

    public static TrashSource? Find(string? key) => All.FirstOrDefault(s => string.Equals(s.Key, key, StringComparison.OrdinalIgnoreCase));

    // اولین فیلد متنی فهرست (نام/عنوان/کد) که واقعاً ویژگی رشته‌ای موجودیت است.
    private static string LabelOf(MDef def)
    {
        foreach (var name in new[] { "Name", "Title", "Code", "Question", "FromPath", "Terms" })
            if (def.Entity.GetProperty(name)?.PropertyType == typeof(string))
                return name;
        var field = def.Fields.FirstOrDefault(f => f.Kind is MKind.Text or MKind.Slug && def.Entity.GetProperty(f.Name)?.PropertyType == typeof(string));
        return field?.Name ?? "";
    }
}
