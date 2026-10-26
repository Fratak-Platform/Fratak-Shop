using System.Text.RegularExpressions;
using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Team;
using Fratak_Shop.Web.Infrastructure.Admin;
using Fratak_Shop.Web.Infrastructure.Auth;
using Microsoft.AspNetCore.DataProtection;

namespace Fratak_Shop.Web.Infrastructure.Team;

// ═══════════════════════════════════════════════════════════════════════════
//  لایهٔ Web امنیت و کار تیمی کارکنان (مرحلهٔ هفتم – فاز ۱۰)
//  • RequireReauthAttribute + ReauthTicket : «تأیید دوباره» پیش از کارهای حساس (کوکی کوتاه‌عمر رمزنگاری‌شده).
//  • AllowWithoutTotpAttribute             : صفحه‌هایی که وقتی TOTP اجباری است، بدون آن هم باز می‌شوند (راه‌اندازی، تأیید دوباره).
//  • SensitiveData                          : پوشاندن موبایل/ایمیل/نشانی/شبا در نماهای پنل بر اساس تنظیمات و دسترسی.
//  • MentionHighlighter                     : نمایش منشن‌ها (@نام) به شکل برچسب رنگی در یادداشت‌ها.
//  • TeamWorker                             : یادآوری موعد کارها و منقضی کردن درخواست‌های تأیید.
//  بررسی‌ها در AdminPageFilter انجام می‌شود (Infrastructure/Admin/AdminAuthorization.cs).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>
/// روی handler یا کلاس صفحهٔ پنل: اگر «تأیید دوباره» در تنظیمات روشن باشد و کارمند در N دقیقهٔ اخیر هویتش را تأیید نکرده باشد،
/// به‌جای اجرا به /admin/reauth فرستاده می‌شود و بعد از تأیید به همان صفحه برمی‌گردد.
/// مثال: <c>[RequireReauth] public async Task&lt;IActionResult&gt; OnPostWalletAsync(...)</c>
/// </summary>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method, Inherited = true)]
public sealed class RequireReauthAttribute : Attribute;

/// <summary>صفحه‌ای که حتی وقتی «برنامهٔ احراز هویت اجباری» است و کارمند هنوز راه‌اندازی نکرده، باز می‌شود.</summary>
[AttributeUsage(AttributeTargets.Class)]
public sealed class AllowWithoutTotpAttribute : Attribute;

/// <summary>
/// نگه‌داری «زمان آخرین تأیید دوباره» در کوکی <c>fratak_reauth</c>.
/// محتوا (رمزنگاری و امضاشده با Data Protection): «شناسهٔ کاربر|شناسهٔ نشست|زمان یونیکس».
/// چون شناسهٔ نشست هم داخلش است، کوکی دزدیده‌شده روی نشست دیگر (یا بعد از خروج و ورود دوباره) بی‌اثر است.
/// </summary>
public interface IReauthTicket
{
    void Issue(HttpContext context, Guid userId);

    /// <summary>آیا این کارمند در <paramref name="minutes"/> دقیقهٔ اخیر و در همین نشست هویتش را دوباره تأیید کرده است؟</summary>
    bool IsFresh(HttpContext context, Guid userId, int minutes);

    void Clear(HttpContext context);
}

public sealed class ReauthTicket(IDataProtectionProvider provider) : IReauthTicket
{
    public const string CookieName = "fratak_reauth";

    /// <summary>بیشترین عمر کوکی (حداکثر مقدار تنظیم «مدت اعتبار» ۲۴۰ دقیقه است).</summary>
    private static readonly TimeSpan MaxLifetime = TimeSpan.FromHours(4);

    private readonly IDataProtector _protector = provider.CreateProtector("Fratak.Admin.Reauth.v1");

    public void Issue(HttpContext context, Guid userId)
    {
        var session = context.User.GetSessionId() ?? Guid.Empty;
        var value = _protector.Protect($"{userId:N}|{session:N}|{DateTimeOffset.UtcNow.ToUnixTimeSeconds()}");
        context.Response.Cookies.Append(CookieName, value, new CookieOptions
        {
            HttpOnly = true,
            Secure = context.Request.IsHttps,
            SameSite = Microsoft.AspNetCore.Http.SameSiteMode.Strict,
            IsEssential = true,
            Path = "/admin",
            MaxAge = MaxLifetime,
        });
    }

    public bool IsFresh(HttpContext context, Guid userId, int minutes)
    {
        if (!context.Request.Cookies.TryGetValue(CookieName, out var value) || string.IsNullOrWhiteSpace(value))
            return false;
        try
        {
            var parts = _protector.Unprotect(value).Split('|');
            if (parts.Length != 3 || !Guid.TryParseExact(parts[0], "N", out var id) || id != userId
                || !Guid.TryParseExact(parts[1], "N", out var session) || session != (context.User.GetSessionId() ?? Guid.Empty)
                || !long.TryParse(parts[2], out var unix))
                return false;
            var age = DateTimeOffset.UtcNow - DateTimeOffset.FromUnixTimeSeconds(unix);
            return age >= TimeSpan.FromMinutes(-1) && age <= TimeSpan.FromMinutes(Math.Clamp(minutes, 5, 240));
        }
        catch (System.Security.Cryptography.CryptographicException)
        {
            return false;
        }
    }

    public void Clear(HttpContext context) =>
        context.Response.Cookies.Delete(CookieName, new CookieOptions { Path = "/admin" });
}

