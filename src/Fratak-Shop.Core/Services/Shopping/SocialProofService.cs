using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Shopping;

/// <summary>آمار واقعی یک کالا برای صفحهٔ کالا. Text خالی یعنی چیزی نشان داده نشود.</summary>
/// <param name="Buyers">تعداد سفارش‌های پرداخت‌شده/قطعی در ۷ روز گذشته.</param>
/// <param name="InCarts">تعداد سبدهای فعالی که در ۲۴ ساعت گذشته این کالا را دارند.</param>
/// <param name="Text">جملهٔ نمایشی (یا null).</param>
public sealed record SocialProofDto(int Buyers, int InCarts, string? Text);

/// <summary>
/// دور ۲۴ (پیشنهاد ۱۵ – اثبات اجتماعی واقعی): «۱۲ نفر در ۷ روز گذشته این کالا را خریده‌اند».
/// <para>اصل مهم: هیچ عدد ساختگی، شمارش معکوس یا «فقط ۲ عدد مانده»ی دروغ نیست. فقط وقتی عدد به حد معنادار برسد چیزی نشان داده می‌شود
/// (حداقل <see cref="MinBuyers"/> خرید یا <see cref="MinCarts"/> سبد)؛ وگرنه جای خالی می‌ماند (بدون آزار و ترفند).</para>
/// <para>سبکی: نتیجهٔ هر کالا ۱۰ دقیقه در حافظه می‌ماند؛ دو پرس‌وجوی شمارشی ساده روی ایندکس‌های موجود.</para>
/// <para>محل استفاده: GET /api/products/{id}/proof (Web/Infrastructure/Commerce/CommerceEndpoints.cs) و assets/js/commerce.js.</para>
/// </summary>
public interface ISocialProofService
{
    /// <summary>حداقل خرید ۷ روزه برای نمایش.</summary>
    const int MinBuyers = 3;

    /// <summary>حداقل سبد ۲۴ ساعته برای نمایش.</summary>
    const int MinCarts = 5;

    Task<SocialProofDto> GetAsync(Guid productId, CancellationToken cancellationToken = default);
}

/// <inheritdoc cref="ISocialProofService"/>
public sealed class SocialProofService(FratakShopDbContext context, IMemoryCache cache) : ISocialProofService
{
    public async Task<SocialProofDto> GetAsync(Guid productId, CancellationToken cancellationToken = default)
    {
        var key = $"proof:{productId:N}";
        if (cache.TryGetValue(key, out SocialProofDto? cached) && cached is not null)
            return cached;

        var now = DateTimeOffset.UtcNow;
        var week = now.AddDays(-7);
        var day = now.AddDays(-1);

        var buyers = await context.OrderItems
            .Where(i => i.ProductId == productId && i.Order.CreatedAt >= week
                        && i.Order.Status != OrderStatus.Cancelled && i.Order.Status != OrderStatus.Returned
                        && (i.Order.PaymentStatus == PaymentStatus.Paid || i.Order.Status >= OrderStatus.Confirmed))
            .Select(i => i.OrderId).Distinct().CountAsync(cancellationToken);

        var carts = await context.CartItems
            .Where(i => i.ProductId == productId && (i.Cart.LastActivityAt ?? i.Cart.UpdatedAt) >= day)
            .Select(i => i.CartId).Distinct().CountAsync(cancellationToken);

        var result = new SocialProofDto(buyers, carts, Describe(buyers, carts));
        cache.Set(key, result, new MemoryCacheEntryOptions { AbsoluteExpirationRelativeToNow = TimeSpan.FromMinutes(10), Size = 1 });
        return result;
    }

    /// <summary>
    /// جملهٔ نمایشی. مثال: (12، 0) ← «۱۲ نفر در ۷ روز گذشته این کالا را خریده‌اند»؛ (0، 6) ← «این کالا در سبد خرید ۶ نفر است»؛
    /// (2، 3) ← null (هنوز معنادار نیست).
    /// </summary>
    public static string? Describe(int buyers, int carts)
    {
        if (buyers >= ISocialProofService.MinBuyers)
            return $"{Fa(buyers)} نفر در ۷ روز گذشته این کالا را خریده‌اند";
        if (carts >= ISocialProofService.MinCarts)
            return $"این کالا در سبد خرید {Fa(carts)} نفر است";
        return null;
    }

    private static string Fa(int value) => Fratak_Shop.Core.Services.Seo.Engine.PersianText.Digits(value);
}
