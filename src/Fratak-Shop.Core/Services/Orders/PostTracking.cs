using System.Globalization;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Orders;

namespace Fratak_Shop.Core.Services.Orders;

// ═══════════════════════════════════════════════════════════════════════════
//  قواعد و تجزیه‌گر رهگیری پست (مرحلهٔ هفتم – فاز ۶)
//   • ShippingCarriers      : نام شرکت حمل، اعتبارسنجی کد رهگیری و لینک رهگیری عمومی.
//   • PostStatusClassifier  : از متن یک رویداد پست می‌فهمد «تحویل شد / برگشت خورد / در حال توزیع / ...».
//   • PostTrackingParser    : HTML سامانهٔ tracking.post.ir را به فهرست رویدادها تبدیل می‌کند (با AngleSharp).
//   • IPostTrackingClient   : قرارداد خواندن از سامانهٔ پست؛ پیاده‌سازی HTTP در لایهٔ وب
//                             (Web/Infrastructure/Shipping/PostTrackingInfrastructure.cs) است تا در تست جعلی شود.
//  همهٔ این‌ها رایگان‌اند: سامانهٔ عمومی رهگیری پست بدون ثبت‌نام و API پولی خوانده می‌شود.
//  محدودیت مهم: tracking.post.ir فقط از IP ایران پاسخ می‌دهد.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// اطلاعات شرکت‌های حمل.
/// <para>استفاده: فرم ثبت کد رهگیری در جزئیات سفارش پنل، کارت رهگیری مشتری و سرویس رهگیری.</para>
/// </summary>
public static class ShippingCarriers
{
    /// <summary>ترتیب نمایش در فهرست انتخاب.</summary>
    public static readonly ShippingCarrier[] All =
        [ShippingCarrier.IranPost, ShippingCarrier.Tipax, ShippingCarrier.Chapar, ShippingCarrier.Courier, ShippingCarrier.Other];

    /// <summary>نام فارسی شرکت حمل. مثال: IranPost → «پست».</summary>
    public static string Title(ShippingCarrier carrier) => carrier switch
    {
        ShippingCarrier.IranPost => "پست",
        ShippingCarrier.Tipax => "تیپاکس",
        ShippingCarrier.Chapar => "چاپار",
        ShippingCarrier.Courier => "پیک",
        _ => "سایر",
    };

    /// <summary>آیا سیستم وضعیت این شرکت را خودکار می‌خواند؟ (فعلاً فقط پست).</summary>
    public static bool SupportsAutoTracking(ShippingCarrier carrier) => carrier == ShippingCarrier.IranPost;

    /// <summary>
    /// لینک صفحهٔ رهگیری عمومی برای دکمهٔ «پیگیری در سایت پست». فقط برای پست که آدرسش قطعی است؛ بقیه null.
    /// مثال: https://tracking.post.ir/search.aspx?id=123456789012345678901234
    /// </summary>
    public static string? PublicTrackingUrl(ShippingCarrier carrier, string code) =>
        carrier == ShippingCarrier.IranPost && !string.IsNullOrWhiteSpace(code)
            ? "https://tracking.post.ir/search.aspx?id=" + Uri.EscapeDataString(code)
            : null;

    /// <summary>
    /// تمیز کردن و بررسی کد رهگیری.
    /// <para>پست: ارقام فارسی/عربی به انگلیسی، فاصله و خط تیره حذف؛ باید ۱۰ تا ۳۰ رقم باشد (کد امروزی پست ۲۴ رقمی است).</para>
    /// <para>بقیه: ۳ تا ۱۰۰ نویسه بدون فاصلهٔ اضافه.</para>
    /// مثال: «۱۲۳۴ ۵۶۷۸-۹۰۱۲...» → «12345678 9012...» بدون فاصله.
    /// </summary>
    public static bool TryNormalizeCode(ShippingCarrier carrier, string? raw, out string code, out string? error)
    {
        code = "";
        error = null;
        var text = SearchText.NormalizeDigits(raw);
        if (text.Length == 0)
        {
            error = "کد رهگیری مرسوله را وارد کنید.";
            return false;
        }

        if (carrier == ShippingCarrier.IranPost)
        {
            var compact = new string(text.Where(c => !char.IsWhiteSpace(c) && c != '-' && c != '_').ToArray());
            if (!compact.All(char.IsAsciiDigit) || compact.Length is < 10 or > 30)
            {
                error = "کد رهگیری پست باید فقط عدد و بین ۱۰ تا ۳۰ رقم باشد (کد امروزی پست ۲۴ رقمی است).";
                return false;
            }
            code = compact;
            return true;
        }

        text = string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
        if (text.Length is < 3 or > 100)
        {
            error = "کد رهگیری باید بین ۳ تا ۱۰۰ نویسه باشد.";
            return false;
        }
        code = text;
        return true;
    }

