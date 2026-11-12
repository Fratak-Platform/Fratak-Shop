using System.Globalization;
using System.Reflection;
using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Branding;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Settings;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Admin;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Settings;

/// <summary>
/// «استودیوی لوگو» (مرحلهٔ دوازدهم) – آدرس <c>/admin/settings/brand</c>.
/// <para>
/// یک صفحه برای همهٔ جاهایی که لوگو دیده می‌شود: هدر، منوی موبایل، فوتر، ورود/ثبت‌نام، پنل مدیریت، فاکتور، ایمیل و صفحهٔ تعمیرات،
/// به‌علاوهٔ آیکون‌های اپ (PWA)، آیفون و فاوآیکن. برای هر محل: نمایش آیکون/متن/شعار، آیکون اختصاصی، اندازهٔ آیکون (دسکتاپ و موبایل)،
/// اندازه و وزن قلم، فاصلهٔ حروف، فاصلهٔ آیکون تا متن، فاصلهٔ دو بخش متن، فاصلهٔ شعار، جابه‌جایی عمودی، چینش، تراز، متن و رنگ.
/// </para>
/// <para>
/// پیش‌نمایش زنده: assets/js/admin-brand.js با هر تغییر فرم همان متغیرهای CSS لوگوی واقعی (--bl-*) را عوض می‌کند، پس آنچه
/// می‌بینید دقیقاً همان است که پس از ذخیره در سایت نمایش داده می‌شود (همان HTML و همان brand-lockup.css).
/// </para>
/// ذخیره: متن‌ها/آیکون‌های پایه در ستون‌های StoreSetting (مثل قبل) و بقیه به صورت JSON در StoreSetting.BrandStyleJson؛
/// همه از مسیر StoreSettingService.UpdateAsync و اعتبارسنجی BrandStyleRules.Validate می‌گذرند.
/// </summary>
[AdminPermission(AdminPermissions.SettingsManage)]
public class BrandModel(IAdminDataService data, IStoreSettingService settings, IMonitoringService monitoring) : AdminPageModel
{
    private const string PagePath = "/admin/settings/brand";

    private static readonly Dictionary<string, PropertyInfo> CommandProperties =
        typeof(StoreSettingCommand).GetProperties(BindingFlags.Public | BindingFlags.Instance).Where(p => p.CanWrite).ToDictionary(p => p.Name);

    private static readonly Dictionary<string, PropertyInfo> EntityProperties =
        typeof(StoreSetting).GetProperties(BindingFlags.Public | BindingFlags.Instance).ToDictionary(p => p.Name);

    /// <summary>فیلدهای پایه که در ستون‌های خود StoreSetting ذخیره می‌شوند (نام فرم: g.{نام}).</summary>
    public static readonly string[] SettingFields =
        ["LogoUrl", "LogoDarkUrl", "PwaIconUrl", "FaviconUrl", "BrandTextPrimary", "BrandTextAccent", "BrandTextColor", "BrandAccentColor"];

    /// <summary>
    /// همهٔ ستون‌های تنظیمات که فقط این صفحه مدیریتشان می‌کند (در تب‌های «تنظیمات فروشگاه» نیستند).
    /// تست AdminRegistryTests.SettingsTabs_CoverEveryStoreSettingField_Once با همین فهرست بررسی می‌کند هیچ تنظیمی بی‌صفحه نماند.
    /// </summary>
    public static readonly string[] ManagedFields =
        [.. SettingFields, "BrandStyleJson", "BrandTextInHeader", "BrandTextInAuth", "BrandTextInAdmin", "BrandTextInInvoice"];

    /// <summary>محل فعال پس از بارگذاری (برای ماندن در همان تب پس از ذخیره). مثال: ?place=Footer.</summary>
    [BindProperty(SupportsGet = true, Name = "place")] public string? ActivePlace { get; set; }

    /// <summary>مقدار فیلدهای پایه برای فرم.</summary>
    public Dictionary<string, string?> Values { get; private set; } = new();

    /// <summary>تنظیمات استودیو (ذخیره‌شده، یا ارسال‌شده اگر ذخیره خطا داد تا تغییرات مدیر از دست نرود).</summary>
    public BrandStyleConfig Config { get; private set; } = new();

    /// <summary>شعار عمومی فروشگاه (تنظیمات ← عمومی) برای placeholder شعار.</summary>
    public string? SiteTagline { get; private set; }

