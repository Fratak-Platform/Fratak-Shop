using System.Text.Json;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Settings;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Sms;
using Fratak_Shop.Web.Infrastructure.Team;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.SystemPages;

/// <summary>
/// صفحهٔ «فنی ← سرویس پیامک» (دور هشتم): سرویس‌دهنده (SMS.ir / فقط لاگ)، کلید API (رمزنگاری‌شده؛ هرگز نمایش داده نمی‌شود)،
/// خط ارسال، قالب کد تأیید و نام متغیر آن؛ به‌علاوهٔ «بررسی اتصال» (اعتبار و خط‌های حساب) و «پیامک آزمایشی».
/// مشاهده: نظارت یا مدیریت فنی. ذخیره و آزمون: فقط مدیریت فنی؛ ذخیره «تأیید دوبارهٔ هویت» هم می‌خواهد چون کد ورود همهٔ کاربران از این مسیر می‌رود.
/// </summary>
[AdminPermission(AdminPermissions.MonitoringView, AdminPermissions.SystemManage)]
public class SmsModel(ISmsProviderService sms, ConfigurableSmsSender sender, IMonitoringService monitoring) : AdminPageModel
{
    private const string PagePath = "/admin/system/sms";

    public SmsProviderView Config { get; private set; } = null!;

    /// <summary>کلید از کجا می‌آید: «پنل مدیریت»، «تنظیمات سرور» یا null (ثبت نشده).</summary>
    public string? KeySource { get; private set; }

    public SmsDiagnostics Stats => sender.Diagnostics;

    public sealed class SmsForm
    {
        public SmsProvider Provider { get; set; }
        public string? ApiKey { get; set; }
        public bool ClearApiKey { get; set; }
        public string? LineNumber { get; set; }
        public string? OtpTemplateId { get; set; }
        public string? OtpParameterName { get; set; }
    }

    public async Task OnGetAsync()
    {
        var ct = HttpContext.RequestAborted;
        Config = await sms.GetAsync(ct);
        KeySource = (await sender.DescribeAsync(ct)).KeySource;
    }

    [AdminPermission(AdminPermissions.SystemManage)]
    [RequireReauth]
    public async Task<IActionResult> OnPostSaveAsync([FromForm(Name = "sms")] SmsForm form)
    {
        var result = await sms.SaveAsync(new SmsProviderInput(form.Provider, form.ApiKey, form.ClearApiKey, form.LineNumber, form.OtpTemplateId, form.OtpParameterName),
            HttpContext.RequestAborted);
        if (Succeeded(result))
        {
            var keyNote = form.ClearApiKey ? "، حذف کلید" : string.IsNullOrWhiteSpace(form.ApiKey) ? "" : "، کلید جدید";
            await monitoring.LogActionAsync("SmsProviderSave", "System", null, $"ویرایش سرویس پیامک ({form.Provider}{keyNote})");
        }
        ShowResult(result);
        return Redirect(PagePath);
    }

    /// <summary>بررسی کلید بدون ارسال پیامک: اعتبار حساب و خط‌ها.</summary>
    [AdminPermission(AdminPermissions.SystemManage)]
    public async Task<IActionResult> OnPostCheckAsync()
    {
        var (credit, lines) = await sender.CheckAccountAsync(HttpContext.RequestAborted);
        if (!credit.Ok)
        {
            ErrorMessage = "بررسی اتصال ناموفق بود: " + credit.Message;
            return Redirect(PagePath);
        }

        var text = $"اتصال برقرار است. اعتبار حساب: {Describe(credit.Data)}";
        if (lines is { Ok: true })
            text += $" · خط‌های حساب: {Describe(lines.Data)}";
        SuccessMessage = text;
        return Redirect(PagePath);
    }

    /// <summary>ارسال یک پیامک واقعی به شمارهٔ واردشده (هزینهٔ یک پیامک دارد). kind=template → قالب کد تأیید؛ وگرنه متن از خط.</summary>
    [AdminPermission(AdminPermissions.SystemManage)]
    public async Task<IActionResult> OnPostTestAsync(string? phone, string? kind)
    {
        var mobile = SearchText.NormalizeDigits(phone);
        if (!mobile.IsValidMobile())
        {
            ErrorMessage = "شمارهٔ موبایل معتبر نیست (مثال: 09121234567).";
            return Redirect(PagePath);
        }

        var template = kind == "template";
        var response = await sender.TestAsync(mobile, template, HttpContext.RequestAborted);
        await monitoring.LogActionAsync("SmsProviderTest", "System", null,
            $"پیامک آزمایشی ({(template ? "قالب کد" : "متنی")}) به {ConfigurableSmsSender.Mask(mobile)}: {(response.Ok ? "موفق" : "ناموفق")}");
        if (response.Ok)
            SuccessMessage = $"پیامک آزمایشی به {mobile} تحویل SMS.ir شد.";
        else
            ErrorMessage = "ارسال ناموفق بود: " + response.Message;
        return Redirect(PagePath);
    }

    /// <summary>نمایش کوتاه بخش data پاسخ: عدد → «1,234»؛ آرایه → «300…، 500…»؛ بقیه خالی.</summary>
    public static string Describe(JsonElement? data)
    {
        if (data is not { } d)
            return "-";
        return d.ValueKind switch
        {
            JsonValueKind.Number when d.TryGetDecimal(out var n) => n.ToString("#,0.##", global::System.Globalization.CultureInfo.InvariantCulture),
            JsonValueKind.Array => string.Join("، ", d.EnumerateArray().Take(10).Select(e => e.ToString())),
            JsonValueKind.String => d.GetString() ?? "-",
            _ => "-",
        };
    }
}