    /// <summary>عنوان فارسی وضعیت رهگیری.</summary>
    public static string StateTitle(ShipmentTrackingState state) => state switch
    {
        ShipmentTrackingState.Pending => "در انتظار اولین بررسی",
        ShipmentTrackingState.InTransit => "در مسیر",
        ShipmentTrackingState.OutForDelivery => "در حال توزیع",
        ShipmentTrackingState.Delivered => "تحویل به گیرنده",
        ShipmentTrackingState.Returned => "برگشت به فرستنده",
        ShipmentTrackingState.NotFound => "هنوز در سامانهٔ پست ثبت نشده",
        _ => "بررسی متوقف",
    };

    /// <summary>آیا هنوز باید بررسی شود؟</summary>
    public static bool IsActive(ShipmentTrackingState state) =>
        state is ShipmentTrackingState.Pending or ShipmentTrackingState.InTransit
            or ShipmentTrackingState.OutForDelivery or ShipmentTrackingState.NotFound;
}

/// <summary>نوع یک رویداد پستی که از متن آن فهمیده می‌شود.</summary>
public enum PostEventKind
{
    Unknown = 0,

    /// <summary>قبول/پذیرش مرسوله در باجه.</summary>
    Accepted = 1,

    /// <summary>ورود/خروج از مراکز مبادلات و جابه‌جایی بین شهرها.</summary>
    InTransit = 2,

    /// <summary>تحویل به نامه‌رسان/موزع برای توزیع.</summary>
    OutForDelivery = 3,

    /// <summary>تلاش ناموفق (عدم حضور گیرنده، نقص آدرس، ...).</summary>
    DeliveryFailed = 4,

    /// <summary>تحویل به گیرنده.</summary>
    Delivered = 5,

    /// <summary>برگشت/اعاده به فرستنده.</summary>
    Returned = 6,
}

/// <summary>
/// تشخیص نوع رویداد از متن فارسی سامانهٔ پست.
/// <para>
/// اصل مهم: «تحویل‌شده» فقط وقتی تشخیص داده می‌شود که متن هم «تحویل» و هم «گیرنده» داشته باشد و منفی نباشد
/// (مثلاً «تحویل مرسوله به گیرنده»). «تحویل به نامه‌رسان» یا «تحویل به مرکز» تحویل نیست. اشتباه در این جهت
/// (تحویل نشده را تحویل‌شده بدانیم) گران است؛ برعکسش فقط چند ساعت تأخیر دارد و کارمند می‌تواند دستی ثبت کند.
/// </para>
/// مثال‌ها: «قبول مرسوله» → Accepted، «ورود به مرکز مبادلات» → InTransit، «تحویل به نامه‌رسان» → OutForDelivery،
/// «عدم حضور گیرنده» → DeliveryFailed، «تحویل مرسوله به گیرنده» → Delivered، «برگشت به فرستنده» → Returned.
/// </summary>
public static class PostStatusClassifier
{
    public static PostEventKind Classify(string? status)
    {
        var text = Normalize(status);
        if (text.Length == 0)
            return PostEventKind.Unknown;

        if (ContainsAny(text, "عدم تحویل", "عدم حضور", "عدم مراجعه", "عدم شناسایی", "نقص آدرس", "آدرس ناقص", "نشانی ناقص", "ناموفق", "عدم امکان"))
            return PostEventKind.DeliveryFailed;
        if (ContainsAny(text, "برگشت", "اعاده", "مرجوع", "عودت"))
            return PostEventKind.Returned;
        if (text.Contains("تحویل") && text.Contains("گیرنده"))
            return PostEventKind.Delivered;
        if (ContainsAny(text, "نامه رسان", "موزع", "توزیع", "پستچی"))
            return PostEventKind.OutForDelivery;
        if (ContainsAny(text, "قبول", "پذیرش", "ثبت مرسوله", "دریافت از فرستنده"))
            return PostEventKind.Accepted;
        return PostEventKind.InTransit;
    }

