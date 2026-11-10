using Fratak_Shop.Core.DTOs.Shopping.Command;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Shopping;

/// <summary>نتیجهٔ «سفارش دوباره». Added: تعداد ردیف‌های افزوده‌شده؛ Skipped: نام کالاهایی که ناموجود/غیرفعال بودند.</summary>
public sealed record ReorderResult(int Added, IReadOnlyList<string> Skipped)
{
    /// <summary>
    /// پیام کوتاه برای مشتری. مثال: (2، []) ← «۲ کالا به سبد خرید اضافه شد.»؛
    /// (1، [«شارژر X»]) ← «۱ کالا به سبد خرید اضافه شد. «شارژر X» الان موجود نیست.»؛ (0، …) ← «هیچ‌کدام از کالاهای این سفارش الان موجود نیست.»
    /// </summary>
    public string Message
    {
        get
        {
            if (Added == 0)
                return "هیچ‌کدام از کالاهای این سفارش الان موجود نیست.";
            var text = $"{Fratak_Shop.Core.Services.Seo.Engine.PersianText.Digits(Added)} کالا به سبد خرید اضافه شد.";
            if (Skipped.Count > 0)
                text += " " + string.Join("، ", Skipped.Take(3).Select(n => $"«{n}»")) + (Skipped.Count > 3 ? " و چند کالای دیگر" : "") + " الان موجود نیست.";
            return text;
        }
    }
}

/// <summary>
/// دور ۲۴ (پیشنهاد ۷ – سفارش دوباره): همهٔ کالاهای یک سفارش قبلی با یک کلیک به سبد خرید برمی‌گردند.
/// <para>قواعد: فقط سفارش خود مشتری؛ فقط کالاهای فعال و موجود؛ تعداد هر کالا حداکثر به اندازهٔ موجودی فعلی (اگر کمتر بود همان مقدار)؛
/// قیمت همیشه قیمت «امروز» است (سبد خرید قیمت را از کالا می‌خواند، نه از سفارش قدیمی).</para>
/// <para>محل استفاده: Pages/Profile/OrderDetail.cshtml(.cs) (دکمهٔ «سفارش دوباره») و Pages/Profile/Orders.cshtml.</para>
/// </summary>
public interface IReorderService
{
    /// <summary>مثال: (کاربر، «FS-1001»، سبد) ← ReorderResult(2، [«کالای ناموجود»]). سفارش دیگران یا ناموجود ← NotFound.</summary>
    Task<OperationResult<ReorderResult>> ReorderAsync(Guid userId, string orderNumber, Guid cartId, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="IReorderService"/>
public sealed class ReorderService(FratakShopDbContext context, ICartService carts) : IReorderService
{
    public async Task<OperationResult<ReorderResult>> ReorderAsync(Guid userId, string orderNumber, Guid cartId, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty || string.IsNullOrWhiteSpace(orderNumber))
            return OperationResult<ReorderResult>.NotFound("سفارش پیدا نشد.");

        var number = orderNumber.Trim();
        var lines = await context.OrderItems
            .Where(i => i.Order.OrderNumber == number && i.Order.UserId == userId)
            .Select(i => new { i.ProductId, i.ProductName, i.Quantity })
            .ToListAsync(cancellationToken);
        if (lines.Count == 0)
            return OperationResult<ReorderResult>.NotFound("سفارش پیدا نشد.");

        var ids = lines.Where(l => l.ProductId.HasValue).Select(l => l.ProductId!.Value).Distinct().ToList();
        var stock = await context.Products
            .Where(p => ids.Contains(p.Id) && p.IsActive)
            .Select(p => new { p.Id, p.StockQuantity })
            .ToDictionaryAsync(p => p.Id, p => p.StockQuantity, cancellationToken);
        var inCart = await context.CartItems
            .Where(i => i.CartId == cartId)
            .Select(i => new { i.ProductId, i.Quantity })
            .ToDictionaryAsync(i => i.ProductId, i => i.Quantity, cancellationToken);

        var added = 0;
        var skipped = new List<string>();
        foreach (var group in lines.GroupBy(l => l.ProductId))
        {
            var name = group.First().ProductName;
            if (group.Key is not { } productId || !stock.TryGetValue(productId, out var available))
            {
                skipped.Add(name);
                continue;
            }
            var room = available - inCart.GetValueOrDefault(productId);
            var quantity = Math.Min(group.Sum(l => l.Quantity), room);
            if (quantity <= 0)
            {
                if (inCart.ContainsKey(productId))
                    added++; // قبلاً در سبد هست؛ «ناموجود» حساب نمی‌شود.
                else
                    skipped.Add(name);
                continue;
            }
            var result = await carts.AddItemAsync(new AddCartItemCommand { CartId = cartId, ProductId = productId, Quantity = quantity });
            if (result.IsSuccess())
                added++;
            else
                skipped.Add(name);
        }

        return OperationResult<ReorderResult>.Success(new ReorderResult(added, skipped));
    }
}
