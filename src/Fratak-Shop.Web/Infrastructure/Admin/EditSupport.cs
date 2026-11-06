using System.Globalization;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Web.Infrastructure.Realtime;
using Fratak_Shop.Core.Services.Notifications;
using Microsoft.AspNetCore.SignalR;

namespace Fratak_Shop.Web.Infrastructure.Admin;

/// <summary>
/// «نسخهٔ رکورد» برای جلوگیری از بازنویسی کار دیگران (Optimistic concurrency ساده).
/// <para>
/// نسخه = زمان آخرین تغییر ثبت‌شدهٔ رکورد از پنل (<see cref="IAuditHistoryService.StampAsync"/>). فرم ویرایش هنگام نمایش آن را
/// در فیلد مخفی <c>__stamp</c> می‌گذارد. هنگام ذخیره اگر نسخهٔ فعلی با آن فرق داشته باشد یعنی در این فاصله کارمند دیگری ذخیره کرده است؛
/// ذخیره انجام نمی‌شود و هشدار داده می‌شود (مگر تیک «با وجود این، نسخهٔ من ذخیره شود» = <c>__force</c> را بزند).
/// </para>
/// استفاده: <c>Manage/Edit</c>، <c>Products/Edit</c>، <c>Blog/Edit</c> + partial <c>_EditStamp</c>.
/// </summary>
public static class EditStamp
{
    public const string FieldName = "__stamp";
    public const string ForceName = "__force";

    /// <summary>نسخه به‌صورت متن (برای فیلد مخفی و ویژگی data-autosave-stamp؛ عدد ۱۸ رقمی در JavaScript دقیق نمی‌ماند).</summary>
    public static string Format(long stamp) => stamp.ToString(CultureInfo.InvariantCulture);

    /// <summary>
    /// آیا فرم ارسالی بر اساس نسخهٔ قدیمی رکورد بوده؟ فرم بدون <c>__stamp</c> (درخواست‌های قدیمی/API) بررسی نمی‌شود.
    /// </summary>
    /// <example>صفحه ۱۰:۰۰ با نسخهٔ X باز شد؛ ۱۰:۰۵ همکار ذخیره کرد (نسخه Y)؛ ۱۰:۰۸ ذخیرهٔ این فرم ← X ≠ Y ← تعارض.</example>
    public static bool IsStale(IFormCollection form, long currentStamp)
    {
        if (form[ForceName].Contains("true"))
            return false;
        var posted = form[FieldName].FirstOrDefault();
        if (string.IsNullOrEmpty(posted) || !long.TryParse(posted, NumberStyles.None, CultureInfo.InvariantCulture, out var stamp))
            return false;
        return stamp != currentStamp;
    }

    /// <summary>متن هشدار تعارض با نام آخرین ویرایشگر (از گزارش فعالیت).</summary>
    public static async Task<string> ConflictMessageAsync(IAuditHistoryService history, string entityType, Guid id)
    {
        var last = (await history.ListAsync(entityType, id, 1)).FirstOrDefault();
        var who = last?.UserName is { Length: > 0 } name ? $"«{name}»" : "کارمند دیگری";
        var when = last is null ? "" : $" ({Helpers.Format.Relative(last.At)})";
        return $"{who}{when} همین مورد را بعد از باز شدن این صفحه ذخیره کرده است، پس تغییرات شما ذخیره نشد تا کار او پاک نشود. "
               + "تغییرات شما در فرم باقی است و پیش‌نویس خودکار هم دارد: صفحه را در تب دیگری باز کنید و مقایسه کنید، "
               + "یا تیک «با وجود این، نسخهٔ من ذخیره شود» را بزنید.";
    }
}

/// <summary>
/// اعلام «این رکورد همین حالا ذخیره شد» به بقیهٔ کسانی که همان صفحهٔ ویرایش را باز کرده‌اند (رویداد editors با saved).
/// <para>
/// مرورگرهای دیگر (admin-edit.js) نوار هشدار «نسخهٔ شما قدیمی است» نشان می‌دهند. کلید رکورد همان کلید <c>data-edit-key</c> فرم است،
/// مثلاً <c>product:3f2a…</c>. خطای ارسال نادیده گرفته می‌شود (ذخیره نباید به‌خاطر اعلان شکست بخورد).
/// </para>
/// </summary>
public sealed class EditPresenceNotifier(IHubContext<LiveHub> hub, ILogger<EditPresenceNotifier> logger)
{
    public async Task SavedAsync(string key, string byName, Guid byUserId)
    {
        try
        {
            await hub.Clients.Group(LiveHub.EditGroup(key)).SendAsync(RealtimeMethods.Editors, new { key, saved = byName, savedBy = byUserId });
        }
        catch (Exception exception)
        {
            logger.LogDebug(exception, "Edit saved broadcast for {Key} failed.", key);
        }
    }
}

/// <summary>داده‌های partialهای ابزار ویرایش: تاریخچه و نسخهٔ رکورد.</summary>
/// <param name="EntityType">نام کلاس موجودیت برای تاریخچه، مثلاً <c>Product</c>.</param>
/// <param name="RevertUrl">آدرس فرم بازگردانی (هندلر Revert همان صفحه).</param>
/// <param name="Labels">برچسب فارسی فیلدها (نام ویژگی ← برچسب)؛ فیلد بدون برچسب با نام خودش نشان داده می‌شود.</param>
public sealed record EditHistoryModel(string EntityType, Guid Id, IReadOnlyList<AuditHistoryItem> Items, string RevertUrl, IReadOnlyDictionary<string, string> Labels, bool CanRevert)
{
    public string LabelOf(string field) =>
        Labels.TryGetValue(field, out var label) ? label
        : field.StartsWith("Seo.", StringComparison.Ordinal) ? "سئو: " + (SeoLabels.TryGetValue(field[4..], out var seo) ? seo : field[4..])
        : field;

    private static readonly Dictionary<string, string> SeoLabels = new(StringComparer.Ordinal)
    {
        ["Title"] = "عنوان", ["MetaDescription"] = "توضیحات متا", ["FocusKeyword"] = "کلمهٔ کلیدی", ["MetaKeywords"] = "کلمات کلیدی",
        ["CanonicalUrl"] = "آدرس کانونیکال", ["OgTitle"] = "عنوان اشتراک", ["OgDescription"] = "توضیح اشتراک", ["NoIndex"] = "noindex",
        ["NoFollow"] = "nofollow", ["BreadcrumbTitle"] = "عنوان مسیر",
    };

    /// <summary>کوتاه‌شدهٔ متن برای نمایش (HTML حذف می‌شود).</summary>
    public static string Short(string? value)
    {
        if (value is null)
            return "∅";
        var text = Core.Utilities.XssSecurity.ToPlainText(value);
        if (text.Length == 0 && value.Length > 0)
            text = value;
        return text.Length <= 160 ? text : text[..159] + "…";
    }
}

/// <summary>داده‌های partial <c>_EditStamp</c> (فیلد مخفی نسخه + هشدار تعارض).</summary>
public sealed record EditStampModel(string Stamp, string? ConflictMessage);
