using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Pages;

namespace Fratak_Shop.Core.DTOs.Content.Query;

public class ContentPageDto : BaseDto
{
    public SitePage Page { get; set; }
    public string Title { get; set; } = null!;
    public string? Summary { get; set; }
    public string? Content { get; set; }
    public bool IsPublished { get; set; }
    public int DisplayOrder { get; set; }

    public string PageTitle => Page.GetTitle();
}

/// <summary>عنوان فارسی صفحه‌های سایت برای پنل مدیریت و منوها.</summary>
public static class SitePageExtensions
{
    public static string GetTitle(this SitePage page) => page switch
    {
        SitePage.Home => "صفحه اصلی",
        SitePage.Products => "لیست محصولات",
        SitePage.ProductDetails => "جزئیات محصول",
        SitePage.Categories => "لیست دسته‌بندی‌ها",
        SitePage.CategoryDetails => "جزئیات دسته‌بندی",
        SitePage.Blog => "لیست مقالات",
        SitePage.BlogPostDetails => "جزئیات مقاله",
        SitePage.Cart => "سبد خرید",
        SitePage.Checkout => "تسویه حساب",
        SitePage.OrderTracking => "پیگیری سفارش",
        SitePage.Search => "نتایج جستجو",
        SitePage.ContactUs => "تماس با ما",
        SitePage.AboutUs => "درباره ما",
        SitePage.Faq => "پرسش‌های متداول",
        SitePage.Warranty => "گارانتی و خدمات پس از فروش",
        SitePage.Terms => "قوانین و مقررات",
        SitePage.Privacy => "حریم خصوصی",
        SitePage.Login => "ورود",
        SitePage.Register => "ثبت‌نام",
        SitePage.UserProfile => "پروفایل کاربر",
        SitePage.UserAddresses => "آدرس‌های کاربر",
        SitePage.UserOrders => "سفارش‌های کاربر",
        SitePage.UserFavorites => "علاقه‌مندی‌ها",
        SitePage.NotFound => "صفحه یافت نشد",
        _ => "نامشخص",
    };

    /// <summary>صفحه‌هایی که محتوای قابل ویرایش دارند.</summary>
    public static readonly SitePage[] ContentPages =
    [
        SitePage.AboutUs,
        SitePage.ContactUs,
        SitePage.Faq,
        SitePage.Warranty,
        SitePage.Terms,
        SitePage.Privacy,
    ];
}