    /// <summary>
    /// وضعیت کلی مرسوله از روی همهٔ رویدادها (به ترتیب قدیمی ← جدید).
    /// برگشت در هر جای تاریخچه → «برگشت» (چون «تحویل به گیرنده» بعد از برگشت یعنی تحویل به خود فرستنده).
    /// </summary>
    public static ShipmentTrackingState Overall(IReadOnlyList<PostEventKind> kinds)
    {
        if (kinds.Count == 0)
            return ShipmentTrackingState.NotFound;
        if (kinds.Contains(PostEventKind.Returned))
            return ShipmentTrackingState.Returned;
        if (kinds.Contains(PostEventKind.Delivered))
            return ShipmentTrackingState.Delivered;
        return kinds[^1] == PostEventKind.OutForDelivery ? ShipmentTrackingState.OutForDelivery : ShipmentTrackingState.InTransit;
    }

    /// <summary>ي/ك عربی → فارسی، نیم‌فاصله → فاصله، فاصله‌های تکراری حذف.</summary>
    internal static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        var chars = value.Select(ch => ch switch
        {
            'ي' or 'ى' => 'ی',
            'ك' => 'ک',
            '\u200C' or '\u200F' or '\u200E' or '\u00A0' => ' ',
            _ when char.IsControl(ch) => ' ',
            _ => ch,
        }).ToArray();
        return string.Join(' ', new string(chars).Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    private static bool ContainsAny(string text, params string[] words) => words.Any(text.Contains);
}

/// <summary>یک رویداد خوانده‌شده از سامانهٔ پست (هنوز ذخیره نشده).</summary>
/// <param name="Sequence">ترتیب، ۰ = قدیمی‌ترین.</param>
/// <param name="Status">متن رویداد. مثال: «قبول مرسوله».</param>
/// <param name="Location">محل. مثال: «باجه معالی آباد شیراز».</param>
/// <param name="RawTime">تاریخ و ساعت به همان شکل سامانه. مثال: «سه شنبه ۱۴۰۵/۰۸/۰۴ 10:45».</param>
/// <param name="OccurredAt">زمان تبدیل‌شده (اگر ممکن بود).</param>
public sealed record PostTrackingEvent(int Sequence, string Status, string? Location, string? RawTime, DateTimeOffset? OccurredAt)
{
    public PostEventKind Kind => PostStatusClassifier.Classify(Status);

    /// <summary>
    /// اثر انگشت پایدار برای تشخیص رویداد تکراری بین دو بررسی (ترتیب در آن نیست چون با رویداد تازه عوض می‌شود).
    /// </summary>
    public string Fingerprint
    {
        get
        {
            var key = $"{PostStatusClassifier.Normalize(RawTime)}|{PostStatusClassifier.Normalize(Status)}|{PostStatusClassifier.Normalize(Location)}";
            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(key)))[..32];
        }
    }
}

/// <summary>نتیجهٔ کلی یک درخواست رهگیری.</summary>
public enum PostTrackingOutcome
{
    /// <summary>مرسوله پیدا شد (ممکن است هنوز رویدادی نداشته باشد).</summary>
    Found = 0,

    /// <summary>سامانه این کد را نمی‌شناسد.</summary>
    NotFound = 1,

    /// <summary>خطای شبکه/سامانه (دوباره امتحان می‌شود).</summary>
    Failed = 2,
}

