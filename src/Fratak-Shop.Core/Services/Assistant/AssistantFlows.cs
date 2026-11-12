using System.Globalization;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.DTOs.Products.Query;
using Fratak_Shop.Core.Services.Seo.Engine;

namespace Fratak_Shop.Core.Services.Assistant;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۴ – دو «جریان» کوچک دستیار (خالص و تست‌پذیر؛ بدون پایگاه داده):
//   • AssistantFinder : کالایاب مرحله‌ای «کمکم کن انتخاب کنم» ← دسته ← بودجه ← اولویت ← ۴ پیشنهاد.
//                        وضعیت جریان در خود دکمه‌ها (Flow) است؛ سرور چیزی نگه نمی‌دارد. مثال Flow: «finder:power-bank:2:best».
//   • AssistantPages  : پرسش‌های پیشنهادی هر صفحه (کالا، دسته، سبد، تسویه، صفحهٔ نخست، …) که با هر جابه‌جایی صفحه تازه می‌شوند.
//  محل استفاده: AssistantService (FinderAsync و SuggestAsync) و تست‌های Unit/AssistantFlowTests.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>یک بازهٔ قیمت (تومان). Max = null یعنی «به بالا».</summary>
public sealed record PriceBand(decimal Min, decimal? Max)
{
    /// <summary>متن دکمه. مثال: (0، 500000) ← «تا ۵۰۰ هزار تومان»؛ (2000000، null) ← «بیشتر از ۲ میلیون تومان».</summary>
    public string Label => Min <= 0
        ? $"تا {AssistantFinder.Short(Max ?? 0)}"
        : Max is null ? $"بیشتر از {AssistantFinder.Short(Min)}" : $"{AssistantFinder.Short(Min)} تا {AssistantFinder.Short(Max.Value)}";
}

/// <summary>وضعیت کالایاب. Band = ۰ یعنی «فرقی ندارد»؛ null یعنی هنوز پرسیده نشده.</summary>
public sealed record FinderState(string? CategorySlug, int? Band, string? Priority);

/// <summary>منطق خالص کالایاب.</summary>
public static partial class AssistantFinder
{
    /// <summary>پیشوند Flow.</summary>
    public const string Prefix = "finder";

    /// <summary>اولویت‌ها: کلید ← (عنوان دکمه، مرتب‌سازی).</summary>
    public static readonly IReadOnlyDictionary<string, (string Label, ProductSortBy Sort)> Priorities = new Dictionary<string, (string, ProductSortBy)>(StringComparer.Ordinal)
    {
        ["best"] = ("پرفروش‌ترین‌ها", ProductSortBy.BestSelling),
        ["cheap"] = ("ارزان‌ترین‌ها", ProductSortBy.Cheapest),
        ["deal"] = ("بیشترین تخفیف", ProductSortBy.MostDiscounted),
        ["new"] = ("جدیدترین‌ها", ProductSortBy.Newest),
    };

    /// <summary>
    /// خواندن Flow. نامعتبر ← null. مثال: «finder» ← (null، null، null)؛ «finder:power-bank:2:best» ← (power-bank، 2، best)؛
    /// «finder:<x>» یا «finder:a:9» ← null.
    /// </summary>
    public static FinderState? Parse(string? flow)
    {
        if (flow is null || flow.Length > 120)
            return null;
        var match = FlowRegex().Match(flow);
        if (!match.Success)
            return null;
        var slug = match.Groups["slug"].Success && match.Groups["slug"].Value.Length > 0 ? match.Groups["slug"].Value : null;
        int? band = match.Groups["band"].Success ? int.Parse(match.Groups["band"].Value, CultureInfo.InvariantCulture) : null;
        var priority = match.Groups["prio"].Success ? match.Groups["prio"].Value : null;
        if (priority is not null && !Priorities.ContainsKey(priority))
            return null;
        if (band is > 4)
            return null;
        return new FinderState(slug, band, priority);
    }

    /// <summary>ساخت Flow. مثال: Build("power-bank", 2, null) ← «finder:power-bank:2».</summary>
    public static string Build(string? slug, int? band = null, string? priority = null)
    {
        var flow = Prefix + ":" + (slug ?? "");
        if (band is { } b)
            flow += ":" + b.ToString(CultureInfo.InvariantCulture);
        if (band is not null && priority is not null)
            flow += ":" + priority;
        return flow;
    }

