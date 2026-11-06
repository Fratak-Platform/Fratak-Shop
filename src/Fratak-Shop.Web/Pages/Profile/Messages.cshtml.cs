using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Core.DTOs.Messages.Command;
using Fratak_Shop.Core.DTOs.Messages.Query;
using Fratak_Shop.Core.Services.Messages;
using Fratak_Shop.Core.Services.Users;
using Fratak_Shop.Data.Entities.Messages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Mvc;

namespace Fratak_Shop.Web.Pages.Profile;

/// <summary>Support tickets of the customer: history and a form to send a new one.</summary>
public class MessagesModel(IMessageService messageService, IUserService userService) : ShopPageModel
{
    public List<MessageFilterData> Messages { get; private set; } = [];

    public static readonly (MessageType Type, string Title)[] Types =
    [
        (MessageType.Support, "پشتیبانی"),
        (MessageType.Suggestion, "پیشنهاد"),
        (MessageType.Complaint, "شکایت"),
    ];

    [BindProperty]
    public MessageInput Input { get; set; } = new();

    public async Task OnGetAsync()
    {
        await LoadAsync();
    }

    public async Task<IActionResult> OnPostAsync()
    {
        if (!ModelState.IsValid)
        {
            await LoadAsync();
            return Page();
        }

        var user = await userService.GetUserByIdAsync(CurrentUserId!.Value);

        var result = await messageService.CreateMessageAsync(new CreateMessageCommand
        {
            UserId = CurrentUserId,
            Type = Types.Any(t => t.Type == Input.Type) ? Input.Type : MessageType.Support,
            FullName = user?.FullName ?? user?.PhoneNumber,
            PhoneNumber = user?.PhoneNumber,
            Email = user?.Email,
            Subject = Input.Subject.Trim(),
            Content = Input.Content.Trim(),
        });

        ShowResult(result);
        return RedirectToPage();
    }

    private async Task LoadAsync()
    {
        Messages = await messageService.GetMessagesByUserAsync(CurrentUserId!.Value);
        ViewData[PageMetaKeys.Title] = "پیغام ها";
        ViewData[PageMetaKeys.Robots] = "noindex, nofollow";
    }

    public class MessageInput
    {
        public MessageType Type { get; set; } = MessageType.Support;

        [Required(ErrorMessage = "موضوع پیام را وارد کنید.")]
        [MaxLength(250)]
        public string Subject { get; set; } = string.Empty;

        [Required(ErrorMessage = "متن پیام را وارد کنید.")]
        [StringLength(4000, MinimumLength = 5, ErrorMessage = "متن پیام باید بین ۵ تا ۴۰۰۰ کاراکتر باشد.")]
        public string Content { get; set; } = string.Empty;
    }
}
