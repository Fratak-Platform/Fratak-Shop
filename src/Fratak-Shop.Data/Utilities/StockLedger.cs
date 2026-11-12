using Fratak_Shop.Data.Auditing;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Products;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Data.Utilities;

/// <summary>
/// «دلیل» تغییر موجودی برای کدی که بعدش SaveChanges صدا زده می‌شود.
/// <para>
/// چرا لازم است؟ DbContext خودش می‌فهمد موجودی عوض شده، اما نمی‌داند «چرا». هر سرویسی که موجودی را
/// به دلیل خاصی عوض می‌کند (تأمین‌کننده، ورود از اکسل، ویرایش اقلام سفارش) دور کارش این را می‌گذارد.
/// </para>
/// مثال:
/// <code>
/// using (StockContext.Begin(StockMovementReason.Supplier, "بررسی تأمین‌کننده"))
///     await context.SaveChangesAsync();
/// </code>
/// اگر چیزی تنظیم نشده باشد: وقتی کارمند در پنل است «دستی»، وگرنه «سیستم» ثبت می‌شود.
/// </summary>
public static class StockContext
{
    /// <summary>مقدار فعلی (AsyncLocal: فقط برای همین درخواست/کار جاری).</summary>
    public sealed record Info(StockMovementReason Reason, string? Note, Guid? OrderId = null, string? Reference = null);

    private static readonly AsyncLocal<Info?> CurrentInfo = new();

    public static Info? Current => CurrentInfo.Value;

    /// <summary>شروع یک بازه با دلیل مشخص؛ با Dispose مقدار قبلی برمی‌گردد.</summary>
    public static IDisposable Begin(StockMovementReason reason, string? note = null, Guid? orderId = null, string? reference = null)
    {
        var previous = CurrentInfo.Value;
        CurrentInfo.Value = new Info(reason, note, orderId, reference);
        return new Scope(previous);
    }

    private sealed class Scope(Info? previous) : IDisposable
    {
        public void Dispose() => CurrentInfo.Value = previous;
    }
}

/// <summary>
/// نوشتن مستقیم ردیف دفتر موجودی برای جاهایی که موجودی را با <c>ExecuteUpdate</c> (بدون SaveChanges) عوض می‌کنند.
/// <para>
/// باید «بعد از» همان ExecuteUpdate و داخل همان تراکنش صدا زده شود تا ستون «مانده» دقیقاً موجودی تازه باشد.
/// استفاده: OrderService (ثبت و لغو سفارش)، ReturnService (دریافت مرجوعی)، OrderMessageService (ناموجودی).
/// </para>
/// </summary>
public static class StockLedger
{
    /// <summary>
    /// یک ردیف گردش برای محصول می‌نویسد؛ مانده از خود جدول محصولات خوانده می‌شود.
    /// مثال: بعد از کم کردن ۲ عدد برای سفارش → <c>RecordAsync(context, id, -2, StockMovementReason.Sale, order.Id, order.OrderNumber)</c>.
    /// </summary>
    public static async Task RecordAsync(FratakShopDbContext context, Guid productId, int delta, StockMovementReason reason,
        Guid? orderId = null, string? reference = null, string? note = null, CancellationToken cancellationToken = default)
    {
        // دیتابیس حافظه‌ای تست‌ها SQL خام ندارد؛ آنجا دفتر لازم نیست.
        if (delta == 0 || !context.Database.IsRelational())
            return;

        var actor = AuditContext.Current;
        var userId = actor?.UserId;
        var userName = Cut(actor?.UserName, 200);
        reference = Cut(reference, 100);
        note = Cut(note, 500);
        var reasonValue = (int)reason;

        // INSERT … SELECT: مانده را همان لحظه از ردیف محصول می‌خواند (داخل تراکنش جاری).
        await context.Database.ExecuteSqlInterpolatedAsync($"""
            INSERT INTO [StockMovements] ([Id], [ProductId], [Delta], [BalanceAfter], [Reason], [OrderId], [Reference], [UserId], [UserName], [Note], [CreatedAt], [UpdatedAt], [IsDeleted])
            SELECT NEWID(), p.[Id], {delta}, p.[StockQuantity], {reasonValue}, {orderId}, {reference}, {userId}, {userName}, {note}, SYSDATETIMEOFFSET(), SYSDATETIMEOFFSET(), 0
            FROM [Products] AS p WHERE p.[Id] = {productId}
            """, cancellationToken);
    }

    private static string? Cut(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Length <= max ? value : value[..max];
}
