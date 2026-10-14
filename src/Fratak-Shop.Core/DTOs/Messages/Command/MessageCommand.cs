using Fratak_Shop.Data.Entities.Messages;

namespace Fratak_Shop.Core.DTOs.Messages.Command;

/// <summary>
/// دستور ثبت پیام (فرم تماس با ما، پشتیبانی، پیشنهاد یا شکایت).
/// اطلاعات فرستنده برای کاربر وارد‌شده از پروفایل و برای مهمان از فرم خوانده می‌شود.
/// </summary>
public class CreateMessageCommand
{
    public Guid? UserId { get; set; }

    public MessageType Type { get; set; }

    public string? FullName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? Subject { get; set; }
    public string Content { get; set; } = null!;
}

/// <summary>
/// دستور ویرایش پیام توسط مدیر (اصلاح متن، نوع و وضعیت پیگیری).
/// </summary>
public class EditMessageCommand
{
    public Guid Id { get; set; }

    public MessageType Type { get; set; }

    public string? FullName { get; set; }
    public string? PhoneNumber { get; set; }
    public string? Email { get; set; }
    public string? Subject { get; set; }
    public string Content { get; set; } = null!;

    public MessageStatus Status { get; set; }
}
