using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Contents;
using Fratak_Shop.Data.Entities.Faqs;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Entities.Settings;
using Fratak_Shop.Data.Entities.Shipping;
using Fratak_Shop.Data.Utilities.SeoManagement;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Data.Seeders;

/// <summary>
/// Seeds the essential, admin-editable store data the storefront needs to work:
/// store settings, shipping methods and delivery time slots, content pages,
/// FAQ groups, home page sections and "why us" features.
/// Every block only runs when its table is empty, so values edited in the admin
/// panel are never overwritten.
/// <para>مرحلهٔ سیزدهم (آمادهٔ انتشار): این دادهٔ پیش‌فرض فقط یک بار، روی پایگاه دادهٔ کاملاً تازه ساخته می‌شود
/// (<see cref="DefaultDataSeeder"/>)؛ اطلاعات تماس ساختگی (تلفن، آدرس، نقشه) و نمادهای اعتماد نمونه دیگر اینجا نیستند
/// چون روی سایت واقعی نباید اطلاعات غلط نمایش داده شود — آن‌ها فقط با دادهٔ نمونه می‌آیند (<see cref="SampleContentSeeder"/>).</para>
/// </summary>
public static class StoreDefaultsSeeder
{
    /// <summary>Tehran province (location code 123 in the location dictionary).</summary>
    public static Guid TehranProvinceId => LocationSeeder.ProvinceId("123");

    public static async Task SeedAsync(FratakShopDbContext context, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(context);

        await SeedSettingsAsync(context, cancellationToken);
        await SeedShippingAsync(context, cancellationToken);
        await SeedContentPagesAsync(context, cancellationToken);
        await SeedFaqAsync(context, cancellationToken);
        await SeedHomeSectionsAsync(context, cancellationToken);
        await SeedFeaturesAsync(context, cancellationToken);
        // نماد اعتماد الکترونیکی باید نماد واقعی همان فروشگاه (با لینک اینماد) باشد؛ تصویر نمونه فقط در دادهٔ نمونه.
    }

