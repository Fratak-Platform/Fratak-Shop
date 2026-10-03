using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Core.Services.Messages;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Data.Entities.Messages;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>دریافت پیام‌های فرم تماس با ما (صفحه محتوایی /page/contact).</summary>
public class ContactModel(
    ISiteSettingService settings,
    IMenuService menus,
    ICartService cart,
    INotificationService notifications,
    ISeoService seoService,
    IMessageService messages)
    : FratakPageModel(settings, menus, cart, notifications, seoService)
{
    [BindProperty]
    public ContactInput Contact { get; set; } = new();

    public IActionResult OnGet() => Redirect("/page/contact");

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            Notify("اطلاعات فرم کامل نیست.", "error");
            return Redirect("/page/contact");
        }

        var result = await messages.CreateMessageAsync(new Message
        {
            UserId = CurrentUserId,
            Type = MessageType.Contact,
            FullName = Contact.FullName,
            PhoneNumber = Contact.PhoneNumber,
            Subject = Contact.Subject,
            Content = Contact.Content,
        });

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return Redirect("/page/contact");
    }

    public class ContactInput
    {
        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "نام خود را وارد کنید.")]
        public string FullName { get; set; } = null!;

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "شماره موبایل را وارد کنید.")]
        [System.ComponentModel.DataAnnotations.RegularExpression(@"^09\d{9}$", ErrorMessage = "شماره موبایل معتبر نیست.")]
        public string PhoneNumber { get; set; } = null!;

        public string? Subject { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "متن پیام را وارد کنید.")]
        public string Content { get; set; } = null!;
    }
}
