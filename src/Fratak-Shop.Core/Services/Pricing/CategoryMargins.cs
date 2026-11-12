using Fratak_Shop.Data.Contexts;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Pricing;

/// <summary>یک دسته برای محاسبهٔ کف سود: والد و کف سود خودش (خالی = ارث از والد/سراسری).</summary>
public sealed record CategoryMarginNode(Guid? ParentId, int? MinMarginPercent);

/// <summary>
/// کف سود هر دسته (پیشنهاد ۶۹، دور ۲۳).
/// <para>قاعده: کف مؤثر = بیشترینِ (کف سراسری تنظیمات، کف خود دسته، کف همهٔ دسته‌های بالادستی). یعنی کف دسته فقط می‌تواند
/// سود را <b>بالاتر</b> ببرد، هرگز پایین‌تر از کف سراسری. مقدار نامعتبر به بازهٔ ۰ تا <see cref="Max"/> محدود می‌شود.</para>
/// <para>مثال: سراسری ۵، «موبایل» ۸، «گوشی سامسونگ» (زیرِ موبایل) خالی ← ۸؛ «لوازم جانبی» ۳ ← ۵ (کف سراسری).</para>
/// <para>محل استفاده: پیشنهاد قیمت (MarketPricing، ویرایش کالا، درون‌ریزی) و قیمت‌گذاری دوبارهٔ تأمین‌کننده (SupplierSyncService).</para>
/// </summary>
public static class CategoryMargins
{
    /// <summary>بیشترین کف سود قابل قبول (درصد). بیشتر از این تقریباً همیشه اشتباه تایپی است.</summary>
    public const int Max = 300;

    /// <summary>بیشترین عمق پیمایش درخت (محافظ در برابر حلقه یا دادهٔ خراب).</summary>
    private const int MaxDepth = 12;

    /// <summary>
    /// کف مؤثر برای یک دسته. دستهٔ ناشناخته/خالی ← کف سراسری. حلقه در درخت (A والد B و B والد A) بی‌خطر است.
    /// مثال: Effective(5, گوشی‌سامسونگ، نقشه) ← ۸ وقتی والدش ۸ است.
    /// </summary>
    public static int Effective(int global, Guid? categoryId, IReadOnlyDictionary<Guid, CategoryMarginNode> nodes)
    {
        var result = Math.Clamp(global, 0, Max);
        var seen = new HashSet<Guid>();
        var current = categoryId;
        for (var depth = 0; current is { } id && depth < MaxDepth && seen.Add(id); depth++)
        {
            if (!nodes.TryGetValue(id, out var node))
                break;
            if (node.MinMarginPercent is { } own)
                result = Math.Max(result, Math.Clamp(own, 0, Max));
            current = node.ParentId;
        }
        return result;
    }

    /// <summary>
    /// نقشهٔ سبک همهٔ دسته‌ها (فقط شناسه، والد و کف سود؛ چند صد ردیف کوچک). یک بار برای هر دسته‌کار خوانده شود، نه برای هر کالا.
    /// </summary>
    public static async Task<Dictionary<Guid, CategoryMarginNode>> NodesAsync(FratakShopDbContext context, CancellationToken cancellationToken = default) =>
        await context.Categories.AsNoTracking()
            .Select(c => new { c.Id, c.ParentId, c.MinMarginPercent })
            .ToDictionaryAsync(c => c.Id, c => new CategoryMarginNode(c.ParentId, c.MinMarginPercent), cancellationToken);

    /// <summary>
    /// میان‌بر برای یک کالا: اگر هیچ دسته‌ای کف خودش را ندارد (حالت معمول)، بدون خواندن کل درخت همان کف سراسری برمی‌گردد.
    /// </summary>
    public static async Task<int> ForCategoryAsync(FratakShopDbContext context, int global, Guid? categoryId, CancellationToken cancellationToken = default)
    {
        if (categoryId is null || !await context.Categories.AnyAsync(c => c.MinMarginPercent != null, cancellationToken))
            return Math.Clamp(global, 0, Max);
        return Effective(global, categoryId, await NodesAsync(context, cancellationToken));
    }
}
