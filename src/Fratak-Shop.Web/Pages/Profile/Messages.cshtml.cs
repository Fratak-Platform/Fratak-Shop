using Fratak_Shop.Core.Services.Messages;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Entities.Messages;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

[Authorize(Policy = PolicyNames.CustomerOnly)]
public class MessagesModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IMessageService messages)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    public List<Message> Items { get; private set; } = [];

    [BindProperty]
    public MessageInput Input { get; set; } = new();

    public async Task<IActionResult> OnGetAsync()
    {
        if (CurrentUserId is not { } userId)
            return Redirect("/login?returnUrl=/profile/messages");

        Items = await messages.GetMessagesByUserAsync(userId);
        ApplySeo(null, BuildFallback());
        return Page();
    }

    public async Task<IActionResult> OnPostSendAsync()
    {
        if (CurrentUserId is not { } userId)
            return Redirect("/login");

        if (!ModelState.IsValid)
        {
            Items = await messages.GetMessagesByUserAsync(userId);
            ApplySeo(null, BuildFallback());
            return Page();
        }

        var result = await messages.CreateMessageAsync(new Message
        {
            UserId = userId,
            Type = MessageType.Support,
            FullName = CurrentUserName,
            PhoneNumber = User.FindFirst(System.Security.Claims.ClaimTypes.MobilePhone)?.Value,
            Subject = Input.Subject,
            Content = Input.Content,
        });

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Profile/Messages");
    }

    private SeoFallback BuildFallback() => new()
    {
        SiteName = Site.SiteName,
        SiteUrl = Site.SiteUrl,
        Path = "/profile/messages",
        ContentTitle = "پیام‌های من",
        EntityType = SeoEntityType.Page,
        ForceNoIndex = true,
    };

    public class MessageInput
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "موضوع پیام را وارد کنید.")]
        public string Subject { get; set; } = null!;

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "متن پیام را وارد کنید.")]
        public string Content { get; set; } = null!;
    }
}