    /// <summary>
    /// ۴ بازهٔ قیمت از قیمت کالاهای موجود (چارک‌ها، گردشده به عدد خوانا). کمتر از ۴ کالا یا قیمت‌های خیلی نزدیک ← فهرست خالی (پرسش بودجه رد می‌شود).
    /// <para>مثال: قیمت‌های ۱۰۰ تا ۹۰۰ هزار ← «تا ۳۰۰ هزار»، «۳۰۰ تا ۵۰۰ هزار»، «۵۰۰ تا ۷۰۰ هزار»، «بیشتر از ۷۰۰ هزار».</para>
    /// </summary>
    public static IReadOnlyList<PriceBand> Bands(IEnumerable<decimal> prices)
    {
        var sorted = prices.Where(p => p > 0).OrderBy(p => p).ToList();
        if (sorted.Count < 4)
            return [];
        decimal At(double q) => sorted[(int)Math.Clamp(Math.Round(q * (sorted.Count - 1)), 0, sorted.Count - 1)];
        var cuts = new[] { Nice(At(0.25)), Nice(At(0.5)), Nice(At(0.75)) }.Distinct().Where(c => c > sorted[0] && c < sorted[^1]).ToList();
        if (cuts.Count < 2)
            return [];
        var bands = new List<PriceBand> { new(0, cuts[0]) };
        for (var i = 1; i < cuts.Count; i++)
            bands.Add(new PriceBand(cuts[i - 1], cuts[i]));
        bands.Add(new PriceBand(cuts[^1], null));
        return bands;
    }

    /// <summary>گرد کردن به دو رقم معنادار. مثال: ۴۸۷٬۰۰۰ ← ۴۹۰٬۰۰۰؛ ۱٬۲۳۴٬۰۰۰ ← ۱٬۲۰۰٬۰۰۰؛ ۹۵ ← ۹۵.</summary>
    public static decimal Nice(decimal value)
    {
        if (value < 100)
            return Math.Round(value);
        var digits = (int)Math.Floor(Math.Log10((double)value)) - 1;
        var unit = (decimal)Math.Pow(10, digits);
        return Math.Round(value / unit, MidpointRounding.AwayFromZero) * unit;
    }

    /// <summary>عدد کوتاه فارسی. مثال: ۵۰۰٬۰۰۰ ← «۵۰۰ هزار تومان»؛ ۲٬۵۰۰٬۰۰۰ ← «۲٫۵ میلیون تومان»؛ ۹۰۰ ← «۹۰۰ تومان».</summary>
    public static string Short(decimal value)
    {
        if (value >= 1_000_000)
            return Fa(Math.Round(value / 1_000_000m, 1).ToString("0.#", CultureInfo.InvariantCulture)) + " میلیون تومان";
        if (value >= 1_000)
            return Fa(Math.Round(value / 1_000m).ToString("0", CultureInfo.InvariantCulture)) + " هزار تومان";
        return Fa(Math.Round(value).ToString("0", CultureInfo.InvariantCulture)) + " تومان";
    }

    /// <summary>رقم انگلیسی به فارسی و ممیز به «٫». مثال: «2.5» ← «۲٫۵».</summary>
    private static string Fa(string text) =>
        new(text.Select(c => c is >= '0' and <= '9' ? (char)('۰' + (c - '0')) : c == '.' ? '٫' : c).ToArray());

    [GeneratedRegex(@"\Afinder(?::(?<slug>[a-z0-9-]{0,100}))?(?::(?<band>\d))?(?::(?<prio>[a-z]{2,8}))?\z")]
    private static partial Regex FlowRegex();
}

/// <summary>نوع صفحه‌ای که مشتری در آن است (از مسیر صفحه در مرورگر).</summary>
public enum AssistantPageKind
{
    Other = 0,
    Home = 1,
    Product = 2,
    Category = 3,
    Cart = 4,
    Checkout = 5,
    Search = 6,
    Blog = 7,
    Profile = 8,
    Login = 9,
}

/// <summary>پرسش‌های پیشنهادی هر صفحه (خالص).</summary>
public static class AssistantPages
{
    /// <summary>
    /// نوع صفحه از مسیر. مثال: «/» ← Home؛ «/product/x» ← Product؛ «/category/power-bank» ← Category؛ «/cart» ← Cart؛ «/blog/x» ← Blog؛ چیز ناشناخته ← Other.
    /// </summary>
    public static AssistantPageKind Kind(string? path)
    {
        if (string.IsNullOrEmpty(path) || path == "/")
            return AssistantPageKind.Home;
        var p = path.Split('?', '#')[0].TrimEnd('/').ToLowerInvariant();
        if (p.Length == 0)
            return AssistantPageKind.Home;
        if (p.StartsWith("/product/", StringComparison.Ordinal))
            return AssistantPageKind.Product;
        if (p.StartsWith("/category/", StringComparison.Ordinal) || p.StartsWith("/brand/", StringComparison.Ordinal))
            return AssistantPageKind.Category;
        if (p == "/cart")
            return AssistantPageKind.Cart;
        if (p.StartsWith("/checkout", StringComparison.Ordinal) || p.StartsWith("/pay", StringComparison.Ordinal))
            return AssistantPageKind.Checkout;
        if (p.StartsWith("/search", StringComparison.Ordinal) || p.StartsWith("/products", StringComparison.Ordinal))
            return AssistantPageKind.Search;
        if (p.StartsWith("/blog", StringComparison.Ordinal))
            return AssistantPageKind.Blog;
        if (p.StartsWith("/profile", StringComparison.Ordinal))
            return AssistantPageKind.Profile;
        if (p.StartsWith("/login", StringComparison.Ordinal) || p.StartsWith("/register", StringComparison.Ordinal))
            return AssistantPageKind.Login;
        return AssistantPageKind.Other;
    }