    public async Task OnGetAsync()
    {
        var current = await settings.GetAsync(HttpContext.RequestAborted);
        Config = BrandStyleRules.Parse(current.BrandStyleJson);
        SiteTagline = current.Tagline;
        foreach (var name in SettingFields)
            Values[name] = typeof(StoreSettingDto).GetProperty(name)?.GetValue(current) as string;
    }

    public async Task<IActionResult> OnPostAsync()
    {
        var entity = await data.Query<StoreSetting>().OrderBy(s => s.CreatedAt).FirstOrDefaultAsync();
        if (entity is null)
            return NotFound();

        // فرمان با مقدارهای فعلی شروع می‌شود و فقط فیلدهای این صفحه عوض می‌شوند.
        var command = new StoreSettingCommand();
        foreach (var (name, property) in CommandProperties)
            if (EntityProperties.TryGetValue(name, out var source) && source.PropertyType == property.PropertyType)
                property.SetValue(command, source.GetValue(entity));

        foreach (var name in SettingFields)
        {
            var text = First("g." + name);
            Values[name] = text;
            CommandProperties[name].SetValue(command, string.IsNullOrWhiteSpace(text) ? null : text.Trim());
        }

        var previous = BrandStyleRules.Parse(entity.BrandStyleJson);
        var (config, error) = ReadConfig(previous);
        Config = config;
        SiteTagline = entity.Tagline;
        if (error is not null)
        {
            ErrorMessage = error;
            return Page();
        }

        // پرچم‌های قدیمی «نمایش متن» (مرحلهٔ دهم) هم‌گام با تنظیم صریح محل‌ها می‌مانند تا هر دو یک چیز بگویند.
        command.BrandTextInHeader = config.For(BrandPlace.Header).ShowText ?? command.BrandTextInHeader;
        command.BrandTextInAuth = config.For(BrandPlace.Auth).ShowText ?? command.BrandTextInAuth;
        command.BrandTextInAdmin = config.For(BrandPlace.Admin).ShowText ?? command.BrandTextInAdmin;
        command.BrandTextInInvoice = config.For(BrandPlace.Invoice).ShowText ?? command.BrandTextInInvoice;
        command.BrandStyleJson = BrandStyleRules.Serialize(config);

        OperationResult result;
        try
        {
            result = await settings.UpdateAsync(command);
        }
        catch (DbUpdateException)
        {
            result = OperationResult.Error("یکی از مقادیر خارج از محدودهٔ مجاز است.");
        }

        if (!Succeeded(result))
        {
            ErrorMessage = result.Message;
            return Page();
        }

        await monitoring.LogActionAsync("settings.brand", "StoreSetting", entity.Id,
            $"استودیوی لوگو: {config.Places.Count} محل سفارشی" + (string.IsNullOrEmpty(First("g.LogoUrl")) ? "" : "، آیکون اختصاصی"));
        SuccessMessage = "تنظیمات لوگو ذخیره شد و همین حالا در سایت اعمال است.";
        var place = Enum.TryParse<BrandPlace>(ActivePlace, true, out var p) && Enum.IsDefined(p) ? p.ToString() : null;
        return Redirect(place is null ? PagePath : $"{PagePath}?place={place}");
    }

