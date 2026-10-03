using Fratak_Shop.Data.Entities.Users;

namespace Fratak_Shop.Core.DTOs.Auth.Query;

/// <summary>نتیجه عملیات احراز هویت که لایه وب برای ورود/هدایت از آن استفاده می‌کند.</summary>
public class AuthResultDto
{
    public Guid UserId { get; set; }

    public string PhoneNumber { get; set; } = null!;

    public string? FullName { get; set; }

    public UserRole PrimaryRole { get; set; }

    /// <summary>تمام نقش‌های کاربر (نقش اصلی + نقش‌های اضافه).</summary>
    public List<UserRole> Roles { get; set; } = [];

    public bool IsPhoneVerified { get; set; }

    /// <summary>آیا کاربر باید کد تأیید را وارد کند؟</summary>
    public bool RequiresPhoneVerification { get; set; }

    /// <summary>آیا کاربر اجازه ورود به پنل مدیریت دارد؟</summary>
    public bool IsStaff { get; set; }

    /// <summary>مسیر پیشنهادی پس از ورود (پنل مدیریت برای کارکنان، پروفایل برای مشتری).</summary>
    public string LandingPath { get; set; } = "/profile";

    /// <summary>
    /// در حالت توسعه، کد تأیید برای تست بدون سرویس پیام‌رسان برگردانده می‌شود.
    /// </summary>
    public string? DevelopmentCode { get; set; }
}
