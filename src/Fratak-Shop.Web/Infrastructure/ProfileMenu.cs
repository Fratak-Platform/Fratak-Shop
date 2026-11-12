using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Web.Models;

namespace Fratak_Shop.Web.Infrastructure;

/// <summary>دور ۲۹: یک ردیف منوی حساب کاربری.</summary>
/// <param name="Section">بخش پروفایل (برای پررنگ کردن ردیف فعال)؛ پنل مدیریت ← null.</param>
/// <param name="Url">آدرس (همیشه داخلی).</param>
/// <param name="Title">عنوان فارسی.</param>
/// <param name="Icon">کلید آیکون (Helpers/Icons).</param>
/// <param name="Group">گروه نمایش در ستون پروفایل («مدیریت»، «حساب من»، «خریدها»، «علاقه‌مندی‌ها»، «ارتباط»).</param>
/// <param name="InDropdown">در منوی کشویی هدر هم دیده شود؟ (منوی هدر خلاصه است؛ ستون پروفایل کامل.)</param>
/// <param name="Staff">ردیف ویژهٔ کارکنان (پنل مدیریت).</param>
public sealed record ProfileMenuItem(ProfileSection? Section, string Url, string Title, string Icon, string Group, bool InDropdown, bool Staff = false);

/// <summary>
/// دور ۲۹ – «یک منبع برای منوی حساب کاربری»: منوی کشویی هدر و ستون کناری پروفایل از همین فهرست ساخته می‌شوند تا ترتیب
/// همیشه یکی باشد (خواستهٔ صریح کاربر). ترتیب:
/// <list type="number">
/// <item>پنل مدیریت (فقط کارکنان) – همیشه اول.</item>
/// <item>حساب من: پیشخوان، اطلاعات حساب کاربری، رمز عبور و امنیت (درست زیر اطلاعات حساب)، دستگاه‌های فعال، آدرس‌ها.</item>
/// <item>خریدها: سفارش‌ها، مرجوعی‌ها، کیف پول، باشگاه مشتریان، کارت هدیه.</item>
/// <item>علاقه‌مندی‌ها: علاقه‌مندی‌ها، خبرم کن.</item>
/// <item>ارتباط: اعلان‌ها، تیکت‌های پشتیبانی، دعوت از دوستان.</item>
/// </list>
/// «پیغام‌ها» حذف شد (تیکت‌ها همان کار را می‌کنند). هر بخش اختیاری فقط وقتی تنظیمش روشن است نمایش داده می‌شود.
/// </summary>
public static class ProfileMenu
{
    /// <summary>گروه پنل مدیریت.</summary>
    public const string StaffGroup = "مدیریت";

    /// <summary>
    /// فهرست کامل به ترتیب نهایی. مثال: کارمند با کیف پول روشن ← [پنل مدیریت، پیشخوان، اطلاعات حساب کاربری، رمز عبور و امنیت، …].
    /// </summary>
    public static List<ProfileMenuItem> Build(StoreSettingDto settings, bool isStaff)
    {
        ArgumentNullException.ThrowIfNull(settings);
        var items = new List<ProfileMenuItem>();
        if (isStaff)
            items.Add(new ProfileMenuItem(null, "/admin", "پنل مدیریت", "SquaresFour", StaffGroup, InDropdown: true, Staff: true));

        const string account = "حساب من";
        items.Add(new ProfileMenuItem(ProfileSection.Dashboard, "/profile", "پیشخوان", "Home", account, InDropdown: false));
        items.Add(new ProfileMenuItem(ProfileSection.PersonalInfo, "/profile/info", "اطلاعات حساب کاربری", "UserCircle", account, InDropdown: true));
        items.Add(new ProfileMenuItem(ProfileSection.Password, "/profile/password", "رمز عبور و امنیت", "Shield", account, InDropdown: false));
        items.Add(new ProfileMenuItem(ProfileSection.Sessions, "/profile/sessions", "دستگاه‌های فعال", "Devices", account, InDropdown: false));
        items.Add(new ProfileMenuItem(ProfileSection.Addresses, "/profile/addresses", "آدرس‌ها", "MapPin", account, InDropdown: false));

        const string shopping = "خریدها";
        items.Add(new ProfileMenuItem(ProfileSection.Orders, "/profile/orders", "سفارش‌ها", "Orders", shopping, InDropdown: true));
        if (settings.IsReturnEnabled)
            items.Add(new ProfileMenuItem(ProfileSection.Returns, "/profile/returns", "مرجوعی‌ها", "ArrowUUpLeft", shopping, InDropdown: false));
        if (settings.IsWalletEnabled)
            items.Add(new ProfileMenuItem(ProfileSection.Wallet, "/profile/wallet", "کیف پول", "Wallet", shopping, InDropdown: true));
        if (settings.IsLoyaltyEnabled)
            items.Add(new ProfileMenuItem(ProfileSection.Club, "/profile/club", settings.LoyaltyName, "Crown", shopping, InDropdown: true));
        if (settings.IsGiftCardEnabled)
            items.Add(new ProfileMenuItem(ProfileSection.GiftCards, "/profile/gift-cards", "کارت هدیه", "Gift", shopping, InDropdown: false));

        const string likes = "علاقه‌مندی‌ها";
        items.Add(new ProfileMenuItem(ProfileSection.Favorites, "/profile/favorites", "علاقه‌مندی‌ها", "HeartThin", likes, InDropdown: true));
        if (settings.IsProductAlertEnabled)
            items.Add(new ProfileMenuItem(ProfileSection.Alerts, "/profile/alerts", "خبرم کن", "Target", likes, InDropdown: false));

        const string contact = "ارتباط";
        items.Add(new ProfileMenuItem(ProfileSection.Notifications, "/profile/notifications", "اعلان‌ها", "Bell", contact, InDropdown: true));
        if (settings.IsTicketEnabled)
            items.Add(new ProfileMenuItem(ProfileSection.Tickets, "/profile/tickets", "تیکت‌های پشتیبانی", "Headset", contact, InDropdown: true));
        if (settings.IsReferralEnabled)
            items.Add(new ProfileMenuItem(ProfileSection.Invite, "/profile/invite", "دعوت از دوستان", "UsersThree", contact, InDropdown: false));
        return items;
    }

    /// <summary>ردیف‌های منوی کشویی هدر (زیرمجموعهٔ همان فهرست با همان ترتیب).</summary>
    public static List<ProfileMenuItem> Dropdown(StoreSettingDto settings, bool isStaff) =>
        Build(settings, isStaff).Where(i => i.InDropdown).ToList();
}
