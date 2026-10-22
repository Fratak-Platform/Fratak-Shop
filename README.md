# Fratak Shop

فروشگاه اینترنتی فارسی (RTL) با **ASP.NET Core 10 Razor Pages**، **EF Core 10 / SQL Server** و معماری سه‌لایه.

## معماری

```
src/
├── Fratak-Shop.Data   → لایه داده: Entity ها، Configuration ها، DbContext، Migration ها، Seeder داده مرجع (استان/شهر)
├── Fratak-Shop.Core   → لایه سرویس: DTO ها، Mapper ها، سرویس‌ها (قوانین کسب‌وکار، اعتبارسنجی، OperationResult)
└── Fratak-Shop.Web    → لایه وب: Razor Pages فروشگاه، سرویس‌های نمایشی، ViewComponent ها، استایل و اسکریپت
```

| لایه | وابستگی | مسئولیت |
|------|---------|---------|
| Web  | Core, Data | صفحات، احراز هویت با کوکی، سبد خرید مهمان، سئو، درگاه پرداخت |
| Core | Data | منطق کسب‌وکار؛ صفحات هرگز مستقیم به DbContext دسترسی ندارند |
| Data | — | مدل داده و دیتابیس |

### لایه وب (`Fratak-Shop.Web`)

| مسیر | توضیح |
|------|-------|
| `Infrastructure/` | `ShopOptions` (تنظیمات فروشگاه)، `ShopPageModel` (کلاس پایه صفحات)، میان‌افزار ریدایرکت سئو، Seeder داده نمایشی |
| `Services/` | سبد خرید (کاربر/مهمان با کوکی + ادغام پس از ورود)، ورود/خروج، لیست مقایسه، متادیتای سئو، کد یک‌بارمصرف و پیامک، درگاه پرداخت |
| `ViewComponents/` | هدر سایت (مگامنو دسته‌بندی‌ها با کش، مینی‌سبد، منوی حساب) |
| `Helpers/` | قالب‌بندی قیمت و تاریخ شمسی، برچسب‌های وضعیت، آدرس تصاویر، مجموعه آیکون‌های قالب |
| `Pages/` | صفحات فروشگاه و ناحیه کاربری (`Pages/Profile`) و پرداخت (`Pages/Payment`) |

## صفحات و مسیرها

| مسیر | صفحه |
|------|------|
| `/` | صفحه اصلی (اسلایدر، دسته‌بندی‌ها، شگفت‌انگیزها با شمارش معکوس، پرفروش‌ها، جدیدترین‌ها، برندها، مقالات) |
| `/search` ، `/products` ، `/category/{slug}` ، `/brand/{slug}` | لیست محصولات با جستجو، فیلتر دسته‌بندی/برند/قیمت/موجودی/تخفیف، مرتب‌سازی و صفحه‌بندی |
| `/product/{slug}` | جزئیات محصول، گالری، مشخصات، دیدگاه‌ها و پرسش‌ها، محصولات مرتبط، علاقه‌مندی و مقایسه |
| `/compare` | مقایسه تا ۴ محصول |
| `/cart` ، `/checkout` | سبد خرید و تکمیل سفارش (آدرس، روز تحویل، روش ارسال، روش پرداخت) |
| `/payment/*` ، `/checkout/success/{no}` ، `/checkout/failed/{no}` | پرداخت و نتیجه سفارش |
| `/login` ، `/register` ، `/verify` ، `/welcome` | ورود، ثبت‌نام، تایید شماره موبایل با کد یک‌بارمصرف |
| `/profile` ، `/profile/orders` ، `/profile/orders/{no}` ، `/profile/addresses` ، `/profile/favorites` ، `/profile/messages` ، `/profile/info` ، `/profile/password` | ناحیه کاربری |
| `/blog` ، `/blog/category/{slug}` ، `/blog/{slug}` | وبلاگ |
| `/faq` ، `/contact-us` ، `/about-us` ، `/rules` ، `/coming-soon` | صفحات محتوایی |

## اجرا

پیش‌نیاز: .NET SDK 10 و SQL Server.

```bash
# رشته اتصال در src/Fratak-Shop.Web/appsettings.json (کلید ConnectionDefault)
dotnet run --project src/Fratak-Shop.Web
```

در اولین اجرا Migration ها اعمال می‌شوند و دیکشنری استان/شهر ثبت می‌شود. در محیط **Development** داده نمایشی
(دسته‌بندی، برند، محصول، بنر، مقاله، سوالات متداول و یک سفارش نمونه) هم ساخته می‌شود:

| نقش | موبایل | رمز عبور |
|-----|--------|----------|
| مشتری نمونه | `09120000000` | `123456` |

## تنظیمات (`appsettings.json` → بخش `Shop`)

| کلید | توضیح |
|------|-------|
| `SiteName`, `Tagline` | نام و شعار فروشگاه (در هدر، فوتر، عنوان صفحات و سئو) |
| `SupportPhone`, `SupportEmail`, `Address`, `WorkingHours` | اطلاعات تماس فوتر و صفحه تماس |
| `ShippingMethods` | روش‌های ارسال (کد، عنوان، هزینه، حداقل روز تحویل) |
| `FreeShippingThreshold` | سقف ارسال رایگان (۰ = غیرفعال) |
| `SeedDemoData` | ساخت داده نمایشی روی دیتابیس خالی |
| `EnableSandboxGateway` | فعال بودن درگاه پرداخت آزمایشی |

## نقاط توسعه

* **درگاه پرداخت:** `Services/Payments/IPaymentGateway` — پیاده‌سازی فعلی `SandboxPaymentGateway` است؛ برای زرین‌پال یا هر درگاه دیگر کافی است پیاده‌سازی جدید را در `WebServiceCollectionExtensions` ثبت کنید.
* **پیامک:** `Services/ISmsSender` — پیاده‌سازی فعلی پیامک را در لاگ می‌نویسد (و در Development کد تایید روی صفحه نمایش داده می‌شود).
* **تصاویر:** نام فایل‌های آپلودی از مسیر `/uploads/{products|categories|blogs|banners|brands}/` خوانده می‌شود (`Helpers/ImageHelper`).

## استایل (Tailwind CSS)

فایل `wwwroot/assets/css/output.css` از روی کلاس‌های استفاده‌شده در صفحات ساخته می‌شود:

```bash
cd src/Fratak-Shop.Web
npm install
npm run build:css     # یا npm run watch:css هنگام توسعه
```

## Migration

```bash
dotnet ef migrations add <Name> --project src/Fratak-Shop.Data --startup-project src/Fratak-Shop.Data
```

(`FratakShopDbContextFactory` رشته اتصال طراحی را از متغیر محیطی `FRATAK_SHOP_CONNECTION` می‌خواند.)

## CI

* `Build` — بیلد کامل Solution.
* `Verify` — بیلد هر پروژه با گزارش کامل خطاها، و اجرای برنامه روی SQL Server واقعی همراه با **تست دود سرتاسری**
  (`.github/scripts/smoke-test.sh`): همه صفحات عمومی، ورود، سبد خرید، ثبت سفارش پرداخت در محل، پرداخت اینترنتی با درگاه آزمایشی،
  ثبت دیدگاه، علاقه‌مندی، فرم تماس و ثبت‌نام.
