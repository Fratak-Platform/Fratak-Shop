using System.Text.RegularExpressions;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Admin;

// ═══════════════════════════════════════════════════════════════════════════
//  مدیریت ارتباط با مشتری (CRM) در پنل: برچسب، یادداشت داخلی و بخش‌بندی RFM.
//  محل استفاده: صفحهٔ «جزئیات مشتری» (/admin/customers/{id}) و فیلتر برچسب در فهرست مشتریان.
//  مشتری هیچ‌کدام از این اطلاعات را نمی‌بیند.
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>یک برچسب مشتری برای نمایش.</summary>
public sealed record CustomerTagDto(Guid Id, string Tag, string? CreatedByName, DateTimeOffset CreatedAt);

/// <summary>یک یادداشت داخلی مشتری برای نمایش.</summary>
public sealed record CustomerNoteDto(Guid Id, Guid? AuthorUserId, string? AuthorName, string Content, bool IsPinned, DateTimeOffset CreatedAt);

/// <summary>برچسب پرکاربرد با تعداد مشتری‌هایش (برای پیشنهاد هنگام تایپ و فیلتر فهرست).</summary>
public sealed record CustomerTagCount(string Tag, int Count);

/// <summary>بخش‌های RFM؛ عدد ثابت است چون ممکن است در گزارش‌ها ذخیره شود.</summary>
public enum CustomerSegment
{
    /// <summary>هنوز خرید موفقی ندارد.</summary>
    NoPurchase = 0,

    /// <summary>اخیراً و زیاد خرید کرده (طلایی).</summary>
    Champion = 1,

    /// <summary>مرتب خرید می‌کند.</summary>
    Loyal = 2,

    /// <summary>تازه اولین خریدش را کرده.</summary>
    New = 3,

    /// <summary>اخیراً خرید کرده ولی هنوز کم.</summary>
    Promising = 4,

    /// <summary>قبلاً زیاد می‌خرید، مدتی است نیامده.</summary>
    AtRisk = 5,

    /// <summary>کم خرید و مدتی غایب.</summary>
    NeedsAttention = 6,

    /// <summary>مشتری پرخرید قدیمی که خیلی وقت است نیامده؛ نباید از دست برود.</summary>
    CantLose = 7,

    /// <summary>خیلی وقت است خرید نکرده.</summary>
    Lost = 8,
}

/// <summary>
/// ورودی محاسبهٔ RFM یک مشتری (فقط سفارش‌های پرداخت‌شده و لغو/مرجوع‌نشده حساب می‌شوند).
/// </summary>
/// <param name="LastOrderAt">زمان آخرین خرید موفق (null یعنی هیچ خریدی).</param>
/// <param name="OrderCount">تعداد خریدهای موفق.</param>
/// <param name="Monetary">جمع خالص خرید (بعد از کسر مبالغ برگشتی) به تومان.</param>
/// <param name="AverageCustomerSpend">میانگین خرید هر مشتری خریدار فروشگاه (مبنای نمرهٔ مبلغ).</param>
public sealed record CustomerRfmInput(DateTimeOffset? LastOrderAt, int OrderCount, decimal Monetary, decimal AverageCustomerSpend);

/// <summary>نتیجهٔ RFM: سه نمرهٔ ۱ تا ۵، بخش، عنوان فارسی و پیشنهاد اقدام.</summary>
public sealed record CustomerRfm(int Recency, int Frequency, int Monetary, CustomerSegment Segment, int? DaysSinceLastOrder)
{
    public string Title => CustomerRfmCalculator.Title(Segment);
    public string Advice => CustomerRfmCalculator.Advice(Segment);

    /// <summary>کد سه‌رقمی رایج RFM؛ مثلاً «۵۴۳» یعنی تازگی ۵، دفعات ۴، مبلغ ۳.</summary>
    public string Code => $"{Recency}{Frequency}{Monetary}";
}

