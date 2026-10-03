using Fratak_Shop.Core.DTOs.Faqs.Command;
using Fratak_Shop.Core.DTOs.Faqs.Query;
using Fratak_Shop.Core.Services.Faqs;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Core.Services.Messaging;
using Fratak_Shop.Core.Services.Navigation;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Payments;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Areas.Admin.Pages.Content;

[Authorize(Policy = PolicyNames.Content)]
public class FaqsModel(
    ISiteSettingService settings,
    IMenuService menus,
    INotificationService notifications,
    IBankTransferService bankTransfers,
    ISeoAuditService seoAudits,
    ISmsSender smsSender,
    IFaqItemService faqs)
    : AdminPageModel(settings, menus, notifications, bankTransfers, seoAudits, smsSender)
{
    public List<FaqItemDto> Items { get; private set; } = [];

    [BindProperty(SupportsGet = true)]
    public Guid? Edit { get; set; }

    [BindProperty]
    public FaqInput Input { get; set; } = new();

    public void OnGet()
    {
        ViewData["Title"] = "پرسش‌های متداول";
        Items = faqs.GetAllFaqItemsAsync().OrderBy(item => item.DisplayOrder).ToList();

        if (Edit.HasValue)
        {
            var current = Items.FirstOrDefault(item => item.Id == Edit.Value);
            if (current is not null)
            {
                Input = new FaqInput
                {
                    Id = current.Id,
                    Question = current.Question,
                    Answer = current.Answer,
                    DisplayOrder = current.DisplayOrder,
                    IsActive = current.IsActive,
                };
            }
        }
    }

    public async Task<IActionResult> OnPostSaveAsync()
    {
        if (!ModelState.IsValid)
        {
            Notify("اطلاعات پرسش کامل نیست.", "error");
            return RedirectToPage("/Content/Faqs", new { area = "Admin" });
        }

        var result = Input.Id.HasValue
            ? await faqs.EditFaqItemAsync(new EditFaqItemCommand
            {
                Id = Input.Id.Value,
                Question = Input.Question,
                Answer = Input.Answer,
                DisplayOrder = Input.DisplayOrder,
                IsActive = Input.IsActive,
            })
            : await faqs.CreateFaqItemAsync(new CreateFaqItemCommand
            {
                Question = Input.Question,
                Answer = Input.Answer,
                DisplayOrder = Input.DisplayOrder,
                IsActive = Input.IsActive,
            });

        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Content/Faqs", new { area = "Admin" });
    }

    public async Task<IActionResult> OnPostDeleteAsync(Guid id)
    {
        var result = await faqs.DeleteFaqItemAsync(id);
        Notify(result.Message, result.IsSuccess() ? "success" : "error");
        return RedirectToPage("/Content/Faqs", new { area = "Admin" });
    }

    public class FaqInput
    {
        public Guid? Id { get; set; }

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "متن پرسش را وارد کنید.")]
        public string Question { get; set; } = null!;

        [System.ComponentModel.DataAnnotations.Required(ErrorMessage = "پاسخ را وارد کنید.")]
        public string Answer { get; set; } = null!;

        public int DisplayOrder { get; set; }
        public bool IsActive { get; set; } = true;
    }
}
