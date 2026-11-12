namespace Fratak_Shop.Core.Services.Assistant;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۴ – «راهنمای زنده»: دستیار به‌جای فقط لینک دادن، مشتری را قدم‌به‌قدم می‌برد.
//  هر پاسخ می‌تواند فهرستی از «قدم» داشته باشد که مرورگر (assets/js/assistant.js) به ترتیب اجرا می‌کند:
//    go    : رفتن به یک صفحهٔ داخلی (قدم‌های باقی‌مانده در sessionStorage می‌ماند و در صفحهٔ بعد ادامه پیدا می‌کند).
//    point : برجسته کردن یک بخش صفحه با یک توضیح کوتاه (مثلاً دکمهٔ «ثبت سفارش و پرداخت»).
//    fill  : پر کردن یک کادر (فقط شمارهٔ موبایلی که خود مشتری در چت نوشته؛ هرگز رمز).
//    focus : بردن مکان‌نما به یک کادر (مثلاً کادر رمز تا مشتری خودش رمز را بزند).
//  هدف‌ها فقط از فهرست ثابت <see cref="AssistantGuide.Targets"/> هستند و در قالب‌ها با data-assistant="…" علامت خورده‌اند؛
//  پس سرور هیچ‌وقت انتخابگر CSS یا HTML دلخواه به مرورگر نمی‌فرستد. «پرداخت» را همیشه خود مشتری می‌زند.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// یک قدم راهنمای زنده.
/// <para>مثال: new("go", Url: "/cart")؛ new("point", Target: "pay-button", Text: "برای پرداخت این دکمه را بزنید").</para>
/// </summary>
/// <param name="Kind">go، point، fill یا focus.</param>
/// <param name="Url">برای go: مسیر داخلی (/…).</param>
/// <param name="Target">برای point/fill/focus: یکی از <see cref="AssistantGuide.Targets"/>.</param>
/// <param name="Text">توضیح کوتاه کنار بخش برجسته (حداکثر ۱۲۰ نویسه).</param>
/// <param name="Value">برای fill: مقدار (فقط رقم، حداکثر ۱۱ نویسه).</param>
public sealed record AssistantStep(string Kind, string? Url = null, string? Target = null, string? Text = null, string? Value = null);

/// <summary>ساخت و اعتبارسنجی قدم‌های راهنمای زنده (خالص؛ تست: Unit/AssistantGuideTests).</summary>
public static class AssistantGuide
{
    /// <summary>بیشترین تعداد قدم در یک پاسخ.</summary>
    public const int MaxSteps = 6;

    /// <summary>بخش‌هایی از صفحه که دستیار اجازهٔ اشاره به آن‌ها را دارد (data-assistant در قالب‌ها).</summary>
    public static readonly IReadOnlySet<string> Targets = new HashSet<string>(StringComparer.Ordinal)
    {
        "add-to-cart",      // Pages/Product.cshtml – دکمهٔ افزودن به سبد
        "cart-summary",     // Pages/Cart.cshtml – جمع سبد
        "checkout-button",  // Pages/Cart.cshtml – ادامهٔ خرید
        "address",          // Pages/Checkout.cshtml – بخش آدرس
        "order-summary",    // Pages/Checkout.cshtml – خلاصهٔ سفارش
        "pay-button",       // Pages/Checkout.cshtml – ثبت سفارش و پرداخت
        "login-phone",      // Pages/Login.cshtml – موبایل (ورود با رمز)
        "login-password",   // Pages/Login.cshtml – رمز
        "otp-phone",        // Pages/Login.cshtml – موبایل (ورود با کد پیامکی)
        "order-cancel",     // Pages/Profile/OrderDetail.cshtml – لغو سفارش (دور ۲۵)
        "order-return",     // Pages/Profile/OrderDetail.cshtml – درخواست مرجوعی (دور ۲۵)
        "order-invoice",    // Pages/Profile/OrderDetail.cshtml – فاکتور و PDF (دور ۲۵)
    };

    private static readonly HashSet<string> Kinds = new(StringComparer.Ordinal) { "go", "point", "fill", "focus" };

    /// <summary>رفتن به صفحه.</summary>
    public static AssistantStep Go(string url) => new("go", Url: url);

    /// <summary>برجسته کردن یک بخش با توضیح.</summary>
    public static AssistantStep Point(string target, string text) => new("point", Target: target, Text: text);

    /// <summary>بردن مکان‌نما به یک کادر با توضیح.</summary>
    public static AssistantStep Focus(string target, string text) => new("focus", Target: target, Text: text);

    /// <summary>پر کردن کادر (فقط موبایل).</summary>
    public static AssistantStep Fill(string target, string value) => new("fill", Target: target, Value: value);