/// <summary>
/// محاسبهٔ خالص RFM (Recency تازگی، Frequency دفعات، Monetary مبلغ) بدون دیتابیس؛ مستقیم تست واحد می‌شود.
/// <para>
/// چرا آستانهٔ ثابت به‌جای صدک؟ فروشگاه کوچک/تازه داده کافی برای صدک ندارد و نمره‌ها بی‌معنی می‌شوند؛
/// آستانه‌های روزی/دفعه‌ای برای فروشگاه آنلاین خرده‌فروشی ایران تنظیم شده و مبلغ نسبت به میانگین خود فروشگاه سنجیده می‌شود.
/// </para>
/// مثال: آخرین خرید ۱۰ روز پیش، ۶ خرید، جمع ۳ برابر میانگین → (۵، ۴، ۵) = «مشتری طلایی».
/// </summary>
public static class CustomerRfmCalculator
{
    public static CustomerRfm Calculate(CustomerRfmInput input, DateTimeOffset now)
    {
        if (input.OrderCount <= 0 || input.LastOrderAt is null)
            return new CustomerRfm(0, 0, 0, CustomerSegment.NoPurchase, null);

        var days = Math.Max(0, (int)(now - input.LastOrderAt.Value).TotalDays);
        var r = days switch { <= 30 => 5, <= 60 => 4, <= 120 => 3, <= 240 => 2, _ => 1 };
        var f = input.OrderCount switch { >= 10 => 5, >= 5 => 4, >= 3 => 3, 2 => 2, _ => 1 };

        int m;
        if (input.AverageCustomerSpend <= 0)
            m = 3; // مبنایی برای مقایسه نیست → متوسط
        else
        {
            var ratio = input.Monetary / input.AverageCustomerSpend;
            m = ratio switch { >= 3m => 5, >= 1.5m => 4, >= 0.75m => 3, >= 0.35m => 2, _ => 1 };
        }

        return new CustomerRfm(r, f, m, Segment(r, f), days);
    }

    /// <summary>
    /// نقشهٔ بخش‌ها بر اساس تازگی و دفعات (روش رایج نقشهٔ RFM). ترتیب شرط‌ها مهم است.
    /// </summary>
    public static CustomerSegment Segment(int recency, int frequency) => (recency, frequency) switch
    {
        (>= 4, >= 4) => CustomerSegment.Champion,
        (>= 3, >= 3) => CustomerSegment.Loyal,
        (>= 4, 1) => CustomerSegment.New,
        (>= 3, _) => CustomerSegment.Promising,
        (2, >= 3) => CustomerSegment.AtRisk,
        (2, _) => CustomerSegment.NeedsAttention,
        (_, >= 3) => CustomerSegment.CantLose,
        _ => CustomerSegment.Lost,
    };

    public static string Title(CustomerSegment segment) => segment switch
    {
        CustomerSegment.Champion => "مشتری طلایی",
        CustomerSegment.Loyal => "وفادار",
        CustomerSegment.New => "تازه‌وارد",
        CustomerSegment.Promising => "امیدوارکننده",
        CustomerSegment.AtRisk => "در خطر ریزش",
        CustomerSegment.NeedsAttention => "نیازمند توجه",
        CustomerSegment.CantLose => "نباید از دست برود",
        CustomerSegment.Lost => "از دست رفته",
        _ => "بدون خرید",
    };

