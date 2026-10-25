using Fratak_Shop.Core.DTOs.Orders.Command;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Entities.Brands;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Wallets;
using Fratak_Shop.Data.Entities.Locations;
using Fratak_Shop.Data.Entities.Loyalty;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Infrastructure.Seeders;

/// <summary>
/// Fills an empty database with a realistic demo catalog so the storefront can be
/// explored right after the first run: categories, brands, products (with images,
/// specifications and discounts), banners, blog posts, FAQs, approved reviews and
/// a demo customer (<c>09120000000</c> / <c>123456</c>) with an address and an order.
/// Runs only when <c>Shop:SeedDemoData</c> is enabled and no product exists yet.
/// </summary>
public class DemoDataSeeder(FratakShopDbContext context, IOrderService orderService, ILogger<DemoDataSeeder> logger)
{
    public const string DemoPhone = "09120000000";
    public const string DemoPassword = "123456";
    private const decimal DemoWalletBalance = 2_000_000;
    private const int DemoLoyaltyPoints = 1_500;

    private const string Img = "/assets/image/";

    public async Task SeedAsync(CancellationToken cancellationToken = default)
    {
        if (await context.Products.IgnoreQueryFilters().AnyAsync(cancellationToken))
            return;

        logger.LogInformation("Seeding storefront demo data ...");

        var now = DateTimeOffset.UtcNow;

        // ── Categories ────────────────────────────────────────────────
        var categories = new Dictionary<string, Category>
        {
            ["headphone"] = Cat("هدفون و هدست", "headphones", "category/1.webp", 1),
            ["watch"] = Cat("ساعت هوشمند", "smart-watch", "category/2.webp", 2),
            ["gaming"] = Cat("کنسول و گیمینگ", "gaming", "category/3.webp", 3),
            ["laptop"] = Cat("لپ تاپ", "laptop", "category/4.webp", 4),
            ["earbuds"] = Cat("هندزفری بی‌سیم", "earbuds", "category/5.webp", 5),
            ["tablet"] = Cat("تبلت", "tablet", "category/6.webp", 6),
            ["network"] = Cat("تجهیزات شبکه", "network", "category/7.webp", 7),
            ["charger"] = Cat("شارژر و پاوربانک", "power-bank", "category/8.webp", 8),
            ["speaker"] = Cat("اسپیکر", "speaker", "category/9.webp", 9),
            ["storage"] = Cat("حافظه و هارد", "storage", "category/10.webp", 10),
            ["home"] = Cat("لوازم خانگی هوشمند", "smart-home", "products/6.webp", 11),
            ["tv"] = Cat("تلویزیون", "tv", "products/7.webp", 12),
            ["accessory"] = Cat("لوازم جانبی خودرو", "car-accessories", "products/4.webp", 13),
        };

        // A few sub-categories so the mega menu has a second level.
        categories["gaming-pc"] = Cat("کیس و قطعات گیمینگ", "gaming-pc", "products/9.webp", 1, categories["gaming"]);
        categories["console"] = Cat("کنسول بازی", "console", "category/3.webp", 2, categories["gaming"]);
        categories["ultrabook"] = Cat("لپ تاپ حرفه‌ای", "ultrabook", "products/8.webp", 1, categories["laptop"]);
        categories["office-laptop"] = Cat("لپ تاپ اداری و دانشجویی", "office-laptop", "products/2.webp", 2, categories["laptop"]);

        context.Categories.AddRange(categories.Values);

        // ── Brands ────────────────────────────────────────────────────
        var brands = new Dictionary<string, Brand>
        {
            ["xiaomi"] = new() { Name = "شیائومی", EnglishName = "Xiaomi", Slug = "xiaomi", LogoName = Img + "brands/1.png", DisplayOrder = 1 },
            ["samsung"] = new() { Name = "سامسونگ", EnglishName = "Samsung", Slug = "samsung", LogoName = Img + "brands/2.png", DisplayOrder = 2 },
            ["apple"] = new() { Name = "اپل", EnglishName = "Apple", Slug = "apple", LogoName = Img + "brands/3.png", DisplayOrder = 3 },
            ["nokia"] = new() { Name = "نوکیا", EnglishName = "Nokia", Slug = "nokia", LogoName = Img + "brands/4.png", DisplayOrder = 4 },
            ["honor"] = new() { Name = "آنر", EnglishName = "Honor", Slug = "honor", LogoName = Img + "brands/5.jpg", DisplayOrder = 5 },
        };

        context.Brands.AddRange(brands.Values);

        // ── Products ──────────────────────────────────────────────────
        var products = new List<Product>
        {
            Prod("ساعت هوشمند شیائومی مدل Redmi Watch 3 Active", "Xiaomi Redmi Watch 3 Active", "xiaomi-redmi-watch-3-active", "XM-RW3A",
                categories["watch"], brands["xiaomi"], 3_450_000, 2_890_000, 24, 18, now.AddDays(3), 340, 61,
                ["products/1.webp", "products/1a.webp", "products/1b.webp", "products/1c.webp", "products/1d.webp"],
                [("اندازه صفحه", "۱.۸۳ اینچ"), ("نوع صفحه", "LCD"), ("عمر باتری", "تا ۱۲ روز"), ("مقاومت در برابر آب", "5ATM"), ("سنسورها", "ضربان قلب، اکسیژن خون"), ("اتصال", "بلوتوث ۵.۳")]),
            Prod("لپ تاپ ۱۶ اینچی آنر مدل MagicBook X16", "Honor MagicBook X16 i5 16GB 512GB", "honor-magicbook-x16", "HN-MBX16",
                categories["office-laptop"], brands["honor"], 42_900_000, 39_500_000, 8, 12, now.AddDays(5), 210, 14,
                ["products/2.webp"],
                [("پردازنده", "Core i5-12450H"), ("حافظه رم", "۱۶ گیگابایت"), ("حافظه داخلی", "۵۱۲ گیگابایت SSD"), ("اندازه صفحه", "۱۶ اینچ"), ("وزن", "۱.۷۵ کیلوگرم")]),
            Prod("هندزفری بی‌سیم شیائومی مدل Haylou T15", "Haylou T15 True Wireless", "haylou-t15", "XM-HT15",
                categories["earbuds"], brands["xiaomi"], 1_150_000, 890_000, 60, 6, now.AddDays(2), 520, 133,
                ["products/3.webp"],
                [("نسخه بلوتوث", "۵.۰"), ("عمر باتری", "۵ ساعت (۱۰۰ ساعت با کیس)"), ("مقاومت در برابر آب", "IPX5"), ("نوع اتصال", "بی‌سیم")]),
            Prod("پایه نگهدارنده موبایل خودرو مدل PicOne", "PicOne Car Phone Holder", "picone-car-holder", "AC-PIC1",
                categories["accessory"], null, 450_000, null, 0, 0, null, 95, 40,
                ["products/4.webp"],
                [("محل نصب", "داشبورد و شیشه"), ("جنس", "پلاستیک ABS"), ("سازگاری", "گوشی‌های ۴ تا ۷ اینچ")]),
            Prod("هدفون بی‌سیم نوکیا مدل E1200", "Nokia Essential Wireless Headphones E1200", "nokia-e1200", "NK-E1200",
                categories["headphone"], brands["nokia"], 2_100_000, 1_790_000, 15, 12, now.AddDays(4), 160, 22,
                ["products/5.webp"],
                [("نوع اتصال", "بلوتوث ۵.۰"), ("عمر باتری", "۴۰ ساعت"), ("وزن", "۲۰۰ گرم"), ("میکروفون", "دارد")]),
            Prod("جاروبرقی رباتیک شیائومی مدل Roborock S8", "Roborock S8", "roborock-s8", "XM-RRS8",
                categories["home"], brands["xiaomi"], 48_500_000, null, 5, 24, null, 410, 9,
                ["products/6.webp"],
                [("قدرت مکش", "۶۰۰۰ پاسکال"), ("ظرفیت باتری", "۵۲۰۰ میلی‌آمپر ساعت"), ("قابلیت طی کشیدن", "دارد"), ("مسیریابی", "LiDAR")]),
            Prod("تلویزیون هوشمند سامسونگ ۴۳ اینچ مدل CU7000", "Samsung 43\" Crystal UHD CU7000", "samsung-43-cu7000", "SM-43CU7",
                categories["tv"], brands["samsung"], 27_800_000, 25_900_000, 7, 24, now.AddDays(6), 275, 18,
                ["products/7.webp"],
                [("اندازه صفحه", "۴۳ اینچ"), ("کیفیت تصویر", "4K UHD"), ("سیستم عامل", "Tizen"), ("HDR", "HDR10+")]),
            Prod("لپ تاپ ۱۴ اینچی اپل مدل MacBook Pro M3", "Apple MacBook Pro 14 M3 8GB 512GB", "apple-macbook-pro-m3", "AP-MBPM3",
                categories["ultrabook"], brands["apple"], 98_000_000, null, 4, 12, null, 690, 11,
                ["products/8.webp"],
                [("پردازنده", "Apple M3"), ("حافظه رم", "۸ گیگابایت"), ("حافظه داخلی", "۵۱۲ گیگابایت SSD"), ("اندازه صفحه", "۱۴.۲ اینچ Liquid Retina XDR"), ("عمر باتری", "تا ۲۲ ساعت")]),
            Prod("کیس گیمینگ اسمبل شده مدل Fratak Storm", "Fratak Storm Gaming PC RTX 4070", "fratak-storm-gaming-pc", "PC-STORM",
                categories["gaming-pc"], null, 115_000_000, 108_000_000, 3, 12, now.AddDays(7), 380, 5,
                ["products/9.webp"],
                [("پردازنده", "Core i7-14700K"), ("کارت گرافیک", "RTX 4070 Super"), ("حافظه رم", "۳۲ گیگابایت DDR5"), ("حافظه داخلی", "۱ ترابایت NVMe")]),
            Prod("هدفون اپل مدل AirPods Max", "Apple AirPods Max", "apple-airpods-max", "AP-APMAX",
                categories["headphone"], brands["apple"], 39_900_000, null, 6, 12, null, 450, 16,
                ["category/1.webp"],
                [("نوع اتصال", "بلوتوث ۵.۰"), ("حذف نویز", "فعال (ANC)"), ("عمر باتری", "۲۰ ساعت")]),
            Prod("ساعت هوشمند سامسونگ مدل Galaxy Watch4 Classic", "Samsung Galaxy Watch4 Classic 46mm", "samsung-galaxy-watch4-classic", "SM-GW4C",
                categories["watch"], brands["samsung"], 9_800_000, 8_700_000, 11, 18, now.AddDays(1), 300, 37,
                ["category/2.webp"],
                [("اندازه صفحه", "۱.۴ اینچ"), ("نوع صفحه", "Super AMOLED"), ("سیستم عامل", "Wear OS"), ("مقاومت در برابر آب", "5ATM")]),
            Prod("کنسول بازی سونی مدل PlayStation 5", "Sony PlayStation 5 Standard", "sony-playstation-5", "SN-PS5",
                categories["console"], null, 36_500_000, null, 0, 12, null, 820, 48,
                ["category/3.webp"],
                [("حافظه داخلی", "۸۲۵ گیگابایت SSD"), ("رزولوشن خروجی", "تا 8K"), ("درایو نوری", "دارد")]),
            Prod("لپ تاپ ۱۵.۶ اینچی اچ پی مدل Victus 15", "HP Victus 15 i5 RTX 3050", "hp-victus-15", "HP-VIC15",
                categories["laptop"], null, 51_000_000, 47_900_000, 9, 12, now.AddDays(3), 240, 20,
                ["category/4.webp"],
                [("پردازنده", "Core i5-12450H"), ("کارت گرافیک", "RTX 3050"), ("حافظه رم", "۸ گیگابایت"), ("اندازه صفحه", "۱۵.۶ اینچ 144Hz")]),
            Prod("هندزفری بی‌سیم اپل مدل AirPods Pro 2", "Apple AirPods Pro 2nd Gen", "apple-airpods-pro-2", "AP-APP2",
                categories["earbuds"], brands["apple"], 14_500_000, 12_900_000, 20, 12, now.AddDays(2), 610, 92,
                ["category/5.webp"],
                [("حذف نویز", "فعال (ANC)"), ("عمر باتری", "۶ ساعت"), ("مقاومت در برابر آب", "IPX4"), ("پورت شارژ", "USB-C")]),
            Prod("تبلت اپل مدل iPad Air 5 Wi-Fi", "Apple iPad Air 5 64GB", "apple-ipad-air-5", "AP-IPA5",
                categories["tablet"], brands["apple"], 32_000_000, null, 10, 12, null, 330, 27,
                ["category/6.webp"],
                [("پردازنده", "Apple M1"), ("اندازه صفحه", "۱۰.۹ اینچ"), ("حافظه داخلی", "۶۴ گیگابایت")]),
            Prod("مودم روتر بی‌سیم تی پی لینک مدل Archer C50", "TP-Link Archer C50 AC1200", "tp-link-archer-c50", "TP-AC50",
                categories["network"], null, 1_950_000, null, 35, 24, null, 120, 54,
                ["category/7.webp"],
                [("سرعت", "تا ۱۲۰۰ مگابیت"), ("تعداد آنتن", "۴"), ("باند", "دو بانده")]),
            Prod("پاوربانک بی‌سیم شیائومی ۱۰۰۰۰ میلی‌آمپر", "Xiaomi Magnetic Wireless Power Bank 10000mAh", "xiaomi-wireless-power-bank", "XM-PB10",
                categories["charger"], brands["xiaomi"], 1_690_000, 1_390_000, 45, 6, now.AddDays(5), 140, 70,
                ["category/8.webp"],
                [("ظرفیت", "۱۰۰۰۰ میلی‌آمپر ساعت"), ("شارژ بی‌سیم", "۷.۵ وات"), ("خروجی", "USB-C 20W")]),
            Prod("اسپیکر بلوتوثی تسکو مدل TS 2399", "TSCO TS 2399 Bluetooth Speaker", "tsco-ts-2399", "TS-2399",
                categories["speaker"], null, 2_350_000, null, 18, 18, null, 85, 31,
                ["category/9.webp"],
                [("توان خروجی", "۲۰ وات"), ("عمر باتری", "۸ ساعت"), ("ورودی", "بلوتوث، فلش، رم")]),
            Prod("هارد اکسترنال ای دیتا مدل HD710 Pro ظرفیت ۱ ترابایت", "ADATA HD710 Pro 1TB", "adata-hd710-pro-1tb", "AD-HD710",
                categories["storage"], null, 2_790_000, 2_490_000, 25, 36, now.AddDays(4), 190, 66,
                ["category/10.webp"],
                [("ظرفیت", "۱ ترابایت"), ("رابط", "USB 3.2"), ("مقاومت", "ضد آب و ضربه IP68")]),
        };

        context.Products.AddRange(products);

        // ── Banners ───────────────────────────────────────────────────
        for (var i = 1; i <= 5; i++)
        {
            context.Banners.Add(new Banner
            {
                Title = $"اسلاید {i}",
                ImageName = Img + (i == 2 ? "heroSlider/2.gif" : $"heroSlider/{i}.webp"),
                ImageAlt = "پیشنهادهای ویژه",
                LinkUrl = "/search?discounted=true",
                Position = BannerPosition.HeroSlider,
                DisplayOrder = i,
            });
        }

        context.Banners.Add(new Banner
        {
            Title = "بنر میانی",
            ImageName = Img + "banner/1.webp",
            ImageAlt = "تخفیف‌های ویژه",
            LinkUrl = "/search?sort=MostDiscounted",
            Position = BannerPosition.WideBanner,
            DisplayOrder = 1,
        });

        context.Banners.Add(new Banner
        {
            Title = "MacBook Pro 2024",
            Subtitle = "چرا از نسخه های جدید استفاده نمیکنی؟",
            ImageName = Img + "products/8.webp",
            ButtonText = "مشاهده محصول",
            LinkUrl = "/product/apple-macbook-pro-m3",
            Position = BannerPosition.PromoCard,
            DisplayOrder = 1,
        });

        context.Banners.Add(new Banner
        {
            Title = "Gaming Case 2024",
            Subtitle = "کیس های اسمبل شده گیمینگ با طراحی زیبا",
            ImageName = Img + "products/9.webp",
            ButtonText = "مشاهده محصول",
            LinkUrl = "/product/fratak-storm-gaming-pc",
            Position = BannerPosition.PromoCard,
            DisplayOrder = 2,
        });

        // ── Users ─────────────────────────────────────────────────────
        var demoUser = await context.Users.FirstOrDefaultAsync(x => x.PhoneNumber == DemoPhone, cancellationToken);
        if (demoUser is null)
        {
            demoUser = new User
            {
                PhoneNumber = DemoPhone,
                Email = "demo@fratak.shop",
                FirstName = "کاربر",
                LastName = "نمونه",
                PasswordHash = PasswordHasher.Hash(DemoPassword),
                Role = UserRole.Customer,
                IsPhoneVerified = true,
                WalletBalance = DemoWalletBalance,
                LoyaltyPoints = DemoLoyaltyPoints,
                LoyaltyLifetimePoints = DemoLoyaltyPoints,
            };
            context.Users.Add(demoUser);

            // Club points to try the rewards (500 → wallet credit, 800 → free shipping, 1000 → 10%).
            context.LoyaltyTransactions.Add(new LoyaltyTransaction
            {
                User = demoUser,
                Type = LoyaltyTransactionType.Earn,
                Points = DemoLoyaltyPoints,
                BalanceAfter = DemoLoyaltyPoints,
                Description = "امتیاز هدیه حساب نمونه",
                ReferenceKey = "demo:welcome",
                ExpiresAt = now.AddYears(1),
            });

            // Opening balance, so wallet payment can be tried right away (ledger stays consistent).
            context.WalletTransactions.Add(new WalletTransaction
            {
                User = demoUser,
                Type = WalletTransactionType.AdminCredit,
                Amount = DemoWalletBalance,
                BalanceAfter = DemoWalletBalance,
                Description = "اعتبار هدیه حساب نمونه",
            });
        }

        // Category-restricted coupon (headphones + earbuds), to try product/category conditions.
        if (!await context.Coupons.IgnoreQueryFilters().AnyAsync(c => c.Code == "AUDIO15", cancellationToken))
        {
            context.Coupons.Add(new Coupon
            {
                Code = "AUDIO15",
                Title = "۱۵٪ تخفیف هدفون و هندزفری",
                Description = "فقط روی کالاهای دسته هدفون و هندزفری بی‌سیم؛ حداکثر ۲ میلیون تومان تخفیف.",
                Type = CouponType.Percentage,
                Value = 15,
                MaxDiscountAmount = 2_000_000,
                UsageLimitPerUser = 2,
                EndsAt = now.AddMonths(3),
                IsPublic = true,
                Targets =
                [
                    new CouponTarget { TargetType = CouponTargetType.Category, TargetId = categories["headphone"].Id },
                    new CouponTarget { TargetType = CouponTargetType.Category, TargetId = categories["earbuds"].Id },
                ],
            });
        }

        var author = new User
        {
            PhoneNumber = "09120000001",
            FirstName = "تیم",
            LastName = "محتوا",
            Role = UserRole.Admin,
            IsPhoneVerified = true,
        };
        context.Users.Add(author);

        // ── Blog ──────────────────────────────────────────────────────
        var blogCategory = new BlogCategory { Name = "راهنمای خرید", Slug = "buying-guide", DisplayOrder = 1 };
        var newsCategory = new BlogCategory { Name = "اخبار تکنولوژی", Slug = "tech-news", DisplayOrder = 2 };
        context.BlogCategories.AddRange(blogCategory, newsCategory);

        var posts = new[]
        {
            ("۷ ترفند برای افزایش طول عمر هدفون", "headphone-life-tips", "blog/1.jpg", blogCategory,
                "اگر از جمله کاربرانی هستید که هدفون‌های شما بعد از حدود یک یا دو سال استفاده خراب می‌شوند، این مقاله برای شماست."),
            ("راهنمای خرید ساعت هوشمند در سال جدید", "smart-watch-buying-guide", "blog/2.jpg", blogCategory,
                "پیش از خرید ساعت هوشمند باید به عمر باتری، سازگاری با گوشی و سنسورهای سلامت توجه کنید."),
            ("چطور لپ تاپ مناسب کار و دانشگاه انتخاب کنیم؟", "choose-laptop-for-work", "blog/3.jpg", blogCategory,
                "پردازنده، حافظه و وزن سه فاکتور اصلی در انتخاب لپ تاپ اداری و دانشجویی هستند."),
            ("نسل جدید کنسول‌های بازی؛ چه انتظاری داشته باشیم؟", "next-gen-consoles", "blog/4.jpg", newsCategory,
                "کنسول‌های نسل بعد با تمرکز بر رهگیری پرتو و بارگذاری سریع، تجربه بازی را متحول می‌کنند."),
        };

        var postIndex = 0;
        var posted = new List<BlogPost>();
        foreach (var (title, slug, image, category, excerpt) in posts)
        {
            var post = new BlogPost
            {
                Author = author,
                BlogCategory = category,
                Title = title,
                Slug = slug,
                Excerpt = excerpt,
                Content = BuildArticle(title, excerpt),
                ImageName = Img + image,
                ImageAlt = title,
                ViewCount = 120 + postIndex * 37,
                IsPublished = true,
                PublishedAt = now.AddDays(-(postIndex * 4 + 1)),
            };
            posted.Add(post);
            context.BlogPosts.Add(post);
            postIndex++;
        }

        // FAQ, content pages and store settings are seeded by StoreDefaultsSeeder (Data layer).

        await context.SaveChangesAsync(cancellationToken);

        // ── Reviews (approved) ────────────────────────────────────────
        var reviewTexts = new (string Name, string Title, string Text, byte Rating)[]
        {
            ("علی رضایی", "کیفیت عالی", "نسبت به قیمتش واقعاً عالیه، ارسال هم خیلی سریع بود.", 5),
            ("مریم احمدی", "راضی هستم", "کیفیت ساخت خوبه ولی بسته‌بندی می‌تونست بهتر باشه.", 4),
            ("حسین کریمی", "پیشنهاد می‌کنم", "چند هفته‌ست استفاده می‌کنم و هیچ مشکلی نداشتم.", 5),
        };

        var reviewIndex = 0;
        foreach (var product in products.Take(10))
        {
            for (var r = 0; r < 2; r++)
            {
                var review = reviewTexts[(reviewIndex + r) % reviewTexts.Length];
                context.Comments.Add(new Comment
                {
                    UserId = demoUser.Id,
                    ProductId = product.Id,
                    Type = CommentType.Review,
                    AuthorName = review.Name,
                    AuthorPhone = DemoPhone,
                    Title = review.Title,
                    Content = review.Text,
                    Rating = review.Rating,
                    IsRecommended = review.Rating >= 4,
                    Status = CommentStatus.Approved,
                });
            }

            reviewIndex++;
        }

        context.Comments.Add(new Comment
        {
            UserId = demoUser.Id,
            BlogPostId = posted[0].Id,
            Type = CommentType.BlogComment,
            AuthorName = "کاربر نمونه",
            AuthorPhone = DemoPhone,
            Content = "مقاله خیلی مفیدی بود، ممنون از تیم محتوا.",
            Status = CommentStatus.Approved,
        });

        // ── Demo address ──────────────────────────────────────────────
        var tehranProvince = await context.Locations
            .FirstOrDefaultAsync(x => x.Type == LocationType.Province && x.Name == "تهران", cancellationToken);
        var tehranCity = tehranProvince is null
            ? null
            : await context.Locations.FirstOrDefaultAsync(x => x.ParentId == tehranProvince.Id && x.Name == "تهران", cancellationToken);

        if (tehranProvince is not null && tehranCity is not null)
        {
            context.UserAddresses.Add(new UserAddress
            {
                UserId = demoUser.Id,
                ProvinceId = tehranProvince.Id,
                CityId = tehranCity.Id,
                Title = "خانه",
                FirstName = demoUser.FirstName,
                LastName = demoUser.LastName,
                AddressLine = "خیابان ولیعصر، کوچه بهار",
                BuildingNumber = "۱۲",
                Unit = "۳",
                PostalCode = "1234567890",
                PhoneNumber = DemoPhone,
                IsDefault = true,
            });
        }

        await context.SaveChangesAsync(cancellationToken);

        // ── Demo order (through the order service: numbering, stock, totals) ──
        if (tehranProvince is not null && tehranCity is not null)
        {
            var orderResult = await orderService.CreateOrderAsync(new CreateOrderCommand
            {
                UserId = demoUser.Id,
                PaymentMethod = PaymentMethod.CashOnDelivery,
                ShippingMethod = "پست پیشتاز",
                ShippingAmount = 65_000,
                CustomerName = "کاربر نمونه",
                CustomerPhone = DemoPhone,
                ShippingProvince = tehranProvince.Name,
                ShippingCity = tehranCity.Name,
                ShippingAddress = "خیابان ولیعصر، کوچه بهار",
                ShippingBuildingNumber = "۱۲",
                ShippingUnit = "۳",
                ShippingPostalCode = "1234567890",
                ShippingPhone = DemoPhone,
                Items =
                [
                    new OrderItemRequest { ProductId = products[2].Id, Quantity = 1 },
                    new OrderItemRequest { ProductId = products[16].Id, Quantity = 2 },
                ],
            });

            if (orderResult.Status == EnOperationResultStatus.Success)
            {
                var orderId = orderResult.Data.Id;
                await orderService.ChangePaymentStatusAsync(orderId, PaymentStatus.Paid, "DEMO-0001");

                // The shop buys every order from a supplier: "processing" needs the purchase invoice.
                context.PurchaseInvoices.Add(new PurchaseInvoice
                {
                    OrderId = orderId,
                    SupplierName = "تأمین‌کننده نمونه",
                    InvoiceNumber = "DEMO-INV-0001",
                    Amount = Math.Round(orderResult.Data.PayableAmount * 0.8m, 0),
                    PurchasedAt = now,
                    FileName = Round6DemoSeeder.DemoInvoiceFileName,
                    ContentType = "application/pdf",
                    SizeBytes = Round6DemoSeeder.DemoInvoicePdf.Length,
                    Notes = "فاکتور خرید نمونه (داده آزمایشی)",
                    UploadedByName = "سیستم",
                });
                await context.SaveChangesAsync(cancellationToken);

                // Walk through the allowed workflow so the demo order ends up delivered.
                foreach (var status in new[] { OrderStatus.Confirmed, OrderStatus.Processing, OrderStatus.Shipped, OrderStatus.Delivered })
                    await orderService.ChangeOrderStatusAsync(orderId, status);

                await orderService.SetTrackingCodeAsync(orderId, "123456789012345678");
            }
        }

        logger.LogInformation("Demo data seeded: {Products} products, {Categories} categories.", products.Count, categories.Count);
    }

