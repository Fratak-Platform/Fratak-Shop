using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Entities.Brands;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Contents;
using Fratak_Shop.Data.Entities.Faqs;
using Fratak_Shop.Data.Entities.Locations;
using Fratak_Shop.Data.Entities.Loyalty;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Searches;
using Fratak_Shop.Data.Entities.SeoRedirects;
using Fratak_Shop.Data.Entities.Shipping;
using Fratak_Shop.Data.Entities.Shopping;
using Fratak_Shop.Data.Entities.Support;
using Fratak_Shop.Web.Services;
using PageAlertEntity = Fratak_Shop.Data.Entities.Pages.PageAlert;
using PageSeoEntity = Fratak_Shop.Data.Entities.Pages.PageSeo;
using P = Fratak_Shop.Core.Services.Admin.AdminPermissions;

namespace Fratak_Shop.Web.Infrastructure.Admin.Manage;

/// <summary>Tables managed by the generic admin engine. Each definition lists exactly the editable fields.</summary>
public static class ManageRegistry
{
    // ── Shared fields ──────────────────────────────────────────────────
    private static MField Active(bool inList = true) => new("IsActive", "فعال", MKind.Bool) { InList = inList };
    private static MField Order() => new("DisplayOrder", "ترتیب نمایش", MKind.Int) { InList = true, Help = "عدد کمتر = نمایش زودتر" };

    // ── Enum labels ────────────────────────────────────────────────────
    private static readonly Dictionary<string, string> BannerPositions = new() { ["HeroSlider"] = "اسلایدر اصلی", ["WideBanner"] = "بنر عریض", ["PromoCard"] = "بنر دوتایی", ["CategoryHeader"] = "سربرگ صفحهٔ دسته" };
    private static readonly Dictionary<string, string> ShippingTypes = new() { ["Post"] = "پست", ["Courier"] = "پیک", ["Express"] = "ارسال فوری", ["StorePickup"] = "تحویل حضوری" };
    private static readonly Dictionary<string, string> PricingModes = new() { ["Fixed"] = "ثابت", ["ByLocation"] = "بر اساس مقصد (تعرفه‌ها)" };
    private static readonly Dictionary<string, string> VideoProviders = new() { ["Aparat"] = "آپارات", ["DirectUrl"] = "لینک مستقیم فایل", ["Upload"] = "فایل آپلودشده (مسیر)" };
    private static readonly Dictionary<string, string> MenuLocations = new() { ["HeaderMain"] = "منوی اصلی هدر", ["HeaderShortcuts"] = "میانبرهای هدر", ["Footer"] = "فوتر" };
    private static readonly Dictionary<string, string> MenuSources = new() { ["Manual"] = "دستی", ["ContentPages"] = "صفحات محتوایی (خودکار)" };
    private static readonly Dictionary<string, string> MenuItemTypes = new() { ["Link"] = "لینک", ["CategoriesMegaMenu"] = "مگامنوی دسته‌بندی‌ها" };
    private static readonly Dictionary<string, string> HomeSectionTypes = new()
    {
        ["HeroSlider"] = "اسلایدر اصلی", ["Categories"] = "دسته‌بندی‌ها", ["Deals"] = "پیشنهادهای ویژه", ["PromoBanners"] = "بنرهای تبلیغاتی",
        ["BestSellers"] = "پرفروش‌ترین‌ها", ["WideBanners"] = "بنرهای عریض", ["Newest"] = "جدیدترین‌ها", ["MostViewed"] = "پربازدیدترین‌ها",
        ["CategoryProducts"] = "محصولات یک دسته", ["Features"] = "مزایای فروشگاه", ["Brands"] = "برندها", ["LatestPosts"] = "آخرین مقاله‌ها",
        ["RecommendedForYou"] = "پیشنهاد برای شما", ["RecentlyViewed"] = "بازدیدهای اخیر",
    };
    private static readonly Dictionary<string, string> AlertPositions = new()
    {
        ["Top"] = "بالای صفحه", ["Bottom"] = "پایین صفحه", ["Left"] = "چپ", ["Right"] = "راست", ["Center"] = "وسط (پاپ‌آپ)",
        ["AboveContent"] = "بالای محتوا", ["BelowContent"] = "پایین محتوا", ["BelowMainImage"] = "زیر تصویر اصلی محصول",
    };
    private static readonly Dictionary<string, string> AlertTypes = new() { ["Info"] = "اطلاع‌رسانی", ["Success"] = "موفقیت", ["Warning"] = "هشدار", ["Error"] = "خطا" };
    private static readonly Dictionary<string, string> AlertAudiences = new() { ["Everyone"] = "همه", ["Guests"] = "مهمان‌ها", ["SignedIn"] = "کاربران واردشده" };
    private static readonly Dictionary<string, string> RedirectTypes = new() { ["MovedPermanently"] = "۳۰۱ دائمی", ["Found"] = "۳۰۲ موقت", ["TemporaryRedirect"] = "۳۰۷ موقت", ["PermanentRedirect"] = "۳۰۸ دائمی" };
    private static readonly Dictionary<string, string> LoyaltyActions = new()
    {
        ["Registration"] = "ثبت‌نام", ["CompleteProfile"] = "تکمیل پروفایل", ["FirstOrder"] = "اولین خرید", ["Purchase"] = "خرید",
        ["ProductReview"] = "ثبت نظر", ["Referral"] = "دعوت دوستان (خرید دوست)", ["ReferralSignup"] = "ثبت‌نام دوست دعوت‌شده", ["Birthday"] = "تولد", ["OrderFeedback"] = "نظرسنجی سفارش",
    };
    private static readonly Dictionary<string, string> RewardTypes = new() { ["WalletCredit"] = "اعتبار هدیه کیف پول", ["PercentageCoupon"] = "کوپن درصدی", ["FixedCoupon"] = "کوپن مبلغی", ["FreeShippingCoupon"] = "کوپن ارسال رایگان" };
    private static readonly Dictionary<string, string> CouponTypes = new() { ["Percentage"] = "درصدی", ["FixedAmount"] = "مبلغ ثابت", ["FreeShipping"] = "ارسال رایگان" };
    private static readonly Dictionary<string, string> ReminderChannels = new() { ["Notification"] = "اعلان سایت", ["Sms"] = "پیامک", ["Both"] = "هر دو" };
    private static readonly Dictionary<string, string> LocationTypes = new() { ["Country"] = "کشور", ["Province"] = "استان", ["City"] = "شهر" };
    private static readonly Dictionary<string, string> SitePages = new()
    {
        ["AllPages"] = "همه صفحات", ["Home"] = "صفحه اصلی", ["Products"] = "فهرست محصولات", ["ProductDetails"] = "صفحه محصول", ["Categories"] = "دسته‌بندی‌ها",
        ["CategoryDetails"] = "صفحه دسته", ["Blog"] = "وبلاگ", ["BlogPostDetails"] = "صفحه مقاله", ["Cart"] = "سبد خرید", ["Checkout"] = "تسویه حساب",
        ["CheckoutComplete"] = "پایان خرید", ["OrderTracking"] = "پیگیری سفارش", ["Search"] = "جست‌وجو", ["ContactUs"] = "تماس با ما", ["AboutUs"] = "درباره ما",
        ["Faq"] = "سؤالات متداول", ["Warranty"] = "گارانتی", ["Terms"] = "قوانین", ["Privacy"] = "حریم خصوصی", ["ReturnPolicy"] = "شرایط مرجوعی",
        ["ShippingGuide"] = "راهنمای ارسال", ["PaymentGuide"] = "راهنمای پرداخت", ["Login"] = "ورود", ["Register"] = "ثبت‌نام", ["Compare"] = "مقایسه",
        ["ContentPage"] = "صفحه محتوایی", ["UserProfile"] = "پروفایل", ["UserOrders"] = "سفارش‌های من", ["OrderDetails"] = "جزئیات سفارش",
        ["UserAddresses"] = "آدرس‌ها", ["UserFavorites"] = "علاقه‌مندی‌ها", ["UserWallet"] = "کیف پول", ["CustomerClub"] = "باشگاه مشتریان",
        ["Notifications"] = "اعلان‌ها", ["UserTickets"] = "تیکت‌ها", ["UserReturns"] = "مرجوعی‌ها", ["GiftCards"] = "کارت هدیه", ["Invite"] = "دعوت دوستان",
        ["UserAlerts"] = "اطلاع‌رسانی‌های من", ["NotFound"] = "صفحه ۴۰۴",
    };