    /// <summary>پیشنهاد اقدام برای کارمند فروش (فقط راهنمایی است، کاری خودکار انجام نمی‌شود).</summary>
    public static string Advice(CustomerSegment segment) => segment switch
    {
        CustomerSegment.Champion => "از او قدردانی کنید؛ دسترسی زودهنگام به تخفیف‌ها یا امتیاز باشگاه هدیه مناسب است.",
        CustomerSegment.Loyal => "محصولات مکمل خریدهای قبلی را پیشنهاد دهید و نظرش را دربارهٔ خرید بپرسید.",
        CustomerSegment.New => "تجربهٔ اولین خرید را پیگیری کنید؛ یک کد تخفیف خرید دوم احتمال بازگشت را بالا می‌برد.",
        CustomerSegment.Promising => "با پیشنهاد محصولات مرتبط او را به خرید دوباره تشویق کنید.",
        CustomerSegment.AtRisk => "مدتی است خرید نکرده؛ پیام شخصی یا کد تخفیف «دلمان تنگ شده» بفرستید.",
        CustomerSegment.NeedsAttention => "یادآوری محصولات جدید یا پیشنهاد محدود زمانی می‌تواند او را برگرداند.",
        CustomerSegment.CantLose => "مشتری پرخرید قدیمی است؛ تماس تلفنی و پرسیدن علت غیبت ارزش دارد.",
        CustomerSegment.Lost => "فقط در کمپین‌های بزرگ (مثل حراج) به او پیام دهید.",
        _ => "هنوز خرید موفقی ندارد؛ یادآوری سبد خرید یا کد تخفیف اولین خرید مناسب است.",
    };
}

/// <summary>
/// سرویس CRM پنل. همهٔ متدها فرض می‌کنند مجوز در لایهٔ صفحه بررسی شده است
/// (دیدن: <c>CustomersView</c>، تغییر: <c>CustomersManage</c>).
/// </summary>
public interface ICustomerCrmService
{
    Task<List<CustomerTagDto>> GetTagsAsync(Guid userId);

    /// <summary>
    /// افزودن برچسب. متن یکسان‌سازی می‌شود (فاصلهٔ اضافی، «#» ابتدا، ی/ک عربی) تا «VIP» و «  vip » دو برچسب جدا نشوند.
    /// </summary>
    Task<OperationResult> AddTagAsync(Guid userId, string? tag, string? actorName);

    Task<OperationResult> RemoveTagAsync(Guid userId, Guid tagId);

    /// <summary>پرکاربردترین برچسب‌ها (برای پیشنهاد و فیلتر فهرست مشتریان)؛ ۱۰ دقیقه کش می‌شود.</summary>
    Task<List<CustomerTagCount>> PopularTagsAsync(int take = 30);

    /// <summary>یادداشت‌ها: سنجاق‌شده‌ها اول، بعد جدیدترین.</summary>
    Task<List<CustomerNoteDto>> GetNotesAsync(Guid userId);

    Task<OperationResult> AddNoteAsync(Guid userId, string? content, bool pinned, Guid? authorId, string? authorName);

    Task<OperationResult> TogglePinAsync(Guid userId, Guid noteId);

    /// <summary>حذف یادداشت؛ فقط نویسنده یا کسی که <paramref name="canDeleteOthers"/> دارد (مالک).</summary>
    Task<OperationResult> DeleteNoteAsync(Guid userId, Guid noteId, Guid? actorId, bool canDeleteOthers);

    /// <summary>محاسبهٔ RFM مشتری با دو کوئری سبک (آمار مشتری + میانگین کش‌شدهٔ فروشگاه).</summary>
    Task<CustomerRfm> GetRfmAsync(Guid userId);
}

public partial class CustomerCrmService(FratakShopDbContext context, IMemoryCache cache) : ICustomerCrmService
{
    private const int MaxTagsPerCustomer = 20;
    private const int MaxTagLength = 40;
    private const int MaxNoteLength = 2000;
    private const string PopularKey = "crm:popular-tags";
    private const string AverageKey = "crm:average-spend";

    public Task<List<CustomerTagDto>> GetTagsAsync(Guid userId) =>
        context.CustomerTags.AsNoTracking().Where(t => t.UserId == userId).OrderBy(t => t.CreatedAt)
            .Select(t => new CustomerTagDto(t.Id, t.Tag, t.CreatedByName, t.CreatedAt)).ToListAsync();