    /// <summary>نامک دسته از مسیر «/category/{slug}». مثال: «/category/power-bank?x=1» ← «power-bank»؛ بقیه ← null.</summary>
    public static string? CategorySlug(string? path)
    {
        if (path is null)
            return null;
        var p = path.Split('?', '#')[0];
        if (!p.StartsWith("/category/", StringComparison.OrdinalIgnoreCase))
            return null;
        var slug = p["/category/".Length..].Trim('/');
        return slug.Length is > 0 and <= 100 && slug.All(c => char.IsAsciiLetterOrDigit(c) || c == '-') ? slug.ToLowerInvariant() : null;
    }

    /// <summary>
    /// دکمه‌های پیشنهادی صفحه. productName فقط برای صفحهٔ کالا؛ inStock = موجود بودن همان کالا؛ freeShippingGap = مبلغ مانده تا ارسال رایگان (سبد).
    /// <para>مثال (صفحهٔ کالا): «معرفی کوتاه»، «مشخصات کلیدی»، «کی به دستم می‌رسد؟»، «گارانتی دارد؟»، «مقایسه با مشابه»، «به سبدم اضافه کن».</para>
    /// </summary>
    public static IReadOnlyList<AssistantAction> Chips(AssistantPageKind kind, string? productName = null, bool inStock = true, decimal freeShippingGap = 0, string? categorySlug = null)
    {
        var chips = new List<AssistantAction>();
        switch (kind)
        {
            case AssistantPageKind.Product:
                chips.Add(new("معرفی کوتاه این کالا", Say: "در مورد این کالا توضیح بده"));
                chips.Add(new("مشخصات کلیدی", Say: "مشخصات این چیه؟"));
                chips.Add(new("کی به دستم می‌رسد؟", Say: "کی میرسه؟"));
                chips.Add(new("گارانتی دارد؟", Say: "گارانتی این چیه؟"));
                chips.Add(new("مقایسه با مشابه", Say: "مقایسه این با مشابه"));
                chips.Add(inStock ? new("به سبدم اضافه کن", Say: "اینو به سبد اضافه کن") : new("کالای مشابه موجود", Say: "مشابه این موجود"));
                break;
            case AssistantPageKind.Category:
                chips.Add(new("کمکم کن انتخاب کنم", Say: "کمکم کن انتخاب کنم", Flow: AssistantFinder.Build(categorySlug)));
                chips.Add(new("پرفروش‌ترین‌های این دسته", Say: "پرفروش‌ترین‌ها", Flow: AssistantFinder.Build(categorySlug, 0, "best")));
                chips.Add(new("ارزان‌ترین‌ها", Say: "ارزان‌ترین‌ها", Flow: AssistantFinder.Build(categorySlug, 0, "cheap")));
                chips.Add(new("تخفیف‌دارها", Say: "تخفیف‌دارهای این دسته", Flow: AssistantFinder.Build(categorySlug, 0, "deal")));
                break;
            case AssistantPageKind.Cart:
                chips.Add(new("تسویه حساب", Say: "تسویه حساب"));
                chips.Add(new(freeShippingGap > 0 ? "تا ارسال رایگان چقدر مانده؟" : "هزینهٔ ارسال", Say: "هزینه ارسال چقدره"));
                chips.Add(new("کد تخفیف دارید؟", Say: "کد تخفیف دارید؟"));
                chips.Add(new("پیشنهاد کالای مکمل", Say: "کالاهای پرفروش"));
                break;
            case AssistantPageKind.Checkout:
                chips.Add(new("روش‌های پرداخت", Say: "روش های پرداخت"));
                chips.Add(new("زمان تحویل", Say: "کی میرسه؟"));
                chips.Add(new("کد تخفیف دارید؟", Say: "کد تخفیف دارید؟"));
                chips.Add(new("شرایط مرجوعی", Say: "شرایط مرجوعی چیه"));
                break;
            case AssistantPageKind.Login:
                chips.Add(new("ورود با کد پیامکی", Say: "ورود با کد پیامکی"));
                chips.Add(new("رمزم را فراموش کرده‌ام", Say: "رمزمو فراموش کردم"));
                break;
            case AssistantPageKind.Profile:
                chips.Add(new("سفارش‌هایم کجاست؟", Say: "سفارشم کجاست"));
                chips.Add(new("امتیاز باشگاه من", Say: "امتیاز باشگاه من"));
                chips.Add(new("آدرس‌های من", Say: "آدرس های من"));
                break;
            default:
                chips.Add(new("کمکم کن انتخاب کنم", Say: "کمکم کن انتخاب کنم", Flow: AssistantFinder.Build(null)));
                chips.Add(new("تخفیف‌های امروز", Say: "تخفیف دارید؟"));
                chips.Add(new("کالاهای پرفروش", Say: "کالاهای پرفروش"));
                chips.Add(new("پیگیری سفارش", Say: "سفارشم کجاست"));
                break;
        }
        return chips;
    }
}