/// <summary>پاسخ یک بار رهگیری.</summary>
public sealed record PostTrackingResponse(
    PostTrackingOutcome Outcome,
    IReadOnlyList<PostTrackingEvent> Events,
    IReadOnlyList<KeyValuePair<string, string>> Info,
    string? Error = null)
{
    public static PostTrackingResponse Fail(string error) => new(PostTrackingOutcome.Failed, [], [], error);
}

/// <summary>
/// خواندن وضعیت یک کد از سامانهٔ رهگیری پست.
/// <para>پیاده‌سازی واقعی: Web/Infrastructure/Shipping/PostTrackingClient (دو درخواست به tracking.post.ir).</para>
/// <para>در تست‌ها یک نسخهٔ جعلی جایش گذاشته می‌شود.</para>
/// </summary>
public interface IPostTrackingClient
{
    Task<PostTrackingResponse> TrackAsync(string code, CancellationToken cancellationToken);
}

/// <summary>
/// تبدیل HTML سامانهٔ tracking.post.ir به داده.
/// <para>
/// ساختار صفحه (از کتابخانهٔ متن‌باز post-tracker گرفته شده): نتیجه داخل <c>#pnlResult</c> است؛ هر <c>div.row</c>
/// یا سرتیتر تاریخ (<c>.newtdheader</c>) است که برای ردیف‌های بعدی معتبر است، یا ردیف رویداد با خانه‌های
/// <c>.newtddata</c> = [ردیف، شرح، محل، ساعت]. اطلاعات کلی مرسوله در <c>#pParcelInfo</c> با جفت‌های
/// <c>.newcolheader</c>/<c>.newcoldata</c> است. صفحه جدیدترین رویداد را اول می‌آورد؛ ما برعکس می‌کنیم (قدیمی ← جدید).
/// </para>
/// اگر پست ساختار صفحه را عوض کند، فقط همین کلاس باید اصلاح شود (و تست‌های واحدش در tests/…/Unit/PostTrackingTests.cs).
/// </summary>
public static class PostTrackingParser
{
    private static readonly Regex JalaliDate = new(@"(1[34]\d{2})\s*[/\-.]\s*(\d{1,2})\s*[/\-.]\s*(\d{1,2})", RegexOptions.Compiled);
    private static readonly Regex ClockTime = new(@"(\d{1,2})\s*:\s*(\d{2})", RegexOptions.Compiled);

    /// <summary>
    /// خواندن فیلدهای مخفی فرم ASP.NET صفحهٔ جست‌وجو (مرحلهٔ اول). اگر نبودند null (یعنی صفحه عوض شده یا خطا داده).
    /// </summary>
    public static (string ViewState, string EventValidation, string? Generator)? ReadFormState(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return null;
        var document = new HtmlParser().ParseDocument(html);
        var viewState = document.GetElementById("__VIEWSTATE")?.GetAttribute("value");
        var validation = document.GetElementById("__EVENTVALIDATION")?.GetAttribute("value");
        if (string.IsNullOrEmpty(viewState) || string.IsNullOrEmpty(validation))
            return null;
        var generator = document.GetElementById("__VIEWSTATEGENERATOR")?.GetAttribute("value");
        return (viewState, validation, string.IsNullOrEmpty(generator) ? null : generator);
    }