    public async Task<OperationResult> AddTagAsync(Guid userId, string? tag, string? actorName)
    {
        var text = NormalizeTag(tag);
        if (text.Length == 0)
            return OperationResult.Error("متن برچسب را بنویسید.");
        if (text.Length > MaxTagLength)
            return OperationResult.Error($"برچسب حداکثر {MaxTagLength} نویسه است.");
        if (!await context.Users.AnyAsync(u => u.Id == userId))
            return OperationResult.NotFound("مشتری یافت نشد.");

        var existing = await context.CustomerTags.Where(t => t.UserId == userId).Select(t => t.Tag).ToListAsync();
        if (existing.Any(t => string.Equals(t, text, StringComparison.OrdinalIgnoreCase)))
            return OperationResult.Error($"برچسب «{text}» قبلاً زده شده است.");
        if (existing.Count >= MaxTagsPerCustomer)
            return OperationResult.Error($"هر مشتری حداکثر {MaxTagsPerCustomer} برچسب می‌تواند داشته باشد.");

        // اگر همین برچسب قبلاً با حروف دیگر در فروشگاه استفاده شده، همان شکل نوشتاری را به‌کار می‌بریم تا فیلتر یکدست بماند.
        var canonical = await context.CustomerTags.Where(t => t.Tag == text).Select(t => t.Tag).FirstOrDefaultAsync() ?? text;

        context.CustomerTags.Add(new CustomerTag { UserId = userId, Tag = canonical, CreatedByName = Cut(actorName, 150) });
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // دو کارمند هم‌زمان یک برچسب زده‌اند؛ ایندکس یکتا جلوی تکرار را گرفت.
            return OperationResult.Error($"برچسب «{text}» قبلاً زده شده است.");
        }