    /// <summary>
    /// قدم معتبر است؟ نوع شناخته‌شده؛ go فقط مسیر داخلی (نه //، نه \)؛ هدف فقط از فهرست ثابت؛ fill فقط رقم و هرگز روی کادر رمز.
    /// <para>مثال: Go("/cart") ← true؛ Go("https://evil") ← false؛ Fill("login-password", "123") ← false؛ Point("body", "x") ← false.</para>
    /// </summary>
    public static bool IsValid(AssistantStep step)
    {
        if (step is null || !Kinds.Contains(step.Kind))
            return false;
        if (step.Text is { Length: > 120 })
            return false;
        return step.Kind switch
        {
            "go" => IsLocalUrl(step.Url),
            "fill" => step.Target is "login-phone" or "otp-phone" && step.Value is { Length: >= 4 and <= 11 } value && value.All(char.IsAsciiDigit),
            _ => step.Target is not null && Targets.Contains(step.Target),
        };
    }

    /// <summary>فقط قدم‌های معتبر و حداکثر <see cref="MaxSteps"/> تا.</summary>
    public static IReadOnlyList<AssistantStep> Clean(IEnumerable<AssistantStep>? steps) =>
        (steps ?? []).Where(IsValid).Take(MaxSteps).ToList();

    /// <summary>مسیر داخلی امن. مثال: «/product/x» ← true؛ «//evil.com»، «/\evil»، «javascript:…»، بیش از ۳۰۰ نویسه ← false.</summary>
    public static bool IsLocalUrl(string? url) =>
        url is { Length: > 0 and <= 300 } && url[0] == '/' && !url.StartsWith("//", StringComparison.Ordinal) && !url.StartsWith("/\\", StringComparison.Ordinal)
        && !url.Any(char.IsControl);

    /// <summary>
    /// قدم‌های «افزودن به سبد»: (اگر در صفحهٔ کالا نیستیم) رفتن به صفحهٔ کالا ← اشاره به دکمهٔ افزودن ← رفتن به سبد ← اشاره به جمع سبد و ادامهٔ خرید.
    /// افزودن واقعی پیش‌تر روی سرور انجام شده؛ این قدم‌ها فقط نشان می‌دهند کجا چه اتفاقی افتاد.
    /// </summary>
    public static IReadOnlyList<AssistantStep> AddedToCart(string productUrl, bool onProductPage)
    {
        var steps = new List<AssistantStep>();
        if (!onProductPage)
            steps.Add(Go(productUrl));
        steps.Add(Point("add-to-cart", "به سبد اضافه شد ✓"));
        steps.Add(Go("/cart"));
        steps.Add(Point("cart-summary", "جمع سبد شما"));
        steps.Add(Point("checkout-button", "برای ادامه این دکمه را بزنید یا به من بگویید «تسویه حساب»"));
        return steps;
    }

    /// <summary>
    /// قدم‌های تسویه با آدرس انتخاب‌شده: رفتن به صفحهٔ پرداخت ← آدرس ← خلاصهٔ سفارش ← دکمهٔ پرداخت (که خود مشتری می‌زند).
    /// </summary>
    public static IReadOnlyList<AssistantStep> Checkout(Guid addressId) =>
    [
        Go($"/checkout?addressId={addressId}"),
        Point("address", "آدرس تحویل انتخاب شد"),
        Point("order-summary", "خلاصه و مبلغ نهایی سفارش"),
        Point("pay-button", "روش ارسال را ببینید و برای پرداخت، خودتان این دکمه را بزنید"),
    ];

    /// <summary>
    /// قدم‌های ورود: رفتن به صفحهٔ ورود (با بازگشت به همین صفحه) ← (اگر موبایل را در چت نوشته) پر کردن موبایل ← مکان‌نما روی رمز.
    /// رمز هرگز در چت گرفته یا پر نمی‌شود.
    /// </summary>
    public static IReadOnlyList<AssistantStep> Login(string? returnUrl, string? phone, bool withCode)
    {
        var back = IsLocalUrl(returnUrl) && !returnUrl!.StartsWith("/login", StringComparison.OrdinalIgnoreCase) ? returnUrl : "/";
        var steps = new List<AssistantStep> { Go($"/login?mode={(withCode ? "otp" : "password")}&returnUrl=" + Uri.EscapeDataString(back)) };
        var target = withCode ? "otp-phone" : "login-phone";
        if (phone is not null)
            steps.Add(Fill(target, phone));
        steps.Add(withCode
            ? Focus("otp-phone", "شماره را بررسی و «دریافت کد» را بزنید")
            : Focus("login-password", "رمزتان را خودتان اینجا بنویسید؛ من به رمز شما دسترسی ندارم 🔒"));
        return steps;
    }
}
