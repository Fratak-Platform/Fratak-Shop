using System.Text;
using System.Text.RegularExpressions;
using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Data.Entities.Admin;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Team;

// ═══════════════════════════════════════════════════════════════════════════
//  نام بردن از همکار در یادداشت‌ها (@منشن) – مرحلهٔ هفتم، فاز ۱۰
//  شکل نام: «@» + نام و نام خانوادگی که فاصله‌هایش «_» شده. مثال: «@سارا_محمدی لطفاً با مشتری تماس بگیر».
//  هنگام تایپ «@» در کادر یادداشت، فهرست همکاران پیشنهاد می‌شود (admin-team.js + /admin/team/staff).
//  بعد از ذخیرهٔ یادداشت (مشتری یا سفارش) به هر همکارِ نام‌برده یک اعلان پنل/پوش با لینک همان صفحه می‌رسد.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>یک همکار قابل منشن. Handle بدون «@» است. مثال: Name=«سارا محمدی»، Handle=«سارا_محمدی».</summary>
public sealed record StaffMention(Guid UserId, string Name, string Handle);

/// <summary>توابع خالص (بدون دیتابیس) برای ساخت و پیدا کردن منشن‌ها؛ در آزمون‌های واحد هم استفاده می‌شوند.</summary>
public static partial class Mentions
{
    /// <summary>
    /// یک منشن در متن: «@» که قبلش حرف یا رقم نباشد (تا ایمیل‌ها مثل ali@site.com منشن حساب نشوند) و بعدش ۲ تا ۶۰ حرف/رقم/«_»/نیم‌فاصله.
    /// </summary>
    [GeneratedRegex(@"(?<![\p{L}\p{Nd}_])@([\p{L}\p{Mn}\p{Nd}_\u200c]{2,60})", RegexOptions.CultureInvariant)]
    public static partial Regex Pattern();

    /// <summary>
    /// شکل یکسان برای مقایسه: ي/ك عربی ← ی/ک فارسی، فاصله و نیم‌فاصله ← «_»، «_»های پشت‌سرهم ← یکی، حروف لاتین کوچک.
    /// مثال: «سارا  محمدي» ← «سارا_محمدی».
    /// </summary>
    public static string Normalize(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return string.Empty;

        var builder = new StringBuilder(value.Length);
        foreach (var raw in value.Trim().TrimStart('@'))
        {
            var c = raw switch
            {
                'ي' or 'ى' => 'ی',
                'ك' => 'ک',
                ' ' or '\u200c' or '\u00a0' or '-' => '_',
                _ => char.ToLowerInvariant(raw),
            };
            if (c == '_' && (builder.Length == 0 || builder[^1] == '_'))
                continue;
            if (c == '_' || char.IsLetterOrDigit(c) || char.GetUnicodeCategory(c) == System.Globalization.UnicodeCategory.NonSpacingMark)
                builder.Append(c);
        }

        return builder.ToString().TrimEnd('_');
    }

    /// <summary>Handle نمایشی برای یک نام (بدون تغییر حروف، فقط فاصله‌ها «_»). مثال: «علی رضا ایزدی» ← «علی_رضا_ایزدی».</summary>
    public static string HandleFor(string name) =>
        string.Join('_', name.Split([' ', '\u00a0'], StringSplitOptions.RemoveEmptyEntries));

    /// <summary>
    /// همکارانی که در متن نام برده شده‌اند (هر نفر یک بار). منشن ناشناخته نادیده گرفته می‌شود.
    /// مثال: متن «@سارا_محمدی و @رضا لطفاً بررسی کنید» با فهرست [سارا محمدی، رضا کریمی] ← فقط سارا (چون «رضا» کامل نیست).
    /// </summary>
    public static List<StaffMention> Find(string? text, IReadOnlyCollection<StaffMention> directory)
    {
        if (string.IsNullOrEmpty(text) || directory.Count == 0 || !text.Contains('@'))
            return [];

        var byHandle = new Dictionary<string, StaffMention>();
        foreach (var member in directory)
            byHandle.TryAdd(Normalize(member.Handle), member);

        var found = new List<StaffMention>();
        foreach (Match match in Pattern().Matches(text))
        {
            var token = Normalize(match.Groups[1].Value);
            // نام ممکن است با علامت نگارشی تمام شود (مثلاً «@سارا_محمدی،»)؛ regex آن را نمی‌گیرد. «_» آخر هم حذف شده.
            if (byHandle.TryGetValue(token, out var member) && found.All(f => f.UserId != member.UserId))
                found.Add(member);
        }

        return found;
    }
}

public interface IMentionService
{
    /// <summary>فهرست همکاران فعال برای پیشنهاد هنگام تایپ «@» و تشخیص منشن (۲ دقیقه کش).</summary>
    Task<List<StaffMention>> DirectoryAsync();

    /// <summary>
    /// به همکارانِ نام‌برده در متن اعلان می‌دهد (به‌جز خود نویسنده). تعداد گیرنده‌ها برمی‌گردد. هرگز خطا پرتاب نمی‌کند.
    /// <paramref name="where"/>: توضیح محل برای متن اعلان. مثال: «یادداشت سفارش ۱۴۰۵۰۱۲».
    /// <paramref name="url"/>: آدرس صفحه در پنل. مثال: «/admin/orders/…#notes».
    /// </summary>
    Task<int> NotifyAsync(string? text, string where, string url, AdminAccess actor);
}

public sealed class MentionService(IAdminAccessService access, IStaffNotificationService notifications, IMemoryCache cache) : IMentionService
{
    private const string CacheKey = "team:mention-directory";

    public async Task<List<StaffMention>> DirectoryAsync() =>
        (await cache.GetOrCreateAsync(CacheKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(2);
            var staff = await access.GetStaffAsync();
            return staff.Where(s => !s.IsBlocked)
                .Select(s => new StaffMention(s.UserId, s.FullName, Mentions.HandleFor(s.FullName)))
                .ToList();
        }))!;

    public async Task<int> NotifyAsync(string? text, string where, string url, AdminAccess actor)
    {
        if (string.IsNullOrWhiteSpace(text) || !text.Contains('@'))
            return 0;

        var mentioned = Mentions.Find(text, await DirectoryAsync()).Where(m => m.UserId != actor.UserId).ToList();
        var snippet = text.Length > 300 ? text[..300] + "…" : text;
        foreach (var member in mentioned)
            await notifications.NotifyUserAsync(member.UserId, StaffNotificationKind.Mention, $"{actor.DisplayName} از شما نام برد", $"{where}: {snippet}", url);
        return mentioned.Count;
    }
}