        cache.Remove(PopularKey);
        return OperationResult.Success($"برچسب «{canonical}» اضافه شد.");
    }

    public async Task<OperationResult> RemoveTagAsync(Guid userId, Guid tagId)
    {
        var row = await context.CustomerTags.AsTracking().FirstOrDefaultAsync(t => t.Id == tagId && t.UserId == userId);
        if (row is null)
            return OperationResult.NotFound("برچسب یافت نشد.");

        context.CustomerTags.Remove(row); // حذف نرم (IsDeleted) توسط DbContext
        await context.SaveChangesAsync();
        cache.Remove(PopularKey);
        return OperationResult.Success($"برچسب «{row.Tag}» برداشته شد.");
    }

    public async Task<List<CustomerTagCount>> PopularTagsAsync(int take = 30)
    {
        var all = await cache.GetOrCreateAsync(PopularKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10);
            // مرتب‌سازی باید پیش از ساختن رکورد باشد؛ EF مرتب‌سازی روی سازندهٔ رکورد را به SQL ترجمه نمی‌کند.
            var rows = await context.CustomerTags.AsNoTracking().GroupBy(t => t.Tag)
                .Select(g => new { Tag = g.Key, Count = g.Count() })
                .OrderByDescending(t => t.Count).ThenBy(t => t.Tag).Take(100).ToListAsync();
            return rows.Select(r => new CustomerTagCount(r.Tag, r.Count)).ToList();
        }) ?? [];
        return all.Take(Math.Clamp(take, 1, 100)).ToList();
    }

    public Task<List<CustomerNoteDto>> GetNotesAsync(Guid userId) =>
        context.CustomerNotes.AsNoTracking().Where(n => n.UserId == userId)
            .OrderByDescending(n => n.IsPinned).ThenByDescending(n => n.CreatedAt).Take(100)
            .Select(n => new CustomerNoteDto(n.Id, n.AuthorUserId, n.AuthorName, n.Content, n.IsPinned, n.CreatedAt)).ToListAsync();

    public async Task<OperationResult> AddNoteAsync(Guid userId, string? content, bool pinned, Guid? authorId, string? authorName)
    {
        var text = (content ?? "").Trim();
        if (text.Length == 0)
            return OperationResult.Error("متن یادداشت را بنویسید.");
        if (text.Length > MaxNoteLength)
            return OperationResult.Error($"یادداشت حداکثر {MaxNoteLength:N0} نویسه است.");
        if (!await context.Users.AnyAsync(u => u.Id == userId))
            return OperationResult.NotFound("مشتری یافت نشد.");

        context.CustomerNotes.Add(new CustomerNote
        {
            UserId = userId, Content = text, IsPinned = pinned, AuthorUserId = authorId, AuthorName = Cut(authorName, 150),
        });
        await context.SaveChangesAsync();
        return OperationResult.Success("یادداشت ثبت شد.");
    }

    public async Task<OperationResult> TogglePinAsync(Guid userId, Guid noteId)
    {
        var note = await context.CustomerNotes.AsTracking().FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId);
        if (note is null)
            return OperationResult.NotFound("یادداشت یافت نشد.");

        note.IsPinned = !note.IsPinned;
        await context.SaveChangesAsync();
        return OperationResult.Success(note.IsPinned ? "یادداشت بالای صفحه سنجاق شد." : "سنجاق یادداشت برداشته شد.");
    }

    public async Task<OperationResult> DeleteNoteAsync(Guid userId, Guid noteId, Guid? actorId, bool canDeleteOthers)
    {
        var note = await context.CustomerNotes.AsTracking().FirstOrDefaultAsync(n => n.Id == noteId && n.UserId == userId);
        if (note is null)
            return OperationResult.NotFound("یادداشت یافت نشد.");
        if (!canDeleteOthers && (actorId is null || note.AuthorUserId != actorId))
            return OperationResult.Error("فقط نویسندهٔ یادداشت یا مالک فروشگاه می‌تواند آن را حذف کند.");

        context.CustomerNotes.Remove(note);
        await context.SaveChangesAsync();
        return OperationResult.Success("یادداشت حذف شد.");
    }

    public async Task<CustomerRfm> GetRfmAsync(Guid userId)
    {
        // سه کوئری تجمیعی سبک روی ایندکس UserId (به‌جای GroupBy ثابت که ترجمه‌اش در همهٔ نسخه‌ها تضمینی نیست).
        var mine = Successful(context.Orders.AsNoTracking().Where(o => o.UserId == userId));
        var count = await mine.CountAsync();
        if (count == 0)
            return CustomerRfmCalculator.Calculate(new CustomerRfmInput(null, 0, 0, 0), DateTimeOffset.UtcNow);
        var last = await mine.MaxAsync(o => o.CreatedAt);
        var spent = await mine.SumAsync(o => (decimal?)(o.PayableAmount - o.RefundedAmount)) ?? 0;

        var average = await cache.GetOrCreateAsync(AverageKey, async entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromHours(1);
            var successful = Successful(context.Orders.AsNoTracking()).Where(o => o.UserId != null);
            var buyers = await successful.Select(o => o.UserId).Distinct().CountAsync();
            if (buyers == 0)
                return 0m;
            var total = await successful.SumAsync(o => (decimal?)(o.PayableAmount - o.RefundedAmount)) ?? 0;
            return total / buyers;
        });

        return CustomerRfmCalculator.Calculate(new CustomerRfmInput(last, count, spent, average), DateTimeOffset.UtcNow);
    }

    /// <summary>خرید موفق = پرداخت‌شده و لغو/مرجوع‌نشده (همان تعریف «مجموع خرید» در فهرست مشتریان).</summary>
    private static IQueryable<Order> Successful(IQueryable<Order> orders) =>
        orders.Where(o => o.PaymentStatus == PaymentStatus.Paid && o.Status != OrderStatus.Cancelled && o.Status != OrderStatus.Returned);

    /// <summary>
    /// یکسان‌سازی متن برچسب: حذف «#» ابتدا، ی/ک عربی به فارسی، فاصله‌های پشت‌سرهم به یکی.
    /// مثال: «  #مشتری   عمده » → «مشتری عمده».
    /// </summary>
    public static string NormalizeTag(string? value)
    {
        var text = (value ?? "").Replace('ي', 'ی').Replace('ك', 'ک').Trim().TrimStart('#').Trim();
        return Spaces().Replace(text, " ");
    }

    private static string? Cut(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length > max ? value[..max] : value.Trim();

    [GeneratedRegex(@"\s+")]
    private static partial Regex Spaces();
}
