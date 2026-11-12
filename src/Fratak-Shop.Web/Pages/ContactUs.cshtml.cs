using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Messages.Command;
using Fratak_Shop.Core.Services.Messages;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Data.Entities.Messages;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Security;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

public class ContactUsModel(IMessageService messageService, IUserService userService, IPageMetaService pageMeta) : ShopPageModel
{
    [BindProperty]
    public ContactInput Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        if (CurrentUserId.HasValue)
        {
            var user = await userService.GetUserByIdAsync(CurrentUserId.Value);
            if (user is not null)
            {
                Input.FullName = user.FullName;
                Input.PhoneNumber = user.PhoneNumber;
                Input.Email = user.Email;
            }
        }

        await ApplySeoAsync();
        PrepareCaptcha(CaptchaActions.Contact);
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!VerifyCaptcha(CaptchaActions.Contact) || !ModelState.IsValid)
        {
            await ApplySeoAsync();
            return Page();
        }

        var result = await messageService.CreateMessageAsync(new CreateMessageCommand
        {
            UserId = CurrentUserId,
            Type = MessageType.Contact,
            FullName = Input.FullName?.Trim(),
            PhoneNumber = Input.PhoneNumber?.Trim(),
            Email = string.IsNullOrWhiteSpace(Input.Email) ? null : Input.Email.Trim(),
            Subject = Input.Subject?.Trim(),
            Content = Input.Content.Trim(),
        });

        if (!Succeeded(result))
        {
            ModelState.AddModelError(string.Empty, result.Message);
            await ApplySeoAsync();
            return Page();
        }

        CaptchaFailed(CaptchaActions.Contact); // every message counts (spam guard)
        SuccessMessage = result.Message;
        return RedirectToPage();
    }

    private async Task ApplySeoAsync()
    {
        await pageMeta.ApplyAsync(ViewData, SitePage.ContactUs, "تماس با ما", "راه های ارتباط با پشتیبانی فروشگاه و ارسال پیام.");
        pageMeta.AddBreadcrumbs(ViewData, ("تماس با ما", null));
    }

    public class ContactInput
    {
        [Required(ErrorMessage = "نام و نام خانوادگی را وارد کنید.")]
        [MaxLength(250)]
        public string? FullName { get; set; }

        [Required(ErrorMessage = "شماره همراه را وارد کنید.")]
        [RegularExpression(@"^0\d{10}$", ErrorMessage = "شماره همراه باید ۱۱ رقم و با ۰ شروع شود.")]
        public string? PhoneNumber { get; set; }

        [EmailAddress(ErrorMessage = "ایمیل وارد شده معتبر نیست.")]
        [MaxLength(255)]
        public string? Email { get; set; }

        [MaxLength(250)]
        public string? Subject { get; set; }

        [Required(ErrorMessage = "متن پیام را وارد کنید.")]
        [StringLength(4000, MinimumLength = 5, ErrorMessage = "متن پیام باید بین ۵ تا ۴۰۰۰ کاراکتر باشد.")]
        public string Content { get; set; } = string.Empty;
    }
}