    public static readonly MDef[] All =
    [
        // ── Catalog ────────────────────────────────────────────────────
        new()
        {
            Key = "categories", Title = "دسته‌بندی‌ها", Singular = "دسته‌بندی", Group = "کاتالوگ", Icon = "Stack", Entity = typeof(Category),
            Permissions = [P.CatalogManage], HasSeo = true, Search = ["Name", "Slug"],
            Fields =
            [
                new("ImageName", "تصویر", MKind.Image) { InList = true, Area = UploadArea.Categories },
                new("Name", "نام", MKind.Text) { Required = true, InList = true },
                new("Slug", "اسلاگ", MKind.Slug) { Ltr = true, InList = true, SlugFrom = "Name", Help = "خالی = از روی نام" },
                new("ParentId", "دسته والد", MKind.Ref) { RefType = typeof(Category), RefLabel = "Name", InList = true, Help = "خالی = دسته اصلی" },
                new("IconKey", "آیکن", MKind.Text)
                {
                    Ltr = true, Placeholder = "Headset",
                    Help = "نام آیکن (مثل DeviceMobile، Headset، Cpu، Plug، Lightning، Home، Gift)؛ خالی ← دستیار هوشمند از روی نوع دسته انتخاب می‌کند",
                },
                new("Description", "توضیحات", MKind.TextArea) { Wide = true, Rows = 4 },
                new("MinMarginPercent", "کمترین سود دسته (٪)", MKind.Int)
                {
                    Ltr = true, Placeholder = "8",
                    Help = "خالی = کف سود سراسری تنظیمات. فقط می‌تواند سود را بالاتر ببرد؛ زیردسته‌ها ارث می‌برند. در پیشنهاد قیمت و قیمت‌گذاری خودکار تأمین‌کننده اعمال می‌شود.",
                },
                Order(), Active(),
            ],
            Children = [("ویژگی‌های فیلتر", "filter-attributes")],
        },
        new()
        {
            Key = "brands", Title = "برندها", Singular = "برند", Group = "کاتالوگ", Icon = "Tag", Entity = typeof(Brand),
            Permissions = [P.CatalogManage], HasSeo = true, Search = ["Name", "EnglishName", "Slug"],
            Fields =
            [
                new("LogoName", "لوگو", MKind.Image) { InList = true, Area = UploadArea.Brands },
                new("Name", "نام", MKind.Text) { Required = true, InList = true },
                new("EnglishName", "نام انگلیسی", MKind.Text) { Ltr = true, InList = true },
                new("Slug", "اسلاگ", MKind.Slug) { Ltr = true, SlugFrom = "EnglishName" },
                new("Description", "توضیحات", MKind.TextArea) { Wide = true },
                Order(), Active(),
            ],
        },
        new()
        {
            Key = "suppliers", Title = "تأمین‌کنندگان", Singular = "تأمین‌کننده", Group = "کاتالوگ", Icon = "Factory", Entity = typeof(Supplier),
            Permissions = [P.SuppliersManage], Search = ["Name", "Website", "ContactName", "Phone"],
            Description = "سایت‌ها و فروشندگانی که کالاها از آن‌ها خریداری می‌شود (خصوصی؛ هرگز در سایت نمایش داده نمی‌شود).",
            Fields =
            [
                new("Name", "نام", MKind.Text) { Required = true, InList = true },
                new("Website", "وب‌سایت", MKind.Url) { Ltr = true, InList = true, Placeholder = "https://" },
                new("ContactName", "نام رابط", MKind.Text) { InList = true },
                new("Phone", "تلفن", MKind.Text) { Ltr = true, InList = true },
                new("Email", "ایمیل", MKind.Text) { Ltr = true },
                new("Notes", "یادداشت (شرایط خرید، حساب کاربری، …)", MKind.TextArea) { Wide = true },
                new("SyncMode", "روش بررسی خودکار موجودی و قیمت", MKind.Enum)
                {
                    InList = true,
                    Labels = new Dictionary<string, string>
                    {
                        ["Auto"] = "تشخیص خودکار", ["WooCommerce"] = "API ووکامرس", ["StructuredData"] = "داده‌های صفحهٔ محصول", ["Disabled"] = "بررسی نشود",
                    },
                    Help = "«تشخیص خودکار» برای اکثر سایت‌ها کافی است. گزارش‌ها در «محصولات ← بررسی تأمین‌کننده».",
                },
                Order(), Active(),
            ],
        },
        new()
        {
            Key = "product-videos", Title = "ویدیوهای محصول", Singular = "ویدیو", Group = "کاتالوگ", Icon = "VideoCamera", Entity = typeof(ProductVideo),
            Permissions = [P.ProductsManage], ParentField = "ProductId", Search = ["Title", "Source"],
            Fields =
            [
                new("ProductId", "محصول", MKind.Ref) { RefType = typeof(Product), RefLabel = "Name", Required = true, InList = true },
                new("Title", "عنوان", MKind.Text) { InList = true },
                new("Provider", "منبع", MKind.Enum) { Labels = VideoProviders, InList = true },
                new("Source", "شناسه آپارات یا آدرس فایل", MKind.Text) { Required = true, Ltr = true, Wide = true, Help = "آپارات: شناسه ویدیو (مثلاً abc12) یا لینک صفحه آن؛ لینک مستقیم: آدرس کامل فایل mp4." },
                new("PosterImage", "تصویر پیش‌نمایش", MKind.Image) { Area = UploadArea.Content, StoreAsPath = true },
                new("DurationSeconds", "مدت (ثانیه)", MKind.Int),
                Order(), Active(),
            ],
        },
        new()
        {
            Key = "filter-attributes", Title = "ویژگی‌های فیلتر", Singular = "ویژگی فیلتر", Group = "کاتالوگ", Icon = "Funnel", Entity = typeof(ProductFilterAttribute),
            Permissions = [P.CatalogManage], ParentField = "CategoryId", Search = ["Title", "FeatureKey"],
            Description = "کدام مشخصه‌های فنی محصولات به‌صورت فیلتر در فهرست محصولات نمایش داده شوند.",
            Fields =
            [
                new("Title", "عنوان فیلتر", MKind.Text) { Required = true, InList = true },
                new("FeatureKey", "عنوان مشخصه در محصولات", MKind.Text) { Required = true, InList = true, Help = "دقیقاً همان عنوانی که در «مشخصات فنی» محصولات وارد شده است." },
                new("Slug", "نام در آدرس", MKind.Slug) { Ltr = true, SlugFrom = "Title" },
                new("CategoryId", "فقط برای دسته", MKind.Ref) { RefType = typeof(Category), InList = true },
                new("ShowWithoutCategory", "نمایش در همه محصولات", MKind.Bool),
                new("MaxValues", "حداکثر گزینه‌ها", MKind.Int),
                Order(), Active(),
            ],
        },
        new()
        {
            Key = "search-synonyms", Title = "مترادف‌های جست‌وجو", Singular = "گروه مترادف", Group = "کاتالوگ", Icon = "MagnifyingGlass", Entity = typeof(SearchSynonym),
            Permissions = [P.CatalogManage, P.SeoManage], Search = ["Terms"], Sort = "CreatedAt",
            Description = "واژه‌های هم‌معنی که در جست‌وجو یکسان در نظر گرفته می‌شوند (مثلاً: گوشی، موبایل، تلفن همراه).",
            Fields =
            [
                new("Terms", "واژه‌ها (با ویرگول جدا کنید)", MKind.TextArea) { Required = true, InList = true, Wide = true },
                Active(),
            ],
        },

        // ── Marketing ──────────────────────────────────────────────────
        new()
        {
            Key = "coupons", Title = "کوپن‌های تخفیف", Singular = "کوپن", Group = "بازاریابی", Icon = "Percent", Entity = typeof(Coupon),
            Permissions = [P.MarketingManage], Search = ["Code", "Title"], Sort = "CreatedAt", SortDescending = true,
            Fields =
            [
                new("Code", "کد", MKind.Text) { Required = true, Ltr = true, InList = true, Help = "حروف انگلیسی و عدد؛ مشتری همین کد را وارد می‌کند." },
                new("Title", "عنوان", MKind.Text) { Required = true, InList = true },
                new("Type", "نوع", MKind.Enum) { Labels = CouponTypes, InList = true },
                new("Value", "مقدار (درصد یا تومان)", MKind.Decimal) { InList = true },
                new("MaxDiscountAmount", "سقف تخفیف (تومان)", MKind.Decimal),
                new("MinOrderAmount", "حداقل مبلغ سفارش", MKind.Decimal),
                new("MaxOrderAmount", "حداکثر مبلغ سفارش", MKind.Decimal),
                new("MinItemCount", "حداقل تعداد کالا", MKind.Int),
                new("StartsAt", "شروع", MKind.Date) { InList = true },
                new("EndsAt", "پایان", MKind.Date) { InList = true },
                new("UsageLimit", "سقف کل استفاده", MKind.Int),
                new("UsageLimitPerUser", "سقف استفاده هر کاربر", MKind.Int),
                new("UsedCount", "تعداد استفاده‌شده", MKind.ReadOnly) { InList = true },
                new("FirstOrderOnly", "فقط اولین خرید", MKind.Bool),
                new("ExcludeDiscountedProducts", "روی کالاهای تخفیف‌دار اعمال نشود", MKind.Bool),
                new("IsPublic", "نمایش عمومی (در صفحه پیشنهادها)", MKind.Bool),
                new("Description", "توضیحات", MKind.TextArea) { Wide = true },
                Active(),
            ],
        },
        new()
        {
            Key = "banners", Title = "بنرها و اسلایدها", Singular = "بنر", Group = "بازاریابی", Icon = "Image", Entity = typeof(Banner),
            Permissions = [P.MarketingManage, P.ContentManage], Search = ["Title", "Subtitle"],
            Fields =
            [
                new("ImageName", "تصویر دسکتاپ", MKind.Image)
                {
                    Required = true, InList = true, Area = UploadArea.Banners,
                    Help = "اندازه‌ها (مثل دیجی‌کالا): اسلایدر ۱۹۲۰×۴۸۰، بنر عریض ۱۹۲۰×۳۲۰، بنر دوتایی ۹۶۰×۴۰۰، سربرگ دسته ۱۹۲۰×۳۰۰",
                },
                new("MobileImageName", "تصویر موبایل", MKind.Image)
                {
                    Area = UploadArea.Banners, Help = "اسلایدر ۱۰۸۰×۵۴۰، بنر عریض و سربرگ دسته ۱۰۸۰×۳۶۰؛ خالی ← همان تصویر دسکتاپ",
                },
                new("Title", "عنوان", MKind.Text) { InList = true },
                new("Subtitle", "زیرعنوان", MKind.Text),
                new("ImageAlt", "متن جایگزین تصویر", MKind.Text),
                new("ButtonText", "متن دکمه", MKind.Text),
                new("LinkUrl", "لینک", MKind.Url) { Ltr = true },
                new("Position", "جایگاه", MKind.Enum) { Labels = BannerPositions, InList = true },
                new("CategoryId", "دسته (برای سربرگ دسته)", MKind.Ref) { RefType = typeof(Category), RefLabel = "Name", Help = "فقط برای جایگاه «سربرگ صفحهٔ دسته»" },
                new("ProductId", "کالای اصلی بنر", MKind.Ref)
                {
                    RefType = typeof(Product), RefLabel = "Name", Help = "اختیاری؛ کالا ناموجود یا غیرفعال شود، بنر خودکار پنهان می‌شود و با موجود شدن برمی‌گردد",
                },
                new("VariantGroup", "گروه نسخه (A/B)", MKind.Text)
                {
                    Ltr = true, Placeholder = "yalda-1405",
                    Help = "چند بنر هم‌جایگاه با یک گروه به نوبت نمایش داده می‌شوند و پرکلیک‌تر بیشتر دیده می‌شود؛ «mourning» = بنر روزهای سوگواری",
                },
                new("Views", "نمایش", MKind.ReadOnly) { InList = true },
                new("Clicks", "کلیک", MKind.ReadOnly) { InList = true },
                new("StartAt", "شروع نمایش", MKind.Date) { InList = true },
                new("EndAt", "پایان نمایش", MKind.Date) { InList = true },
                Order(), Active(),
            ],
        },
        new()
        {
            Key = "loyalty-rules", Title = "قوانین امتیازدهی باشگاه", Singular = "قانون امتیاز", Group = "بازاریابی", Icon = "Crown", Entity = typeof(LoyaltyRule),
            Permissions = [P.LoyaltyManage], Search = ["Title"],
            Fields =
            [
                new("Action", "رویداد", MKind.Enum) { Labels = LoyaltyActions, InList = true },
                new("Title", "عنوان", MKind.Text) { Required = true, InList = true },
                new("Points", "امتیاز ثابت", MKind.Int) { InList = true },
                new("PointsPerStep", "امتیاز به ازای هر پله مبلغ", MKind.Int),
                new("AmountStep", "پله مبلغ (تومان)", MKind.Decimal),
                new("MinOrderAmount", "حداقل مبلغ سفارش", MKind.Decimal),
                new("MaxPointsPerEvent", "سقف امتیاز هر بار", MKind.Int),
                new("MaxTimesPerUser", "سقف دفعات برای هر کاربر", MKind.Int),
                new("Description", "توضیحات", MKind.TextArea) { Wide = true },
                Order(), Active(),
            ],
        },
        new()
        {
            Key = "loyalty-tiers", Title = "سطح‌های باشگاه", Singular = "سطح", Group = "بازاریابی", Icon = "Medal", Entity = typeof(LoyaltyTier),
            Permissions = [P.LoyaltyManage], Search = ["Name"],
            Fields =
            [
                new("Name", "نام سطح", MKind.Text) { Required = true, InList = true },
                new("MinLifetimePoints", "حداقل امتیاز کل", MKind.Int) { InList = true },
                new("PointsMultiplier", "ضریب امتیاز", MKind.Decimal) { InList = true },
                new("Color", "رنگ", MKind.Color) { InList = true, Ltr = true },
                new("Benefits", "مزایا", MKind.TextArea) { Wide = true },
                Order(), Active(),
            ],
        },
        new()
        {
            Key = "loyalty-rewards", Title = "جوایز باشگاه", Singular = "جایزه", Group = "بازاریابی", Icon = "Gift", Entity = typeof(LoyaltyReward),
            Permissions = [P.LoyaltyManage], Search = ["Title"],
            Fields =
            [
                new("Title", "عنوان", MKind.Text) { Required = true, InList = true },
                new("Type", "نوع", MKind.Enum) { Labels = RewardTypes, InList = true },
                new("PointsCost", "امتیاز لازم", MKind.Int) { InList = true },
                new("Value", "مقدار (تومان یا درصد)", MKind.Decimal) { InList = true },
                new("MaxDiscountAmount", "سقف تخفیف", MKind.Decimal),
                new("MinOrderAmount", "حداقل مبلغ سفارش", MKind.Decimal),
                new("CouponValidDays", "اعتبار کوپن (روز)", MKind.Int),
                new("MinTierId", "حداقل سطح", MKind.Ref) { RefType = typeof(LoyaltyTier), RefLabel = "Name" },
                new("Description", "توضیحات", MKind.TextArea) { Wide = true },
                Order(), Active(),
            ],
        },
        new()
        {
            Key = "cart-reminders", Title = "مراحل یادآوری سبد رهاشده", Singular = "مرحله یادآوری", Group = "بازاریابی", Icon = "CartFull", Entity = typeof(AbandonedCartReminderStep),
            Permissions = [P.MarketingManage], Sort = "StepNumber", Search = ["Title"],
            Fields =
            [
                new("StepNumber", "شماره مرحله", MKind.Int) { InList = true },
                new("DelayMinutes", "تأخیر (دقیقه پس از رها شدن)", MKind.Int) { InList = true },
                new("Channel", "کانال", MKind.Enum) { Labels = ReminderChannels, InList = true },
                new("Title", "عنوان", MKind.Text) { Required = true, InList = true },
                new("Message", "متن اعلان", MKind.TextArea) { Required = true, Wide = true },
                new("SmsText", "متن پیامک", MKind.TextArea) { Wide = true },
                new("IncludeCoupon", "همراه با کوپن", MKind.Bool) { InList = true },
                new("CouponType", "نوع کوپن", MKind.Enum) { Labels = CouponTypes },
                new("CouponValue", "مقدار کوپن", MKind.Decimal),
                new("CouponMaxDiscountAmount", "سقف تخفیف کوپن", MKind.Decimal),
                new("CouponMinOrderAmount", "حداقل سفارش کوپن", MKind.Decimal),
                new("CouponValidDays", "اعتبار کوپن (روز)", MKind.Int),
                Active(),
            ],
        },

        // ── Content ────────────────────────────────────────────────────
        new()
        {
            Key = "content-pages", Title = "صفحات محتوایی", Singular = "صفحه", Group = "محتوا", Icon = "Notebook", Entity = typeof(ContentPage),
            Permissions = [P.ContentManage], HasSeo = true, Search = ["Title", "Slug"], PublicUrl = "/page/{Slug}",
            Description = "درباره ما، قوانین، حریم خصوصی، راهنماها و هر صفحه دلخواه.",
            Fields =
            [
                new("Title", "عنوان", MKind.Text) { Required = true, InList = true },
                new("Slug", "اسلاگ", MKind.Slug) { Ltr = true, InList = true, SlugFrom = "Title" },
                new("Summary", "خلاصه", MKind.TextArea) { Wide = true },
                new("Content", "متن صفحه", MKind.Html) { Wide = true, Rows = 16 },
                new("ImageUrl", "تصویر", MKind.Image) { Area = UploadArea.Content, StoreAsPath = true },
                new("ShowInFooter", "نمایش در فوتر", MKind.Bool) { InList = true },
                Order(), Active(),
            ],
        },
        new()
        {
            Key = "faq-categories", Title = "دسته‌های سؤالات متداول", Singular = "دسته سؤال", Group = "محتوا", Icon = "Question", Entity = typeof(FaqCategory),
            Permissions = [P.ContentManage], Search = ["Title"], Children = [("سؤال‌ها", "faq-items")],
            Fields =
            [
                new("Title", "عنوان", MKind.Text) { Required = true, InList = true },
                new("Slug", "اسلاگ", MKind.Slug) { Ltr = true, SlugFrom = "Title" },
                new("Description", "توضیحات", MKind.TextArea) { Wide = true },
                new("IconKey", "آیکن", MKind.Text) { Ltr = true },
                Order(), Active(),
            ],
        },
        new()
        {
            Key = "faq-items", Title = "سؤالات متداول", Singular = "سؤال", Group = "محتوا", Icon = "Question", Entity = typeof(FaqItem),
            Permissions = [P.ContentManage], ParentField = "CategoryId", Search = ["Question", "Answer"],
            Fields =
            [
                new("CategoryId", "دسته", MKind.Ref) { RefType = typeof(FaqCategory), RefLabel = "Title", InList = true },
                new("Question", "سؤال", MKind.Text) { Required = true, InList = true, Wide = true },
                new("Answer", "پاسخ", MKind.Html) { Required = true, Wide = true, Rows = 6 },
                Order(), Active(),
            ],
        },
        new()
        {
            Key = "menus", Title = "منوها", Singular = "منو", Group = "محتوا", Icon = "List", Entity = typeof(Menu),
            Permissions = [P.ContentManage], Search = ["Title", "Key"], Children = [("آیتم‌های منو", "menu-items")],
            Fields =
            [
                new("Title", "عنوان", MKind.Text) { Required = true, InList = true },
                new("Key", "کلید", MKind.Text) { Required = true, Ltr = true, InList = true },
                new("Location", "جایگاه", MKind.Enum) { Labels = MenuLocations, InList = true },
                new("Source", "منبع آیتم‌ها", MKind.Enum) { Labels = MenuSources },
                Order(), Active(),
            ],
        },
        new()
        {
            Key = "menu-items", Title = "آیتم‌های منو", Singular = "آیتم منو", Group = "محتوا", Icon = "List", Entity = typeof(MenuItem),
            Permissions = [P.ContentManage], ParentField = "MenuId", Search = ["Title", "Url"],
            Fields =
            [
                new("MenuId", "منو", MKind.Ref) { RefType = typeof(Menu), RefLabel = "Title", Required = true, InList = true },
                new("ParentId", "آیتم والد", MKind.Ref) { RefType = typeof(MenuItem), RefLabel = "Title", InList = true },
                new("Title", "عنوان", MKind.Text) { Required = true, InList = true },
                new("Url", "لینک", MKind.Url) { Ltr = true, InList = true },
                new("Type", "نوع", MKind.Enum) { Labels = MenuItemTypes },
                new("Icon", "آیکن", MKind.Text) { Ltr = true },
                new("BadgeText", "برچسب (مثلاً جدید)", MKind.Text),
                new("OpenInNewTab", "باز شدن در زبانه جدید", MKind.Bool),
                new("StartsAt", "شروع نمایش", MKind.Date),
                new("EndsAt", "پایان نمایش", MKind.Date),
                Order(), Active(),
            ],
        },
        new()
        {
            Key = "home-sections", Title = "بخش‌های صفحه اصلی", Singular = "بخش", Group = "محتوا", Icon = "SquaresFour", Entity = typeof(HomeSection),
            Permissions = [P.ContentManage, P.MarketingManage], Search = ["Title"],
            Fields =
            [
                new("Type", "نوع بخش", MKind.Enum) { Labels = HomeSectionTypes, InList = true },
                new("Title", "عنوان", MKind.Text) { InList = true },
                new("Subtitle", "زیرعنوان", MKind.Text),
                new("LinkUrl", "لینک «مشاهده همه»", MKind.Url) { Ltr = true },
                new("ItemCount", "تعداد آیتم‌ها", MKind.Int),
                new("CategoryId", "دسته (برای «محصولات یک دسته»)", MKind.Ref) { RefType = typeof(Category) },
                Order(), Active(),
            ],
        },
        new()
        {
            Key = "site-features", Title = "مزایای فروشگاه", Singular = "مزیت", Group = "محتوا", Icon = "SealCheck", Entity = typeof(SiteFeature),
            Permissions = [P.ContentManage], Search = ["Title"],
            Fields =
            [
                new("Title", "عنوان", MKind.Text) { Required = true, InList = true },
                new("Description", "توضیح", MKind.Text) { InList = true },
                new("IconKey", "آیکن", MKind.Text) { Ltr = true },
                new("LinkUrl", "لینک", MKind.Url) { Ltr = true },
                Order(), Active(),
            ],
        },
        new()
        {
            Key = "trust-badges", Title = "نمادهای اعتماد", Singular = "نماد", Group = "محتوا", Icon = "ShieldCheck", Entity = typeof(TrustBadge),
            Permissions = [P.ContentManage], Search = ["Title"],
            Fields =
            [
                new("ImageUrl", "تصویر", MKind.Image) { InList = true, Area = UploadArea.Content, StoreAsPath = true, Help = "یا تصویر را بارگذاری کنید یا «کد نماد» را بچسبانید." },
                new("Title", "عنوان", MKind.Text) { Required = true, InList = true },
                new("LinkUrl", "لینک", MKind.Url) { Ltr = true },
                new("EmbedCode", "کد نماد (اختیاری)", MKind.TextArea)
                {
                    Ltr = true, Wide = true, NoInline = true, Rows = 4,
                    Help = "کد آمادهٔ اینماد، ساماندهی و … را همین‌جا بچسبانید؛ تصویر و لینک امن از آن برداشته می‌شود و کد خام هرگز در سایت چاپ نمی‌شود.",
                },
                Order(), Active(),
            ],
            AfterBind = entity => TrustBadgeRules.Apply((TrustBadge)entity),
        },
        new()
        {
            Key = "page-alerts", Title = "پیام‌ها و اطلاعیه‌های صفحات", Singular = "اطلاعیه", Group = "محتوا", Icon = "Megaphone", Entity = typeof(PageAlertEntity),
            Permissions = [P.ContentManage, P.MarketingManage], Search = ["Title", "Content"],
            Fields =
            [
                new("Page", "صفحه", MKind.Enum) { Labels = SitePages, InList = true },
                new("Position", "جایگاه", MKind.Enum) { Labels = AlertPositions, InList = true },
                new("Type", "نوع", MKind.Enum) { Labels = AlertTypes, InList = true },
                new("Audience", "مخاطب", MKind.Enum) { Labels = AlertAudiences },
                new("Title", "عنوان", MKind.Text) { InList = true },
                new("Content", "متن", MKind.TextArea) { Required = true, Wide = true },
                new("LinkUrl", "لینک", MKind.Url) { Ltr = true },
                new("LinkText", "متن لینک", MKind.Text),
                new("IsDismissible", "قابل بستن", MKind.Bool),
                new("StartAt", "شروع", MKind.Date) { InList = true },
                new("EndAt", "پایان", MKind.Date) { InList = true },
                Order(), Active(),
            ],
        },
        new()
        {
            Key = "blog-categories", Title = "دسته‌های وبلاگ", Singular = "دسته وبلاگ", Group = "محتوا", Icon = "Article", Entity = typeof(BlogCategory),
            Permissions = [P.BlogPublish, P.ContentManage], HasSeo = true, Search = ["Name", "Slug"],
            Fields =
            [
                new("Name", "نام", MKind.Text) { Required = true, InList = true },
                new("Slug", "اسلاگ", MKind.Slug) { Ltr = true, InList = true, SlugFrom = "Name" },
                new("Description", "توضیحات", MKind.TextArea) { Wide = true },
                Order(), Active(),
            ],
        },

        // ── SEO ────────────────────────────────────────────────────────
        new()
        {
            Key = "seo-redirects", Title = "ریدایرکت‌ها", Singular = "ریدایرکت", Group = "سئو", Icon = "ArrowsLeftRight", Entity = typeof(SeoRedirect),
            Permissions = [P.SeoManage], Search = ["FromPath", "ToUrl", "Notes"], Sort = "CreatedAt", SortDescending = true,
            Fields =
            [
                new("FromPath", "از مسیر", MKind.Text) { Required = true, Ltr = true, InList = true, Placeholder = "/old-path", Help = "مسیر نسبی قدیمی (با / شروع شود)." },
                new("ToUrl", "به آدرس", MKind.Url) { Required = true, Ltr = true, InList = true },
                new("Type", "نوع", MKind.Enum) { Labels = RedirectTypes, InList = true },
                new("Notes", "یادداشت", MKind.Text),
                new("HitCount", "تعداد بازدید", MKind.ReadOnly) { InList = true },
                new("LastHitAt", "آخرین استفاده", MKind.ReadOnly),
                Active(),
            ],
        },
        new()
        {
            Key = "page-seo", Title = "سئو صفحات ثابت", Singular = "سئو صفحه", Group = "سئو", Icon = "Globe", Entity = typeof(PageSeoEntity),
            Permissions = [P.SeoManage], HasSeo = true, Sort = "Page",
            Description = "عنوان و توضیحات متای صفحاتی مانند صفحه اصلی، جست‌وجو، وبلاگ و تماس با ما.",
            Fields = [new("Page", "صفحه", MKind.Enum) { Labels = SitePages, InList = true }],
        },

        // ── Shipping ───────────────────────────────────────────────────
        new()
        {
            Key = "shipping-methods", Title = "روش‌های ارسال", Singular = "روش ارسال", Group = "ارسال", Icon = "Truck", Entity = typeof(ShippingMethod),
            Permissions = [P.ShippingManage], Search = ["Title", "Code"], Children = [("بازه‌های تحویل", "time-slots"), ("تعرفه‌ها", "shipping-rates")],
            Fields =
            [
                new("Title", "عنوان", MKind.Text) { Required = true, InList = true },
                new("Code", "کد", MKind.Text) { Required = true, Ltr = true, InList = true },
                new("Type", "نوع", MKind.Enum) { Labels = ShippingTypes, InList = true },
                new("Cost", "هزینه پایه (تومان)", MKind.Decimal) { InList = true },
                new("PricingMode", "نحوه قیمت‌گذاری", MKind.Enum) { Labels = PricingModes },
                new("OnlyListedLocations", "فقط برای مقصدهای تعریف‌شده", MKind.Bool),
                new("FreeShippingThreshold", "ارسال رایگان از مبلغ", MKind.Decimal),
                new("MinDeliveryDays", "حداقل روز تحویل", MKind.Int),
                new("MaxDeliveryDays", "حداکثر روز تحویل", MKind.Int),
                new("RequiresTimeSlot", "انتخاب بازه تحویل", MKind.Bool),
                new("SupportsCashOnDelivery", "پرداخت در محل", MKind.Bool) { InList = true },
                new("ProvinceId", "فقط در استان", MKind.Ref) { RefType = typeof(Location), RefLabel = "Name" },
                new("Description", "توضیحات", MKind.TextArea) { Wide = true },
                Order(), Active(),
            ],
        },
        new()
        {
            Key = "time-slots", Title = "بازه‌های زمانی تحویل", Singular = "بازه تحویل", Group = "ارسال", Icon = "Clock", Entity = typeof(DeliveryTimeSlot),
            Permissions = [P.ShippingManage], ParentField = "ShippingMethodId", Search = ["Title"],
            Fields =
            [
                new("ShippingMethodId", "روش ارسال", MKind.Ref) { RefType = typeof(ShippingMethod), RefLabel = "Title", Required = true, InList = true },
                new("Title", "عنوان", MKind.Text) { Required = true, InList = true, Placeholder = "۹ تا ۱۲" },
                new("StartTime", "از ساعت", MKind.Time) { Required = true, InList = true, Ltr = true },
                new("EndTime", "تا ساعت", MKind.Time) { Required = true, InList = true, Ltr = true },
                new("Capacity", "ظرفیت هر روز", MKind.Int) { InList = true, Help = "خالی = نامحدود" },
                Order(), Active(),
            ],
        },
        new()
        {
            Key = "shipping-rates", Title = "تعرفه‌های ارسال", Singular = "تعرفه", Group = "ارسال", Icon = "MapPin", Entity = typeof(ShippingRate),
            Permissions = [P.ShippingManage], ParentField = "ShippingMethodId", Sort = "CreatedAt", Search = ["Note"],
            Fields =
            [
                new("ShippingMethodId", "روش ارسال", MKind.Ref) { RefType = typeof(ShippingMethod), RefLabel = "Title", InList = true, Help = "خالی = همه روش‌ها" },
                new("ProvinceId", "استان", MKind.Ref) { RefType = typeof(Location), RefLabel = "Name", InList = true },
                new("CityId", "شهر", MKind.Ref) { RefType = typeof(Location), RefLabel = "Name", InList = true },
                new("Cost", "هزینه (تومان)", MKind.Decimal) { InList = true },
                new("FreeShippingThreshold", "ارسال رایگان از مبلغ", MKind.Decimal),
                new("ExtraDeliveryDays", "روز اضافه تحویل", MKind.Int),
                new("IsBlocked", "ارسال به این مقصد ممکن نیست", MKind.Bool) { InList = true },
                new("Note", "یادداشت", MKind.Text),
                Active(),
            ],
        },
        new()
        {
            Key = "locations", Title = "استان‌ها و شهرها", Singular = "مکان", Group = "ارسال", Icon = "Globe", Entity = typeof(Location),
            Permissions = [P.ShippingManage], ParentField = "ParentId", Search = ["Name", "Code"], Sort = "Name",
            Fields =
            [
                new("Name", "نام", MKind.Text) { Required = true, InList = true },
                new("Code", "کد", MKind.Text) { Required = true, Ltr = true, InList = true },
                new("Type", "نوع", MKind.Enum) { Labels = LocationTypes, InList = true },
                new("ParentId", "زیرمجموعه", MKind.Ref) { RefType = typeof(Location), RefLabel = "Name", InList = true },
                Active(),
            ],
        },

        // ── Support ────────────────────────────────────────────────────
        new()
        {
            Key = "return-reasons", Title = "دلایل مرجوعی", Singular = "دلیل مرجوعی", Group = "پشتیبانی", Icon = "ArrowUUpLeft", Entity = typeof(ReturnReason),
            Permissions = [P.ReturnsManage], Search = ["Title"],
            Fields =
            [
                new("Title", "عنوان", MKind.Text) { Required = true, InList = true },
                new("Hint", "راهنما برای مشتری", MKind.Text) { Wide = true },
                new("RequiresImage", "تصویر الزامی است", MKind.Bool) { InList = true },
                Order(), Active(),
            ],
        },
        new()
        {
            Key = "support-departments", Title = "دپارتمان‌های پشتیبانی", Singular = "دپارتمان", Group = "پشتیبانی", Icon = "Headset", Entity = typeof(SupportDepartment),
            Permissions = [P.TicketsManage], Search = ["Title"],
            Fields =
            [
                new("Title", "عنوان", MKind.Text) { Required = true, InList = true },
                new("Description", "توضیحات", MKind.Text) { Wide = true },
                Order(), Active(),
            ],
        },
    ];

    public static MDef? Find(string? key) => All.FirstOrDefault(d => string.Equals(d.Key, key, StringComparison.OrdinalIgnoreCase));
}
