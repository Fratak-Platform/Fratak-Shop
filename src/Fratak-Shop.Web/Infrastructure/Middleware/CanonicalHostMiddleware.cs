using System.Net;
using Microsoft.Extensions.Options;

namespace Fratak_Shop.Web.Infrastructure.Middleware;

/// <summary>
/// یک دامنهٔ اصلی برای کل سایت (مرحلهٔ سیزدهم، انتشار روی faratek.ir).
/// <para>وقتی <see cref="ShopOptions.CanonicalHost"/> تنظیم باشد (مثلاً <c>faratek.ir</c>)، درخواستی که با دامنهٔ دیگری آمده
/// (مثل <c>www.faratek.ir</c> یا دامنهٔ موقت هاست) با ۳۰۱ به همان مسیر روی دامنهٔ اصلی با https می‌رود.
/// نتیجه: گوگل فقط یک نسخه از هر صفحه می‌بیند، کوکی ورود روی یک دامنه می‌ماند و نقشهٔ سایت/robots/لینک کانونیکال
/// (که از دامنهٔ درخواست ساخته می‌شوند) همیشه با دامنهٔ اصلی‌اند. هدر Host جعلی هم دیگر وارد لینک‌های ساخته‌شده نمی‌شود.</para>
/// <para>استثناها: localhost و آدرس IP (آزمایش روی خود سرور و آزمون‌ها)، <c>/health</c> و <c>/healthz</c> (سرویس پایش)
/// و <c>/.well-known/</c> (تأیید گواهی SSL). درخواست غیر GET با ۳۰۸ می‌رود تا متد و بدنه حفظ شود.</para>
/// <para>محل ثبت: Program.cs، بعد از هدرهای امنیتی و دیوار آتش و پیش از فایل‌های ثابت.</para>
/// </summary>
public sealed class CanonicalHostMiddleware(RequestDelegate next, IOptions<ShopOptions> options)
{
    private readonly string? _host = Normalize(options.Value.CanonicalHost);

    public Task InvokeAsync(HttpContext context)
    {
        if (_host is null || !ShouldRedirect(_host, context.Request.Host.Host, context.Request.Path))
            return next(context);

        var request = context.Request;
        var target = $"https://{_host}{request.PathBase}{request.Path}{request.QueryString}";
        context.Response.StatusCode = HttpMethods.IsGet(request.Method) || HttpMethods.IsHead(request.Method)
            ? StatusCodes.Status301MovedPermanently
            : StatusCodes.Status308PermanentRedirect;
        context.Response.Headers.Location = target;
        return Task.CompletedTask;
    }

    /// <summary>
    /// آیا این درخواست باید به دامنهٔ اصلی برود؟ (خالص و تست‌پذیر)
    /// <para>مثال: («faratek.ir»، «www.faratek.ir»، «/») ← true؛ («faratek.ir»، «FARATEK.IR»، «/») ← false؛
    /// («faratek.ir»، «localhost»، «/») ← false؛ («faratek.ir»، «185.1.2.3»، «/») ← false؛ («faratek.ir»، «www.faratek.ir»، «/health») ← false.</para>
    /// </summary>
    public static bool ShouldRedirect(string canonicalHost, string? requestHost, PathString path)
    {
        if (string.IsNullOrEmpty(requestHost) || string.Equals(requestHost, canonicalHost, StringComparison.OrdinalIgnoreCase))
            return false;
        if (string.Equals(requestHost, "localhost", StringComparison.OrdinalIgnoreCase)
            || requestHost.EndsWith(".localhost", StringComparison.OrdinalIgnoreCase)
            || IPAddress.TryParse(requestHost.Trim('[', ']'), out _))
            return false;
        return !(path.StartsWithSegments("/health") || path.StartsWithSegments("/healthz") || path.StartsWithSegments("/.well-known"));
    }

    /// <summary>
    /// مقدار تنظیمات به نام دامنهٔ خالص. مثال: «https://www.Faratek.ir/» ← «www.faratek.ir»؛ «faratek.ir» ← «faratek.ir»؛ «» ← null.
    /// <para>دامنهٔ عمومی همیشه نقطه دارد؛ پس «off»، «none» یا «localhost» یعنی خاموش (null). با این، خاموش کردن با متغیر محیطی
    /// <c>Shop__CanonicalHost=off</c> هم ممکن است (مقدار خالی متغیر محیطی در ویندوز یعنی «متغیر وجود ندارد»).</para>
    /// </summary>
    public static string? Normalize(string? value)
    {
        var text = value?.Trim();
        if (string.IsNullOrEmpty(text))
            return null;
        if (!text.Contains("://", StringComparison.Ordinal))
            text = "https://" + text;
        return Uri.TryCreate(text, UriKind.Absolute, out var uri) && !string.IsNullOrEmpty(uri.Host) && uri.HostNameType == UriHostNameType.Dns && uri.IdnHost.Contains('.')
            ? uri.IdnHost.ToLowerInvariant()
            : null;
    }
}