    /// <summary>
    /// خواندن فرم به <see cref="BrandStyleConfig"/>. خالی = پیش‌فرض (null). عدد نامعتبر → پیام خطا.
    /// بولی‌ها: نمایش آیکون/لوگو فقط وقتی خاموش‌اند ذخیره می‌شوند؛ نمایش متن همیشه صریح؛ شعار فقط اگر با پیش‌فرض محل فرق دارد.
    /// </summary>
    private (BrandStyleConfig Config, string? Error) ReadConfig(BrandStyleConfig previous)
    {
        var config = new BrandStyleConfig
        {
            Tagline = Text("s.Tagline"),
            TaglineColor = Text("s.TaglineColor"),
            PwaIcon192Url = Text("s.PwaIcon192Url"),
            PwaMaskableIconUrl = Text("s.PwaMaskableIconUrl"),
            AppleTouchIconUrl = Text("s.AppleTouchIconUrl"),
            PwaBackgroundColor = Text("s.PwaBackgroundColor"),
            IconGenBackground = previous.IconGenBackground,
            IconGenPadding = previous.IconGenPadding,
            IconGenRadius = previous.IconGenRadius,
        };

        foreach (var info in BrandStyleRules.Places)
        {
            var key = "p." + info.Place + ".";
            var label = info.Title;
            var style = new BrandPlaceStyle
            {
                Visible = info.CanHide && !Bool(key + "Visible", true) ? false : null,
                ShowIcon = Bool(key + "ShowIcon", true) ? null : false,
                ShowText = Bool(key + "ShowText", info.ShowTextDefault),
                IconUrl = Text(key + "IconUrl"),
                IconDarkUrl = info.Raster || info.OnDark ? null : Text(key + "IconDarkUrl"),
                Layout = Enum.TryParse<BrandLayout>(First(key + "Layout"), out var layout) && Enum.IsDefined(layout) && layout != info.LayoutDefault ? layout : null,
                Align = Enum.TryParse<BrandAlign>(First(key + "Align"), out var align) && Enum.IsDefined(align) && align != BrandAlign.Center ? align : null,
                Text1 = Text(key + "Text1"),
                Text2 = Text(key + "Text2"),
                Tagline = Text(key + "Tagline"),
                Color1 = Text(key + "Color1"),
                Color2 = Text(key + "Color2"),
                TaglineColor = Text(key + "TaglineColor"),
            };
            var showTagline = Bool(key + "ShowTagline", info.ShowTaglineDefault);
            style.ShowTagline = showTagline == info.ShowTaglineDefault ? null : showTagline;

            if (!Int(key + "IconHeight", out var ih)) return (config, Bad("ارتفاع آیکون", label));
            if (!Int(key + "IconHeightMobile", out var ihm)) return (config, Bad("ارتفاع آیکون موبایل", label));
            if (!Int(key + "FontSize", out var fs)) return (config, Bad("اندازهٔ قلم", label));
            if (!Int(key + "FontSizeMobile", out var fsm)) return (config, Bad("اندازهٔ قلم موبایل", label));
            if (!Int(key + "FontWeight", out var fw)) return (config, Bad("وزن قلم", label));
            if (!Int(key + "IconGap", out var gap)) return (config, Bad("فاصلهٔ آیکون تا متن", label));
            if (!Int(key + "PartGap", out var partGap)) return (config, Bad("فاصلهٔ دو بخش متن", label));
            if (!Int(key + "TaglineGap", out var taglineGap)) return (config, Bad("فاصلهٔ شعار", label));
            if (!Int(key + "TaglineSize", out var taglineSize)) return (config, Bad("اندازهٔ شعار", label));
            if (!Int(key + "TextOffsetY", out var offset)) return (config, Bad("جابه‌جایی عمودی متن", label));
            if (!Double(key + "LetterSpacing", out var spacing)) return (config, Bad("فاصلهٔ حروف", label));
            style.IconHeight = ih;
            style.IconHeightMobile = info.Responsive ? ihm : null;
            style.FontSize = fs;
            style.FontSizeMobile = info.Responsive ? fsm : null;
            style.FontWeight = fw == 800 ? null : fw;
            style.IconGap = gap;
            style.PartGap = partGap;
            style.TaglineGap = taglineGap;
            style.TaglineSize = taglineSize;
            style.TextOffsetY = offset == 0 ? null : offset;
            style.LetterSpacing = spacing is 0 ? null : spacing;

            if (!style.IsEmpty)
                config.Places[info.Place.ToString()] = style;
        }

        return (config, BrandStyleRules.Validate(config));
    }

    private static string Bad(string field, string place) => $"«{field}» در «{place}» باید عدد باشد (یا خالی برای خودکار).";

    /// <summary>اولین مقدار فرم (چک‌باکس + hidden false: اولی برنده است).</summary>
    private string? First(string name)
    {
        var values = Request.Form[name];
        return values.Count == 0 ? null : values[0];
    }

    private string? Text(string name) => BrandStyleRules.Blank(First(name));

    private bool Bool(string name, bool fallback) =>
        First(name) is { } value ? string.Equals(value, "true", StringComparison.OrdinalIgnoreCase) : fallback;

