using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.DTOs.Settings.Query;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Utilities.SeoManagement;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Fratak_Shop.Web.Infrastructure;

/// <summary>
/// Base page model of the storefront. It loads the shared layout data (settings,
/// dynamic menus, cart badge, notifications) and gives every page a single way to
/// publish its SEO model, so no page has to repeat that plumbing.
/// </summary>
public abstract class FratakPageModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService) : PageModel
{
    public StorefrontSettings Site { get; private set; } = new();

    public LayoutViewModel Layout { get; private set; } = new();

    public SeoRenderModel Seo { get; private set; } = new();

    /// <summary>شناسه کاربر وارد‌شده (اگر وارد شده باشد).</summary>
    protected Guid? CurrentUserId =>
        Guid.TryParse(User.FindFirst(FratakClaims.UserId)?.Value, out var id) ? id : null;

    protected string CurrentUserName => User.FindFirst(FratakClaims.FullName)?.Value ?? string.Empty;

    /// <summary>کلید سبد خرید مهمان؛ در اولین بازدید ساخته و در سشن ذخیره می‌شود.</summary>
    public string CartSessionKey
    {
        get
        {
            var key = HttpContext.Session.GetString(SessionKeys.Cart);
            if (!string.IsNullOrWhiteSpace(key))
                return key;

            key = Guid.NewGuid().ToString("N");
            HttpContext.Session.SetString(SessionKeys.Cart, key);
            return key;
        }
    }

    public override async Task OnPageHandlerExecutionAsync(PageHandlerExecutingContext context,
        PageHandlerExecutionDelegate next)
    {
        Site = await settings.GetStorefrontSettingsAsync();

        Layout = new LayoutViewModel
        {
            Site = Site,
            HeaderMenu = await menus.GetHeaderAsync(),
            FooterColumns = await menus.GetFooterColumnsAsync(),
            FooterBottom = await menus.GetFooterBottomAsync(),
            ProfileMenu = await menus.GetProfileMenuAsync(),
            HeaderSocials = await menus.GetSocialLinksAsync(inHeader: true),
            FooterSocials = await menus.GetSocialLinksAsync(inFooter: true),
            CurrentPath = HttpContext.Request.Path.Value ?? "/",
            IsAuthenticated = CurrentUserId.HasValue,
            UserFullName = CurrentUserName,
            CartCount = await cart.GetItemCountAsync(CurrentUserId, CartSessionKey),
        };

        if (CurrentUserId.HasValue)
            Layout.UnreadNotifications = await notifications.GetUnreadCountAsync(CurrentUserId.Value);

        ViewData[ViewDataKeys.Layout] = Layout;
        ViewData[ViewDataKeys.Seo] = Seo;

        await next();
    }

    /// <summary>ثبت مدل سئوی صفحه؛ در قالب اصلی رندر می‌شود.</summary>
    protected void ApplySeo(SeoMetaData? seo, SeoFallback fallback)
    {
        Seo = seoService.Build(seo, fallback);
        ViewData[ViewDataKeys.Seo] = Seo;
    }

    /// <summary>اطلاع‌رسانی نتیجه عملیات به کاربر در قالب پیام موقت.</summary>
    protected void Notify(string message, string type = "success")
    {
        TempData[ViewDataKeys.Message] = message;
        TempData[ViewDataKeys.MessageType] = type;
    }

    /// <summary>خواندن شناسه کاربر یا هدایت به صفحه ورود.</summary>
    protected bool TryGetUserId(out Guid userId)
    {
        if (CurrentUserId is { } id)
        {
            userId = id;
            return true;
        }

        userId = Guid.Empty;
        return false;
    }
}