    private static async Task SeedSettingsAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (await context.StoreSettings.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        context.StoreSettings.Add(new StoreSetting
        {
            SiteName = "فراتک شاپ",
            Tagline = "بهترین خرید رو تجربه کن!",
            SiteDescription = "فراتک شاپ؛ فروشگاه اینترنتی کالای دیجیتال با ضمانت اصالت کالا، ارسال سریع به سراسر ایران و پشتیبانی همه روزه.",
            // تلفن، ایمیل، آدرس، نقشه و شبکه‌های اجتماعی عمداً خالی‌اند: سایت واقعی نباید اطلاعات تماس ساختگی نشان دهد.
            // مدیر از «تنظیمات › اطلاعات فروشگاه» پر می‌کند (چک‌لیست راه‌اندازی در «سیستم › سلامت» یادآوری می‌کند).
            WorkingHours = "شنبه تا پنجشنبه ۹ تا ۱۸",
            FooterTitle = "بهترین خرید رو تجربه کن!",
            FooterSubtitle = "به مشتریان خود خدمات شگفت انگیزی ارائه می دهیم.",
            FooterText = "برای داشتن مشتریان همیشه راضی، کنترل کیفی دقیق لازم داریم. ارتباط عالی با مشتریان یکی از ویژگی های ماست و ما دائما در حال بهبود کیفیت خدمات خود هستیم.",
            CopyrightText = "تمامی حقوق مادی و معنوی این وب‌سایت متعلق به فراتک شاپ است.",
            FreeShippingThreshold = 5_000_000,
            MinimumOrderAmount = 0,
            DeliveryDaysAhead = 6,
            DeliveryOffDays = "5",
            UnpaidOrderTimeoutMinutes = 60,
            AllowCustomerCancellation = true,
            IsOnlinePaymentEnabled = true,
            IsCashOnDeliveryEnabled = true,
            CashOnDeliveryMaxAmount = 0,
            // Public ZarinPal sandbox merchant – replace with the real merchant id and turn sandbox off in production.
            ZarinPalMerchantId = "1344b5d4-0048-11e8-94db-005056a205be",
            ZarinPalSandbox = true,
            IsWalletEnabled = true,
            IsWalletPaymentEnabled = true,
            IsWalletTopUpEnabled = true,
            IsWithdrawalEnabled = true,
            WalletMinTopUpAmount = 10_000,
            WalletMaxTopUpAmount = 100_000_000,
            WalletMinWithdrawalAmount = 50_000,
            TitleSeparator = "|",
            DefaultMetaKeywords = "فروشگاه اینترنتی, خرید آنلاین, کالای دیجیتال, هدفون, ساعت هوشمند, لپ تاپ",
            OrganizationLegalName = "فراتک شاپ",
        });

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedShippingAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (await context.ShippingMethods.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        var tehranExists = await context.Locations.AnyAsync(l => l.Id == TehranProvinceId, cancellationToken);
        Guid? tehran = tehranExists ? TehranProvinceId : null;

        var post = new ShippingMethod
        {
            Code = "post",
            Title = "پست پیشتاز",
            Description = "ارسال به سراسر کشور، ۳ تا ۵ روز کاری",
            Type = ShippingMethodType.Post,
            Cost = 65_000,
            MinDeliveryDays = 3,
            MaxDeliveryDays = 5,
            SupportsCashOnDelivery = false,
            DisplayOrder = 1,
        };

        var courier = new ShippingMethod
        {
            Code = "courier",
            Title = "پیک فروشگاه",
            Description = "ویژه تهران، تحویل در روز و بازه زمانی انتخابی",
            Type = ShippingMethodType.Courier,
            Cost = 90_000,
            MinDeliveryDays = 1,
            MaxDeliveryDays = 2,
            RequiresTimeSlot = true,
            SupportsCashOnDelivery = true,
            ProvinceId = tehran,
            DisplayOrder = 2,
            TimeSlots =
            [
                new DeliveryTimeSlot { Title = "۹ تا ۱۲", StartTime = new TimeOnly(9, 0), EndTime = new TimeOnly(12, 0), Capacity = 40, DisplayOrder = 1 },
                new DeliveryTimeSlot { Title = "۱۲ تا ۱۵", StartTime = new TimeOnly(12, 0), EndTime = new TimeOnly(15, 0), Capacity = 40, DisplayOrder = 2 },
                new DeliveryTimeSlot { Title = "۱۵ تا ۱۸", StartTime = new TimeOnly(15, 0), EndTime = new TimeOnly(18, 0), Capacity = 40, DisplayOrder = 3 },
                new DeliveryTimeSlot { Title = "۱۸ تا ۲۱", StartTime = new TimeOnly(18, 0), EndTime = new TimeOnly(21, 0), Capacity = 30, DisplayOrder = 4 },
            ],
        };

        var express = new ShippingMethod
        {
            Code = "express",
            Title = "ارسال فوری",
            Description = "ویژه تهران، تحویل همان روز (سفارش تا ساعت ۱۴)",
            Type = ShippingMethodType.Express,
            Cost = 150_000,
            FreeShippingThreshold = 0,
            MinDeliveryDays = 0,
            MaxDeliveryDays = 0,
            RequiresTimeSlot = true,
            SupportsCashOnDelivery = true,
            ProvinceId = tehran,
            IsActive = false,
            DisplayOrder = 3,
            TimeSlots =
            [
                new DeliveryTimeSlot { Title = "۱۷ تا ۲۱", StartTime = new TimeOnly(17, 0), EndTime = new TimeOnly(21, 0), Capacity = 20, DisplayOrder = 1 },
            ],
        };

        context.ShippingMethods.AddRange(post, courier, express);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedContentPagesAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (await context.ContentPages.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        context.ContentPages.AddRange(
            Page("about-us", "درباره ما", SitePage.AboutUs, 1,
                "آشنایی با فراتک شاپ، ارزش‌ها و خدماتی که برای یک خرید مطمئن ارائه می‌دهیم.",
                """
                <h2>فراتک شاپ را بشناسید</h2>
                <p>فراتک شاپ با هدف ارائه تجربه‌ای مطمئن و لذت‌بخش از خرید اینترنتی کالای دیجیتال فعالیت خود را آغاز کرد. تمام تلاش ما این است که کالای اصل را با بهترین قیمت و در کوتاه‌ترین زمان به دست شما برسانیم.</p>
                <h2>چرا فراتک شاپ؟</h2>
                <ul>
                  <li>ضمانت اصالت و سلامت فیزیکی تمام کالاها</li>
                  <li>ارسال سریع به سراسر ایران و پیک اختصاصی در تهران</li>
                  <li>امکان پرداخت اینترنتی، پرداخت از کیف پول و پرداخت در محل</li>
                  <li>پشتیبانی همه روزه و پاسخگویی سریع</li>
                </ul>
                <h2>ماموریت ما</h2>
                <p>ماموریت ما ساده است: خریدی آسان، شفاف و بدون دغدغه. اگر پیشنهاد یا انتقادی دارید، از طریق صفحه تماس با ما در ارتباط باشید؛ نظر شما برای ما بسیار ارزشمند است.</p>
                """, "/assets/image/about/image-1.jpg"),
            Page("rules", "قوانین و مقررات", SitePage.Terms, 2,
                "قوانین و مقررات استفاده از فروشگاه، ثبت سفارش، پرداخت، ارسال و بازگشت کالا.",
                """
                <h2>تعاریف</h2>
                <ul>
                  <li>کاربر: هر شخص حقیقی یا حقوقی است که از وب‌سایت فراتک شاپ استفاده می‌کند.</li>
                  <li>سفارش: درخواست خرید کالا که پس از تکمیل مراحل خرید در سامانه ثبت می‌شود.</li>
                  <li>روز کاری: روزهای شنبه تا چهارشنبه به جز تعطیلات رسمی.</li>
                </ul>
                <h2>حساب کاربری</h2>
                <ul>
                  <li>ثبت نام با شماره موبایل معتبر انجام می‌شود و هر شماره تنها یک حساب کاربری دارد.</li>
                  <li>مسئولیت حفظ رمز عبور و فعالیت‌های انجام‌شده با حساب کاربری بر عهده کاربر است.</li>
                </ul>
                <h2>ثبت سفارش و پرداخت</h2>
                <ul>
                  <li>قیمت کالاها به تومان و شامل مالیات بر ارزش افزوده است.</li>
                  <li>سفارش پس از پرداخت موفق (یا انتخاب پرداخت در محل) نهایی و برای پردازش ارسال می‌شود.</li>
                  <li>سفارش‌های پرداخت‌نشده اینترنتی پس از مدت مشخصی به‌صورت خودکار لغو می‌شوند.</li>
                </ul>
                <h2>لغو سفارش و بازگشت وجه</h2>
                <ul>
                  <li>تا پیش از شروع آماده‌سازی، امکان لغو سفارش از پروفایل کاربری وجود دارد.</li>
                  <li>مبلغ سفارش‌های پرداخت‌شده‌ای که لغو می‌شوند، بلافاصله به کیف پول کاربر بازگردانده می‌شود و قابل برداشت یا خرید مجدد است.</li>
                </ul>
                <h2>ارسال و تحویل</h2>
                <ul>
                  <li>هزینه و زمان ارسال بر اساس روش ارسال انتخابی در مرحله تکمیل سفارش نمایش داده می‌شود.</li>
                  <li>هنگام تحویل، سلامت ظاهری بسته را بررسی و در صورت آسیب‌دیدگی از تحویل آن خودداری کنید.</li>
                </ul>
                """),
            Page("privacy", "حریم خصوصی", SitePage.Privacy, 3,
                "سیاست حفظ حریم خصوصی و نحوه نگهداری اطلاعات کاربران فراتک شاپ.",
                """
                <h2>اطلاعاتی که جمع‌آوری می‌کنیم</h2>
                <p>برای ثبت و ارسال سفارش، نام، شماره تماس و نشانی شما را دریافت می‌کنیم. این اطلاعات تنها برای پردازش سفارش و اطلاع‌رسانی استفاده می‌شود.</p>
                <h2>امنیت اطلاعات</h2>
                <ul>
                  <li>ارتباط با سایت رمزنگاری‌شده است و رمز عبور شما به‌صورت هش‌شده نگهداری می‌شود.</li>
                  <li>پرداخت‌ها از طریق درگاه امن بانکی انجام می‌شود و اطلاعات کارت بانکی ذخیره نمی‌شود.</li>
                  <li>اطلاعات شما در اختیار اشخاص ثالث قرار نمی‌گیرد، مگر به حکم مراجع قانونی یا برای تحویل سفارش (مثلاً نام و آدرس گیرنده برای شرکت پست).</li>
                </ul>
                <h2>پیامک و اعلان‌ها</h2>
                <p>کد ورود، وضعیت سفارش و اطلاعیه‌های مهم حساب برای شما پیامک یا اعلان می‌شود. اعلان‌های تبلیغاتی را می‌توانید از بخش «اعلان‌ها» در پروفایل مدیریت کنید.</p>
                <h2>کوکی‌ها</h2>
                <p>برای ماندن در حساب کاربری، نگهداری سبد خرید و امنیت فرم‌ها از کوکی‌های ضروری استفاده می‌شود. آمار بازدید بدون شناسایی شخصی و بدون سرویس‌های ردیابی خارجی جمع‌آوری می‌شود.</p>
                <h2>حقوق شما</h2>
                <ul>
                  <li>اطلاعات حساب، آدرس‌ها و تاریخچهٔ سفارش‌ها همیشه از پروفایل کاربری قابل مشاهده و ویرایش است.</li>
                  <li>برای حذف حساب کاربری یا پرسش دربارهٔ اطلاعات خود، از طریق تیکت پشتیبانی یا صفحهٔ تماس با ما اقدام کنید.</li>
                </ul>
                """),
            Page("return-policy", "شرایط بازگشت کالا", SitePage.ReturnPolicy, 4,
                "شرایط و مراحل مرجوع کردن کالا در فراتک شاپ.",
                """
                <h2>مهلت بازگشت</h2>
                <p>تا ۷ روز پس از تحویل سفارش می‌توانید درخواست مرجوع کردن کالا را ثبت کنید. مهلت از روزی حساب می‌شود که وضعیت سفارش «تحویل شده» است.</p>
                <h2>مراحل مرجوع کردن</h2>
                <ol>
                  <li>در پروفایل، بخش «سفارش‌های من»، سفارش را باز کنید و «درخواست مرجوعی» را بزنید.</li>
                  <li>کالا و تعداد را انتخاب کنید، دلیل را برگزینید و توضیح ایراد را بنویسید.</li>
                  <li>پس از بررسی و تأیید درخواست، کالا را با بسته‌بندی کامل ارسال و کد رهگیری مرسوله را در همان صفحه ثبت کنید.</li>
                  <li>پس از رسیدن و بررسی کالا، مبلغ آن به کیف پول شما بازگردانده می‌شود.</li>
                </ol>
                <h2>کالاهای قابل مرجوع</h2>
                <ul>
                  <li>کالای سالم با جعبه، پلمپ، لوازم جانبی، دفترچه و کارت گارانتی کامل.</li>
                  <li>کالای معیوب، آسیب‌دیده در حمل یا مغایر با مشخصات سفارش (رنگ، مدل یا کالای اشتباه) — حتی اگر پلمپ باز شده باشد.</li>
                </ul>
                <h2>موارد غیرقابل مرجوع</h2>
                <ul>
                  <li>کالایی که پلمپ آن باز شده و ایراد فنی ندارد (به‌ویژه هدفون، هندزفری و لوازم بهداشتی شخصی).</li>
                  <li>کالایی که بر اثر استفادهٔ نادرست، ضربه، نفوذ مایعات یا تعمیر غیرمجاز آسیب دیده باشد.</li>
                  <li>درخواست‌هایی که پس از پایان مهلت ۷ روزه ثبت شوند؛ برای ایراد فنی پس از این مدت از گارانتی کالا استفاده کنید.</li>
                </ul>
                <h2>هزینهٔ ارسال و بازگشت وجه</h2>
                <ul>
                  <li>اگر کالا معیوب، آسیب‌دیده یا مغایر باشد، هزینهٔ ارسال برگشت بر عهدهٔ فروشگاه است.</li>
                  <li>در انصراف از خرید کالای سالم، هزینهٔ ارسال برگشت بر عهدهٔ خریدار است.</li>
                  <li>مبلغ تأییدشده به کیف پول بازمی‌گردد؛ سهمی که با پول نقد پرداخت شده قابل برداشت است و سهم اعتبار هدیه یا تخفیف فقط برای خرید بعدی.</li>
                </ul>
                <p>برای هر پرسش دربارهٔ مرجوعی، از تیکت پشتیبانی در پروفایل یا صفحهٔ «تماس با ما» استفاده کنید.</p>
                """),
            Page("warranty", "گارانتی و خدمات پس از فروش", SitePage.Warranty, 5,
                "اطلاعات گارانتی کالاها و خدمات پس از فروش فراتک شاپ.",
                """
                <h2>گارانتی کالاها</h2>
                <p>تمامی کالاهای فراتک شاپ دارای گارانتی معتبر هستند و مدت و شرکت گارانتی‌کننده در صفحه هر محصول درج شده است.</p>
                <h2>دریافت خدمات</h2>
                <p>برای استفاده از گارانتی، با پشتیبانی تماس بگیرید تا مراحل ارسال کالا به مرکز خدمات به شما اعلام شود.</p>
                <h2>نکته‌های مهم</h2>
                <ul>
                  <li>کارت گارانتی، فاکتور و جعبهٔ کالا را تا پایان مدت گارانتی نگه دارید.</li>
                  <li>آسیب‌های ناشی از ضربه، نوسان برق، نفوذ مایعات یا تعمیر توسط افراد غیرمجاز معمولاً مشمول گارانتی نیست.</li>
                  <li>پیگیری گارانتی از طریق تیکت پشتیبانی در پروفایل کاربری سریع‌تر انجام می‌شود.</li>
                </ul>
                """),
            Page("shipping", "شیوه‌های ارسال", SitePage.ShippingGuide, 6,
                "روش‌های ارسال سفارش، هزینه‌ها و زمان تحویل.",
                """
                <h2>پست پیشتاز</h2>
                <p>ارسال به سراسر کشور در ۳ تا ۵ روز کاری.</p>
                <h2>پیک فروشگاه</h2>
                <p>ویژه تهران؛ تحویل در روز و بازه زمانی که هنگام ثبت سفارش انتخاب می‌کنید.</p>
                <h2>ارسال رایگان</h2>
                <p>سفارش‌هایی که مبلغ کالاهای آن‌ها بیشتر از سقف ارسال رایگان باشد، بدون هزینه ارسال می‌شوند.</p>
                """),
            Page("payment-guide", "شیوه‌های پرداخت", SitePage.PaymentGuide, 7,
                "روش‌های پرداخت در فراتک شاپ: درگاه اینترنتی، کیف پول و پرداخت در محل.",
                """
                <h2>پرداخت اینترنتی</h2>
                <p>پرداخت با تمامی کارت‌های عضو شتاب از طریق درگاه امن زرین‌پال.</p>
                <h2>کیف پول</h2>
                <p>می‌توانید کیف پول خود را شارژ کنید و سفارش را مستقیماً از موجودی آن پرداخت کنید. مبالغ بازگشتی سفارش‌های لغوشده نیز به کیف پول واریز می‌شود.</p>
                <h2>پرداخت در محل</h2>
                <p>در صورت فعال بودن، برای روش‌های ارسالی که از آن پشتیبانی می‌کنند امکان پرداخت هنگام تحویل وجود دارد.</p>
                """),
            Page("how-to-buy", "راهنمای خرید", SitePage.ContentPage, 8,
                "مراحل خرید از فروشگاه، از انتخاب کالا تا تحویل سفارش، به زبان ساده.",
                """
                <h2>۱. انتخاب کالا</h2>
                <p>از منوی «فروشگاه» یا جست‌وجوی بالای صفحه کالای موردنظر را پیدا کنید. مشخصات، تصاویر، دیدگاه خریداران و گارانتی هر کالا در صفحهٔ آن آمده است و می‌توانید چند کالا را با هم مقایسه کنید.</p>
                <h2>۲. افزودن به سبد خرید</h2>
                <p>دکمهٔ «افزودن به سبد خرید» را بزنید. در سبد خرید می‌توانید تعداد را تغییر دهید و کد تخفیف را وارد کنید.</p>
                <h2>۳. ورود یا ثبت‌نام</h2>
                <p>شمارهٔ موبایل خود را وارد کنید تا کد تأیید برایتان پیامک شود؛ با همین کد وارد می‌شوید و اگر حساب نداشته باشید، خودکار ساخته می‌شود. بعداً می‌توانید از پروفایل رمز عبور هم تعیین کنید.</p>
                <h2>۴. آدرس، روش ارسال و پرداخت</h2>
                <p>آدرس گیرنده را انتخاب یا ثبت کنید، روش ارسال (و در صورت نیاز روز و بازهٔ تحویل) را برگزینید و با درگاه بانکی، کیف پول یا پرداخت در محل سفارش را نهایی کنید.</p>
                <h2>۵. پیگیری سفارش</h2>
                <p>وضعیت سفارش، کد رهگیری مرسوله و فاکتور از بخش «سفارش‌های من» در پروفایل در دسترس است و هر تغییر وضعیت برای شما اعلان می‌شود.</p>
                """),
            Page("club-rules", "قوانین باشگاه مشتریان", SitePage.ContentPage, 9,
                "نحوهٔ گرفتن امتیاز، سطح‌های باشگاه و تبدیل امتیاز به جایزه.",
                """
                <h2>گرفتن امتیاز</h2>
                <p>با خرید، ثبت دیدگاه، معرفی دوستان و فعالیت‌های دیگری که در صفحهٔ باشگاه آمده امتیاز می‌گیرید. امتیاز خرید پس از تحویل سفارش به حساب شما اضافه می‌شود.</p>
                <h2>سطح‌ها</h2>
                <p>مجموع امتیازهایی که تاکنون گرفته‌اید سطح شما را مشخص می‌کند؛ در سطح‌های بالاتر امتیاز هر خرید بیشتر است و جوایز ویژه باز می‌شود.</p>
                <h2>جوایز</h2>
                <ul>
                  <li>امتیاز را می‌توانید با اعتبار خرید، کد تخفیف یا کد ارسال رایگان عوض کنید.</li>
                  <li>اعتبار هدیه و تبلیغاتی فقط برای خرید از فروشگاه قابل استفاده است و قابل برداشت نقدی نیست.</li>
                  <li>امتیازها پس از مدت اعلام‌شده در صفحهٔ باشگاه منقضی می‌شوند.</li>
                </ul>
                <h2>استفادهٔ منصفانه</h2>
                <p>امتیاز خرید فقط برای سفارش‌های تحویل‌شده داده می‌شود. ساخت حساب‌های متعدد یا هر روش غیرعادی برای گرفتن امتیاز، باعث باطل شدن امتیازها می‌شود.</p>
                """));

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedFaqAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (await context.FaqCategories.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        var orders = new FaqCategory { Title = "ثبت و پیگیری سفارش", Slug = "orders", IconKey = "Orders", DisplayOrder = 1 };
        var payment = new FaqCategory { Title = "پرداخت و کیف پول", Slug = "payment", IconKey = "Wallet", DisplayOrder = 2 };
        var delivery = new FaqCategory { Title = "ارسال و تحویل", Slug = "delivery", IconKey = "Truck", DisplayOrder = 3 };
        var returns = new FaqCategory { Title = "بازگشت کالا و گارانتی", Slug = "returns", IconKey = "Shield", DisplayOrder = 4 };
        context.FaqCategories.AddRange(orders, payment, delivery, returns);

        // Questions created before FAQ groups existed are kept and attached to the first group.
        var existing = await context.FaqItems.AsTracking().Where(f => f.CategoryId == null).ToListAsync(cancellationToken);
        foreach (var item in existing)
            item.Category = orders;

        if (existing.Count == 0)
        {
            var faqs = new (FaqCategory Category, string Q, string A)[]
            {
                (orders, "چگونه سفارش خود را ثبت کنم؟", "محصول مورد نظر را به سبد خرید اضافه کنید، وارد حساب کاربری شوید و پس از انتخاب آدرس، روش ارسال و روش پرداخت، سفارش را نهایی کنید."),
                (orders, "چگونه سفارش خود را پیگیری کنم؟", "از بخش «سفارش‌های من» در پروفایل کاربری می‌توانید وضعیت و کد رهگیری سفارش را مشاهده کنید."),
                (orders, "آیا می‌توانم سفارشم را لغو کنم؟", "تا پیش از شروع آماده‌سازی سفارش، از صفحه جزئیات سفارش امکان لغو وجود دارد. مبلغ سفارش‌های پرداخت‌شده به کیف پول شما بازمی‌گردد."),
                (payment, "چه روش‌های پرداختی وجود دارد؟", "پرداخت اینترنتی از طریق درگاه زرین‌پال، پرداخت از موجودی کیف پول و در صورت فعال بودن، پرداخت در محل."),
                (payment, "کیف پول چیست و چطور شارژ می‌شود؟", "کیف پول حساب اعتباری شما در فروشگاه است. از بخش کیف پول در پروفایل می‌توانید آن را به‌صورت آنلاین شارژ کنید یا درخواست برداشت موجودی به حساب بانکی ثبت کنید."),
                (payment, "اگر پرداخت ناموفق بود و از حسابم کم شد چه کنم؟", "مبالغ کسرشده در پرداخت‌های ناموفق حداکثر تا ۷۲ ساعت توسط بانک به حساب شما بازمی‌گردد."),
                (delivery, "هزینه ارسال چقدر است؟", "هزینه ارسال بر اساس روش ارسال انتخابی محاسبه می‌شود و برای سفارش‌های بالای سقف ارسال رایگان، رایگان است."),
                (delivery, "زمان تحویل سفارش چقدر است؟", "سفارش‌های تهران با پیک فروشگاه در روز و بازه انتخابی و سایر شهرها با پست پیشتاز ۳ تا ۵ روز کاری تحویل می‌شوند."),
                (returns, "شرایط مرجوع کردن کالا چیست؟", "تا ۷ روز پس از تحویل، در صورت سالم بودن کالا و بسته‌بندی، امکان مرجوع کردن وجود دارد."),
                (returns, "آیا کالاها گارانتی دارند؟", "تمامی کالاها دارای گارانتی معتبر هستند و مدت آن در صفحه هر محصول درج شده است."),
            };

            for (var i = 0; i < faqs.Length; i++)
            {
                context.FaqItems.Add(new FaqItem
                {
                    Category = faqs[i].Category,
                    Question = faqs[i].Q,
                    Answer = faqs[i].A,
                    DisplayOrder = i + 1,
                });
            }
        }

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedHomeSectionsAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (await context.HomeSections.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        context.HomeSections.AddRange(
            new HomeSection { Type = HomeSectionType.HeroSlider, Title = "اسلایدر اصلی", ItemCount = 10, DisplayOrder = 1 },
            new HomeSection { Type = HomeSectionType.Categories, Title = "دسته بندی محصولات", ItemCount = 20, DisplayOrder = 2 },
            new HomeSection { Type = HomeSectionType.Deals, Title = "تخفیف های ویژه زمان دار", Subtitle = "پیشنهادهای شگفت انگیز امروز", LinkUrl = "/search?discounted=true", ItemCount = 10, DisplayOrder = 3 },
            new HomeSection { Type = HomeSectionType.PromoBanners, Title = "پیشنهادهای ویژه", ItemCount = 2, DisplayOrder = 4 },
            new HomeSection { Type = HomeSectionType.BestSellers, Title = "پرفروش ترین محصولات", LinkUrl = "/search?sort=BestSelling", ItemCount = 10, DisplayOrder = 5 },
            new HomeSection { Type = HomeSectionType.WideBanners, Title = "بنر تبلیغاتی", ItemCount = 1, DisplayOrder = 6 },
            new HomeSection { Type = HomeSectionType.Newest, Title = "جدیدترین محصولات", LinkUrl = "/search?sort=Newest", ItemCount = 10, DisplayOrder = 7 },
            new HomeSection { Type = HomeSectionType.MostViewed, Title = "پربازدیدترین محصولات", LinkUrl = "/search?sort=MostViewed", ItemCount = 10, DisplayOrder = 8, IsActive = false },
            new HomeSection { Type = HomeSectionType.Features, Title = "چرا فراتک شاپ؟", ItemCount = 4, DisplayOrder = 9 },
            new HomeSection { Type = HomeSectionType.Brands, Title = "محبوب ترین برندها", ItemCount = 20, DisplayOrder = 10 },
            new HomeSection { Type = HomeSectionType.LatestPosts, Title = "آخرین مقالات", LinkUrl = "/blog", ItemCount = 4, DisplayOrder = 11 });

        await context.SaveChangesAsync(cancellationToken);
    }

    private static async Task SeedFeaturesAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (await context.SiteFeatures.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        context.SiteFeatures.AddRange(
            new SiteFeature { Title = "پشتیبانی همه روزه", Description = "پاسخگویی سریع در تمام روزهای هفته", IconKey = "Headset", DisplayOrder = 1 },
            new SiteFeature { Title = "پرداخت امن", Description = "درگاه امن، کیف پول و پرداخت در محل", IconKey = "CreditCard", DisplayOrder = 2 },
            new SiteFeature { Title = "ضمانت اصالت کالا", Description = "تضمین اصل بودن و سلامت فیزیکی کالا", IconKey = "Shield", DisplayOrder = 3 },
            new SiteFeature { Title = "ارسال به کل ایران", Description = "ارسال سریع با پست و پیک اختصاصی", IconKey = "Truck", DisplayOrder = 4 });

        await context.SaveChangesAsync(cancellationToken);
    }

    internal static async Task SeedTrustBadgesAsync(FratakShopDbContext context, CancellationToken cancellationToken)
    {
        if (await context.TrustBadges.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        context.TrustBadges.AddRange(
            new TrustBadge { Title = "نماد اعتماد الکترونیکی", ImageUrl = "/assets/image/service/symbol-01.png", DisplayOrder = 1 },
            new TrustBadge { Title = "نشان ملی ثبت رسانه‌های دیجیتال", ImageUrl = "/assets/image/service/symbol-02.png", DisplayOrder = 2 });

        await context.SaveChangesAsync(cancellationToken);
    }

    private static ContentPage Page(string slug, string title, SitePage key, int order, string summary, string html, string? image = null) => new()
    {
        Slug = slug,
        Title = title,
        PageKey = key,
        DisplayOrder = order,
        Summary = summary,
        Content = html.Trim(),
        ImageUrl = image,
        ShowInFooter = true,
        IsActive = true,
        Seo = new SeoMetaData { MetaDescription = summary },
    };
}
