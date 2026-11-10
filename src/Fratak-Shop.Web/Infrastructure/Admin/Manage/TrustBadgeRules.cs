using Fratak_Shop.Core.Services.Contents;
using Fratak_Shop.Data.Entities.Contents;

namespace Fratak_Shop.Web.Infrastructure.Admin.Manage;

/// <summary>
/// دور ۲۴ (پیشنهاد ۴۶): قاعدهٔ ذخیرهٔ «نماد اعتماد» در جدول عمومی پنل.
/// <list type="bullet">
/// <item>کد نماد چسبانده شده ← تصویر و لینک امن از آن برداشته می‌شود (<see cref="TrustBadgeEmbed"/>)؛ کد نامعتبر ← خطا و چیزی ذخیره نمی‌شود.</item>
/// <item>بدون کد ← بارگذاری تصویر الزامی است (مثل قبل).</item>
/// </list>
/// مثال: چسباندن کد اینماد ← ImageUrl = https://trustseal.enamad.ir/logo.aspx?… و LinkUrl = https://trustseal.enamad.ir/?…
/// </summary>
public static class TrustBadgeRules
{
    public static string? Apply(TrustBadge badge)
    {
        if (!string.IsNullOrWhiteSpace(badge.EmbedCode))
        {
            var parsed = TrustBadgeEmbed.Parse(badge.EmbedCode);
            if (!parsed.IsValid)
                return parsed.Error ?? "کد نماد معتبر نیست.";
            badge.EmbedCode = badge.EmbedCode.Trim();
            badge.ImageUrl = parsed.ImageUrl!;
            if (parsed.LinkUrl is not null)
                badge.LinkUrl = parsed.LinkUrl;
            return null;
        }
        badge.EmbedCode = null;
        return string.IsNullOrWhiteSpace(badge.ImageUrl) ? "تصویر نماد را بارگذاری کنید یا کد نماد را بچسبانید." : null;
    }
}