    /// <summary>عدد صحیح با رقم فارسی هم. خالی = null (خودکار).</summary>
    private bool Int(string name, out int? value)
    {
        value = null;
        var text = SearchText.NormalizeDigits(First(name) ?? "").Trim();
        if (text.Length == 0)
            return true;
        if (!int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out var number))
            return false;
        value = number;
        return true;
    }

    private bool Double(string name, out double? value)
    {
        value = null;
        var text = SearchText.NormalizeDigits(First(name) ?? "").Trim().Replace('٫', '.').Replace('/', '.');
        if (text.Length == 0)
            return true;
        if (!double.TryParse(text, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var number)
            || double.IsNaN(number) || double.IsInfinity(number))
            return false;
        value = number;
        return true;
    }

    // ── کمک‌های نما ─────────────────────────────────────────────

    /// <summary>مقدار عددی برای input (خالی اگر null). مثال: 44 → «44».</summary>
    public static string Num(int? value) => value?.ToString(CultureInfo.InvariantCulture) ?? "";

    public static string Num(double? value) => value is { } v ? BrandStyleRules.Css(v) : "";

    /// <summary>
    /// داده‌های ثابت یک محل برای اسکریپت پیش‌نمایش (data-*): ارتفاع پیش‌فرض، زمینهٔ تیره، موبایل، چینش پیش‌فرض، PNG.
    /// </summary>
    public static string PlaceData(BrandPlaceInfo info) =>
        string.Create(CultureInfo.InvariantCulture,
            $"{{\"ih\":{info.IconHeight},\"dark\":{Js(info.OnDark)},\"responsive\":{Js(info.Responsive)},\"raster\":{Js(info.Raster)},\"text\":{Js(info.ShowTextDefault)},\"tagline\":{Js(info.ShowTaglineDefault)},\"layout\":\"{info.LayoutDefault}\"}}");

    private static string Js(bool value) => value ? "true" : "false";

    /// <summary>
    /// نمایش متن مؤثر یک محل برای چک‌باکس: تنظیم صریح، وگرنه پرچم قدیمی (هدر/ورود/پنل/فاکتور) یا پیش‌فرض محل.
    /// </summary>
    public static bool EffectiveShowText(BrandPlaceInfo info, BrandPlaceStyle style, StoreSettingDto current) => style.ShowText ?? info.Place switch
    {
        BrandPlace.Header or BrandPlace.MobileMenu => current.BrandTextInHeader,
        BrandPlace.Auth => current.BrandTextInAuth,
        BrandPlace.Admin => current.BrandTextInAdmin,
        BrandPlace.Invoice => current.BrandTextInInvoice,
        _ => info.ShowTextDefault,
    };
}

/// <summary>
/// فیلد تصویر استودیو (partial «_BrandImageField»): ورودی آدرس + بندانگشتی + دکمهٔ کتابخانهٔ رسانه.
/// <paramref name="Name"/> نام فرم (مثلاً «p.Header.IconUrl»)، <paramref name="Id"/> شناسهٔ ورودی.
/// <see cref="Field"/> کلید اسکریپت پیش‌نمایش (data-f برای فیلد محل، data-g برای فیلد پایه)؛ خالی = همان Id.
/// </summary>
public sealed record BrandImageField(string Name, string Label, string? Value, string? Help, string Id)
{
    public string? Field { get; init; }
    public bool IsPlace => Name.StartsWith("p.", StringComparison.Ordinal);
    public string Key => Field ?? Id;
}

/// <summary>
/// فیلد رنگ (partial «_BrandColorField»): متن هگز (خالی = پیش‌فرض <paramref name="Fallback"/>) + انتخابگر رنگ مرورگر.
/// <see cref="Place"/> = فیلد اختصاصی یک محل (data-f) وگرنه فیلد پایه (data-g).
/// </summary>
public sealed record BrandColorField(string Name, string Label, string? Value, string Fallback, string? Help, string Id, string Key)
{
    public bool Place { get; init; }
}

/// <summary>
/// اسلایدر + عدد (partial «_BrandRange»). فقط ورودی عددی نام فرم دارد؛ اسلایدر کمکی است. خالی = خودکار
/// (اسکریپت مقدار خودکار را در placeholder و جای اسلایدر نشان می‌دهد). مثال: ارتفاع آیکون هدر، ۱۲ تا ۲۰۰.
/// </summary>
public sealed record BrandRange(string Name, string Id, string Label, int? Value, int Min, int Max, string Key, string? Placeholder);
