using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Messages.Command;
using Fratak_Shop.Core.Services.Messages;
using Fratak_Shop.Data.Entities.Messages;
using Fratak_Shop.Web.Infrastructure;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages;

/// <summary>Maintenance / launch page that collects phone numbers for a notification.</summary>
public class ComingSoonModel(IMessageService messageService) : ShopPageModel
{
    [BindProperty]
    [Required(ErrorMessage = "شماره همراه را وارد کنید.")]
    [RegularExpression(@"^09\d{9}$", ErrorMessage = "شماره همراه معتبر نیست.")]
    public string PhoneNumber { get; set; } = string.Empty;

    public void OnGet()
    {
        ViewData[PageMetaKeys.Title] = "به زودی";
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            ErrorMessage = ModelState.Values.SelectMany(v => v.Errors).Select(e => e.ErrorMessage).FirstOrDefault();
            return RedirectToPage();
        }

        var result = await messageService.CreateMessageAsync(new CreateMessageCommand
        {
            UserId = CurrentUserId,
            Type = MessageType.Suggestion,
            FullName = "درخواست اطلاع رسانی",
            PhoneNumber = PhoneNumber,
            Subject = "اطلاع رسانی بازگشایی",
            Content = $"لطفاً هنگام بازگشایی به شماره {PhoneNumber} اطلاع دهید.",
        });

        if (Succeeded(result))
            result.Message = "شماره شما ثبت شد؛ در اولین فرصت به شما اطلاع می دهیم.";

        ShowResult(result);
        return RedirectToPage();
    }
}
