namespace Fratak_Shop.Core.Services.Orders;

/// <summary>
/// خواندن «کد امنیتی» (کپچای عددی) صفحهٔ رهگیری شرکت پست (tracking.post.ir).
/// <para>
/// چرا لازم است؟ سایت رهگیری پست از اواخر ۱۴۰۴ قبل از نشان دادن نتیجه یک تصویر ۴ رقمی می‌خواهد. بدون حل آن، رهگیری خودکار
/// (و در نتیجه «تحویل شد» خودکار و خط زمانی مشتری) کار نمی‌کند. سرویس پولی یا API خارجی ممنوع است؛ پس یک مدل کوچک
/// تشخیص متن (ONNX، آفلاین، داخل خود برنامه) عدد را می‌خواند.
/// </para>
/// پیاده‌سازی: <c>Fratak_Shop.Web.Infrastructure.Shipping.OnnxCaptchaSolver</c> (لایهٔ Web چون OnnxRuntime و SkiaSharp آنجاست).
/// استفاده: <c>PostTrackingClient</c> قبل از ارسال فرم جست‌وجو.
/// </summary>
public interface IPostCaptchaSolver
{
    /// <summary>مدل پیدا و بارگذاری شده است؟ (اگر فایل مدل نباشد false و رهگیری با پیام روشن «ناموفق» می‌شود.)</summary>
    bool IsAvailable { get; }

    /// <summary>
    /// خواندن عدد داخل تصویر (PNG/JPEG/GIF). خروجی فقط رقم‌های لاتین است (مثلاً «4058»)؛ اگر تصویر قابل خواندن نبود null.
    /// خطا پرتاب نمی‌کند.
    /// </summary>
    string? Solve(byte[] image);
}

/// <summary>
/// رمزگشایی خروجی مدل به روش CTC (Connectionist Temporal Classification) – منطق خالص و قابل تست، بدون وابستگی به OnnxRuntime.
/// <para>
/// مدل برای هر «ستون زمانی» تصویر (از چپ به راست) ۱۱ امتیاز می‌دهد: کلاس ۰ = «خالی» (blank) و کلاس i = رقم i−1.
/// قاعدهٔ CTC: در هر ستون کلاس بیشترین امتیاز را برمی‌داریم، تکرارهای پشت‌سرهم را یکی می‌کنیم و خالی‌ها را حذف می‌کنیم.
/// </para>
/// مثال: کلاس‌های ستون‌ها <c>0 5 5 0 1 1 0 4 0 9 9</c> → «5 5» یکی می‌شود → ارقام «4 0 3 8» (چون کلاس i یعنی رقم i−1).
/// برای «00» مدل بین دو صفر یک blank می‌گذارد: <c>1 0 1</c> → «00».
/// </summary>
public static class CtcDigitDecoder
{
    /// <summary>تعداد کلاس‌های مدل: blank + ده رقم.</summary>
    public const int Classes = 11;

    /// <summary>
    /// رمزگشایی از آرایهٔ تخت امتیازها به طول steps×classes (ترتیب: ستون اول همهٔ کلاس‌ها، بعد ستون دوم و ...).
    /// </summary>
    /// <param name="logits">امتیازهای خام مدل (لازم نیست softmax شده باشد؛ فقط بیشینه مهم است).</param>
    /// <param name="steps">تعداد ستون‌های زمانی (T).</param>
    /// <param name="classes">تعداد کلاس‌ها (باید ۱۱ باشد).</param>
    /// <returns>رشتهٔ ارقام؛ اگر ورودی نامعتبر بود رشتهٔ خالی.</returns>
    public static string Decode(ReadOnlySpan<float> logits, int steps, int classes = Classes)
    {
        if (steps <= 0 || classes != Classes || logits.Length < steps * classes)
            return "";
        var result = new System.Text.StringBuilder(8);
        var previous = -1;
        for (var t = 0; t < steps; t++)
        {
            var row = logits.Slice(t * classes, classes);
            var best = 0;
            for (var c = 1; c < classes; c++)
            {
                if (row[c] > row[best])
                    best = c;
            }
            if (best != previous && best != 0)
                result.Append((char)('0' + best - 1));
            previous = best;
        }
        return result.ToString();
    }

    /// <summary>
    /// آیا جواب شبیه کد امنیتی پست است؟ (فقط رقم، ۴ تا ۶ کاراکتر). جواب مشکوک ارسال نمی‌شود تا تلاش هدر نرود.
    /// مثال: «4058» → true، «40» → false.
    /// </summary>
    public static bool LooksValid(string? code) =>
        code is { Length: >= 4 and <= 6 } && code.All(char.IsAsciiDigit);
}