    private static Category Cat(string name, string slug, string image, int order, Category? parent = null) => new()
    {
        Name = name,
        Slug = slug,
        ImageName = Img + image,
        DisplayOrder = order,
        ParentCategory = parent,
        Description = $"خرید انواع {name} با بهترین قیمت و گارانتی معتبر",
    };

    private static Product Prod(
        string name, string englishName, string slug, string sku,
        Category category, Brand? brand,
        decimal price, decimal? discountPrice, int stock, int warrantyMonths, DateTimeOffset? discountEnd,
        int views, int sales,
        string[] images, (string Key, string Value)[] features)
    {
        return new Product
        {
            Category = category,
            Brand = brand,
            Name = name,
            EnglishName = englishName,
            Slug = slug,
            SKU = sku,
            ShortDescription = $"{name}؛ اصل و دارای گارانتی، ارسال سریع به سراسر کشور.",
            Description = $"<p>{name} یکی از محبوب‌ترین محصولات دسته {category.Name} است که با کیفیت ساخت بالا و امکانات کامل، انتخابی مطمئن برای خرید محسوب می‌شود.</p>"
                          + "<p>این محصول با ضمانت اصالت کالا و گارانتی معتبر عرضه می‌شود و امکان مرجوعی تا ۷ روز برای آن فراهم است.</p>",
            Price = price,
            DiscountPrice = discountPrice,
            DiscountStartAt = discountPrice.HasValue ? DateTimeOffset.UtcNow.AddDays(-1) : null,
            DiscountEndAt = discountPrice.HasValue ? discountEnd : null,
            StockQuantity = stock,
            WarrantyTitle = warrantyMonths > 0 ? $"گارانتی {warrantyMonths} ماهه" : null,
            WarrantyMonths = warrantyMonths > 0 ? warrantyMonths : null,
            IsActive = true,
            PublishedAt = DateTimeOffset.UtcNow.AddDays(-sales % 30),
            ViewCount = views,
            SalesCount = sales,
            Images = images.Select((image, index) => new ProductImage
            {
                ImageName = Img + image,
                AltText = name,
                DisplayOrder = index,
                IsPrimary = index == 0,
            }).ToList(),
            Features = features.Select((feature, index) => new ProductFeature
            {
                Key = feature.Key,
                Value = feature.Value,
                ValueType = FeatureValueType.Text,
                DisplayOrder = index,
            }).ToList(),
        };
    }

    private static string BuildArticle(string title, string excerpt) =>
        $"<p>{excerpt}</p>"
        + $"<h2>{title}</h2>"
        + "<p>در این مقاله به مهم‌ترین نکاتی که باید پیش از خرید و هنگام استفاده بدانید می‌پردازیم. انتخاب درست، علاوه بر صرفه‌جویی در هزینه، تجربه کاربری بهتری را برای شما رقم می‌زند.</p>"
        + "<h3>نکات کلیدی</h3>"
        + "<ul><li>نیاز واقعی خود را مشخص کنید.</li><li>مشخصات فنی را با دقت مقایسه کنید.</li><li>از فروشگاه معتبر با گارانتی رسمی خرید کنید.</li></ul>"
        + "<p>اگر سوالی دارید، در بخش دیدگاه‌ها با ما در میان بگذارید؛ کارشناسان ما پاسخگوی شما هستند.</p>";
}