/// <summary>
/// نمایش اطلاعات شخصی مشتری در نماهای پنل. اگر «پوشاندن اطلاعات» در تنظیمات روشن باشد و کارمند دسترسی
/// «دیدن اطلاعات کامل مشتری» نداشته باشد، شکل پوشانده برمی‌گردد؛ وگرنه همان مقدار کامل.
/// شبا/کارت با دسترسی «تسویه برداشت‌ها» دیده می‌شود. مالک فروشگاه همیشه همه‌چیز را می‌بیند.
/// استفاده در نما (از طریق _ViewImports پنل): <c>@Pii.Phone(o.CustomerPhone)</c>، <c>@if (Pii.ShowContacts) { دکمهٔ کپی }</c>.
/// </summary>
public sealed class SensitiveData(IHttpContextAccessor accessor, ISiteContext site)
{
    private bool? _maskContacts;
    private bool? _maskBank;

    private AdminAccess? Access => accessor.HttpContext?.Items[AdminPageFilter.AccessItemKey] as AdminAccess;

    /// <summary>موبایل، ایمیل و نشانی پوشانده می‌شوند؟</summary>
    public bool MaskContacts => _maskContacts ??= site.Settings.IsDataMaskingEnabled && Access?.Has(AdminPermissions.CustomersSensitive) != true;

    /// <summary>شبا، کارت و شماره حساب پوشانده می‌شوند؟</summary>
    public bool MaskBank => _maskBank ??= site.Settings.IsDataMaskingEnabled && Access?.Has(AdminPermissions.WithdrawalsManage) != true;

    /// <summary>برای نمایش دکمه‌های «کپی» و لینک‌های tel:/mailto: (فقط وقتی مقدار کامل دیده می‌شود).</summary>
    public bool ShowContacts => !MaskContacts;

    public bool ShowBank => !MaskBank;

    public string Phone(string? value) => MaskContacts ? Masking.Phone(value) : value ?? string.Empty;

    public string Email(string? value) => MaskContacts ? Masking.Email(value) : value ?? string.Empty;

    public string Address(string? value) => MaskContacts ? Masking.Address(value) : value ?? string.Empty;

    public string Iban(string? value) => MaskBank ? Masking.Iban(value) : value ?? string.Empty;

    public string Card(string? value) => MaskBank ? Masking.Card(value) : value ?? string.Empty;

    /// <summary>
    /// نام نمایشی مشتری: اگر نام ندارد معمولاً موبایل نشان داده می‌شود؛ این متد همان را (در صورت لزوم پوشانده) برمی‌گرداند.
    /// مثال: NameOrPhone("", "09121234567") ← «0912•••4567».
    /// </summary>
    public string NameOrPhone(string? name, string? phone) => string.IsNullOrWhiteSpace(name) ? Phone(phone) : name;
}

/// <summary>
/// نمایش منشن‌ها: متن یادداشت HTML-امن می‌شود و هر «@نام» در یک برچسب رنگی قرار می‌گیرد.
/// مثال: «@سارا_محمدی لطفاً تماس بگیر» ← «&lt;span class="a-mention"&gt;@سارا_محمدی&lt;/span&gt; لطفاً تماس بگیر».
/// محل استفاده: یادداشت‌های مشتری و سفارش (Customers/Detail، Orders/Detail).
/// </summary>
public static partial class MentionHighlighter
{
    [GeneratedRegex(@"(?<![\p{L}\p{Nd}_])@([\p{L}\p{Mn}\p{Nd}_\u200c]{2,60})", RegexOptions.CultureInvariant)]
    private static partial Regex Pattern();

    public static Microsoft.AspNetCore.Html.HtmlString Render(string? text)
    {
        if (string.IsNullOrEmpty(text))
            return Microsoft.AspNetCore.Html.HtmlString.Empty;
        // WebUtility فقط < > & " ' را کد می‌کند (حروف فارسی دست‌نخورده می‌مانند تا regex کار کند).
        var encoded = System.Net.WebUtility.HtmlEncode(text);
        return new Microsoft.AspNetCore.Html.HtmlString(Pattern().Replace(encoded, "<span class=\"a-mention\">@$1</span>"));
    }
}

/// <summary>
/// کار پس‌زمینهٔ تیم: هر ۵ دقیقه یادآوری کارهای سررسیده را می‌فرستد و درخواست‌های تأییدِ تاریخ‌گذشته را «منقضی» می‌کند.
/// در صفحهٔ «کارهای پس‌زمینه» با نام JobName دیده و قابل توقف است.
/// </summary>
public sealed class TeamWorker(IServiceScopeFactory scopeFactory, ILogger<TeamWorker> logger) : BackgroundService
{
    public const string JobName = "یادآوری کارها و تأییدهای کارکنان";

    private static readonly TimeSpan Tick = TimeSpan.FromMinutes(5);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        JobMonitor.Register(JobName, TimeSpan.FromMinutes(15));
        try
        {
            await Task.Delay(TimeSpan.FromSeconds(45), stoppingToken);
            using var timer = new PeriodicTimer(Tick);
            do
            {
                try
                {
                    await JobMonitor.TrackAsync(JobName, async () =>
                    {
                        using var scope = scopeFactory.CreateScope();
                        var now = DateTimeOffset.UtcNow;
                        var reminders = await scope.ServiceProvider.GetRequiredService<IStaffTaskService>().SendDueRemindersAsync(now, stoppingToken);
                        var expired = await scope.ServiceProvider.GetRequiredService<IApprovalService>().ExpireDueAsync(now, stoppingToken);
                        if (reminders + expired > 0)
                            scope.ServiceProvider.GetRequiredService<AdminBadgeService>().Invalidate();
                        return reminders + expired;
                    });
                }
                catch (Exception exception) when (exception is not OperationCanceledException)
                {
                    logger.LogError(exception, "Team reminders failed");
                }
            }
            while (await timer.WaitForNextTickAsync(stoppingToken));
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
        }
    }
}
