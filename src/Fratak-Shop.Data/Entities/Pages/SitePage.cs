namespace Fratak_Shop.Data.Entities.Pages;

/// <summary>
/// Complete list of site pages. Used by <see cref="PageSeo"/> (static-page SEO)
/// and <see cref="PageAlert"/> (positioned page alerts) so the SEO specialist
/// can address every page of the shop by name.
/// </summary>
public enum SitePage
{
    None = 0,
    Home = 1,             // صفحه اصلی / ایندکس
    Products = 2,         // لیست محصولات
    ProductDetails = 3,   // صفحه محصول
    Categories = 4,       // لیست دسته‌بندی‌ها
    CategoryDetails = 5,  // صفحه دسته‌بندی
    Blog = 6,             // لیست مقالات
    BlogPostDetails = 7,  // صفحه مقاله
    Cart = 8,             // سبد خرید
    Checkout = 9,         // تسویه حساب
    OrderTracking = 10,   // پیگیری سفارش
    Search = 11,          // نتایج جستجو
    ContactUs = 12,       // تماس با ما
    AboutUs = 13,         // درباره ما
    Faq = 14,             // پرسش‌های متداول
    Warranty = 15,        // گارانتی و خدمات پس از فروش
    Terms = 16,           // قوانین و مقررات
    Privacy = 17,         // حریم خصوصی
    Login = 18,           // ورود
    Register = 19,        // ثبت‌نام
    UserProfile = 20,     // پروفایل کاربر
    UserAddresses = 21,   // آدرس‌های کاربر
    UserOrders = 22,      // سفارش‌های کاربر
    UserFavorites = 23,   // علاقه‌مندی‌های کاربر
    NotFound = 24,        // صفحه یافت نشد
    ReturnPolicy = 25,    // شرایط بازگشت کالا
    ShippingGuide = 26,   // شیوه‌های ارسال
    PaymentGuide = 27,    // شیوه‌های پرداخت
    UserWallet = 28,      // کیف پول کاربر
    Compare = 29,         // مقایسه محصولات
    ContentPage = 30,     // صفحات محتوایی دیگر
}