    /// <summary>
    /// تبدیل پاسخ جست‌وجو (مرحلهٔ دوم) به رویدادها. پاسخ ممکن است HTML کامل یا قالب «Delta» ای‌جکس ASP.NET باشد؛
    /// تجزیه‌گر HTML هر دو را می‌خواند چون فقط دنبال عنصرهای با شناسه/کلاس مشخص است.
    /// </summary>
    public static PostTrackingResponse ParseResult(string? html)
    {
        if (string.IsNullOrWhiteSpace(html))
            return PostTrackingResponse.Fail("پاسخ سامانهٔ پست خالی بود.");

        var document = new HtmlParser().ParseDocument(html);
        var panel = document.GetElementById("pnlResult");
        if (panel is null)
            return new PostTrackingResponse(PostTrackingOutcome.NotFound, [], []);

        var events = new List<(string Status, string? Location, string? Raw, DateTimeOffset? At)>();
        string? currentDate = null;
        foreach (var row in panel.QuerySelectorAll("div.row"))
        {
            var header = row.ClassList.Contains("newtdheader") ? row : row.QuerySelector(".newtdheader");
            if (header is not null)
            {
                currentDate = Clean(header.TextContent);
                continue;
            }

            // لینک‌های داخل ردیف (مثلاً «نظرسنجی») جزو متن رویداد نیستند.
            foreach (var link in row.QuerySelectorAll("a").ToList())
                link.Remove();
            var cells = row.QuerySelectorAll(".newtddata").Select(c => Clean(c.TextContent)).ToList();
            if (cells.Count < 2 || string.IsNullOrEmpty(cells[1]))
                continue;

            var status = Cut(cells[1], 300)!;
            var location = Cut(cells.ElementAtOrDefault(2), 200);
            var time = cells.ElementAtOrDefault(3);
            var raw = string.Join(' ', new[] { currentDate, time }.Where(s => !string.IsNullOrEmpty(s)));
            events.Add((status, string.IsNullOrEmpty(location) ? null : location, raw.Length == 0 ? null : Cut(raw, 100), ParseMoment(currentDate, time)));
        }

        events.Reverse();
        var list = events.Select((e, i) => new PostTrackingEvent(i, e.Status, e.Location, e.Raw, e.At)).ToList();

        var info = new List<KeyValuePair<string, string>>();
        var parcel = document.GetElementById("pParcelInfo");
        if (parcel is not null)
        {
            foreach (var col in parcel.QuerySelectorAll("div.newrowdatacol"))
            {
                var key = Clean(col.QuerySelector(".newcolheader")?.TextContent);
                var value = Clean(col.QuerySelector(".newcoldata")?.TextContent);
                if (!string.IsNullOrEmpty(key) && !string.IsNullOrEmpty(value) && info.Count < 30)
                    info.Add(new(Cut(key, 60)!, Cut(value, 200)!));
            }
        }

        return new PostTrackingResponse(PostTrackingOutcome.Found, list, info);
    }

    /// <summary>
    /// تبدیل تاریخ شمسی و ساعت به زمان مطلق (وقت ایران). اگر تاریخ قابل خواندن نبود null.
    /// مثال: («سه شنبه ۱۴۰۵/۰۸/۰۴»، «۱۰:۴۵») → 2026-10-26 10:45 +03:30.
    /// </summary>
    public static DateTimeOffset? ParseMoment(string? dateText, string? timeText)
    {
        var date = JalaliDate.Match(SearchText.NormalizeDigits(dateText));
        if (!date.Success)
            return null;
        try
        {
            var calendar = new PersianCalendar();
            var gregorian = calendar.ToDateTime(
                int.Parse(date.Groups[1].Value, CultureInfo.InvariantCulture),
                int.Parse(date.Groups[2].Value, CultureInfo.InvariantCulture),
                int.Parse(date.Groups[3].Value, CultureInfo.InvariantCulture), 0, 0, 0, 0);
            var time = TimeOnly.MinValue;
            var clock = ClockTime.Match(SearchText.NormalizeDigits(timeText));
            if (clock.Success)
            {
                var hour = int.Parse(clock.Groups[1].Value, CultureInfo.InvariantCulture);
                var minute = int.Parse(clock.Groups[2].Value, CultureInfo.InvariantCulture);
                if (hour is >= 0 and < 24 && minute is >= 0 and < 60)
                    time = new TimeOnly(hour, minute);
            }
            return IranClock.At(DateOnly.FromDateTime(gregorian), time);
        }
        catch (ArgumentOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>متن تمیز: فاصله‌ها و خط‌های اضافه حذف و ارقام فارسی حفظ می‌شوند.</summary>
    private static string Clean(string? text) =>
        string.IsNullOrWhiteSpace(text) ? "" : string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    private static string? Cut(string? text, int max) =>
        text is null ? null : text.Length <= max ? text : text[..max];
}
