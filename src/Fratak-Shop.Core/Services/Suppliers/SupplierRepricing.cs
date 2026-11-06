namespace Fratak_Shop.Core.Services.Suppliers;

/// <summary>
/// سیاست «قیمت‌گذاری دوباره» وقتی قیمت خرید از تأمین‌کننده عوض می‌شود (از تنظیمات فروشگاه، تب «تأمین‌کننده»).
/// <para>هدف: افزایش قیمت خرید هیچ‌وقت باعث فروش با ضرر نشود، بدون اینکه کارمند تک‌تک محصولات را دستی عوض کند.</para>
/// <para>کجا استفاده می‌شود: <see cref="SupplierSyncRules.Evaluate"/> (تصمیم خودکار یا پیشنهاد) و
/// <c>SupplierSyncService.ApplyChunkAsync</c> (اعمال روی محصول).</para>
/// </summary>
/// <param name="AutoMode">۰ = همیشه منتظر تأیید کارمند (مثل قبل)، ۱ = افزایش خرید خودکار اعمال شود، ۲ = هر تغییری خودکار اعمال شود.</param>
/// <param name="Mode">روش محاسبهٔ قیمت فروش تازه؛ <see cref="SupplierRepriceMode"/>.</param>
/// <param name="MarkupPercent">درصد سود ثابت روی قیمت خرید (فقط روش ۳). مثال: ۲۵ ← خرید ۱۰۰ = فروش ۱۲۵.</param>
/// <param name="MarkupAmount">سود ثابت تومانی روی قیمت خرید (فقط روش ۴). مثال: ۵۰٬۰۰۰ ← خرید ۱۰۰ هزار = فروش ۱۵۰ هزار.</param>
/// <param name="MinMarginPercent">کف سود در همهٔ روش‌ها (درصد روی قیمت خرید). قیمت فروش هرگز از خرید × (۱ + کف) کمتر نمی‌شود.</param>
/// <param name="Rounding">گرد کردن قیمت فروش به بالا (تومان). مثال: ۱۰۰۰ ← ۵۱۸٬۷۵۰ می‌شود ۵۱۹٬۰۰۰. صفر یا ۱ = بدون گرد کردن.</param>
/// <param name="OnDecrease">وقتی قیمت خرید کم شد، قیمت فروش هم پایین بیاید؟ (پیش‌فرض خیر: سود بیشتر می‌شود.)</param>
/// <param name="MaxJumpPercent">سقف افزایش خودکار؛ اگر خرید بیش از این درصد بالا برود، به‌جای تغییر قیمت، کالا موقتاً ناموجود
/// و پیشنهاد برای تأیید کارمند ساخته می‌شود (احتمال اشتباه منبع). صفر = بدون سقف.</param>
public sealed record SupplierRepricePolicy(
    int AutoMode,
    int Mode,
    int MarkupPercent,
    decimal MarkupAmount,
    int MinMarginPercent,
    decimal Rounding,
    bool OnDecrease,
    int MaxJumpPercent)
{
    /// <summary>پیش‌فرض‌های فروشگاه (همان مقادیر seed): افزایش‌ها خودکار، حفظ درصد سود، کف ۵٪، گرد کردن به هزار، سقف ۵۰٪.</summary>
    public static SupplierRepricePolicy Default { get; } = new(1, (int)SupplierRepriceMode.KeepMarginPercent, 25, 0, 5, 1000, false, 50);

    /// <summary>
    /// آیا این تغییر قیمت خرید بدون تأیید کارمند اعمال شود؟
    /// <para>قیمت خرید قبلی نامعلوم باشد ← خیر (نمی‌دانیم افزایش است یا کاهش؛ کارمند تصمیم بگیرد).</para>
    /// </summary>
    public bool IsAutomatic(decimal? oldCost, decimal newCost)
    {
        if (oldCost is not > 0 || newCost <= 0 || newCost == oldCost)
            return false;
        return AutoMode switch
        {
            1 => newCost > oldCost,
            2 => true,
            _ => false,
        };
    }

    /// <summary>
    /// آیا افزایش از سقف مجاز بیشتر است؟ مثال: سقف ۵۰٪، خرید ۴۰۰ → ۷۰۰ هزار (۷۵٪) ← بله.
    /// </summary>
    public bool ExceedsJump(decimal? oldCost, decimal newCost)
        => MaxJumpPercent > 0 && oldCost is > 0 && newCost > oldCost.Value * (1 + MaxJumpPercent / 100m);
}

/// <summary>روش محاسبهٔ قیمت فروش تازه بعد از تغییر قیمت خرید (عدد هر گزینه در تنظیمات ذخیره می‌شود).</summary>
public enum SupplierRepriceMode
{
    /// <summary>فقط قیمت خرید به‌روز شود؛ قیمت فروش دست نمی‌خورد (کف سود همچنان از ضرر جلوگیری می‌کند).</summary>
    PurchaseOnly = 0,

    /// <summary>حفظ درصد سود: فروش تازه = خرید تازه × (فروش قبلی ÷ خرید قبلی). مثال: ۴۰۰/۵۰۰ ← خرید ۴۱۵ = فروش ۵۱۸٬۷۵۰.</summary>
    KeepMarginPercent = 1,

    /// <summary>حفظ مبلغ سود: فروش تازه = خرید تازه + (فروش قبلی − خرید قبلی). مثال: ۴۰۰/۵۰۰ ← خرید ۴۱۵ = فروش ۵۱۵.</summary>
    KeepProfitAmount = 2,

    /// <summary>درصد سود ثابت روی خرید. مثال: ۲۵٪ ← خرید ۴۱۵ = فروش ۵۱۸٬۷۵۰.</summary>
    FixedMarkupPercent = 3,

    /// <summary>مبلغ سود ثابت روی خرید. مثال: ۱۰۰ هزار ← خرید ۴۱۵ = فروش ۵۱۵.</summary>
    FixedMarkupAmount = 4,

    /// <summary>بیشترِ «حفظ درصد» و «حفظ مبلغ» (محتاطانه‌ترین برای سود).</summary>
    HigherOfBoth = 5,
}

/// <summary>نتیجهٔ محاسبهٔ قیمت فروش تازه.</summary>
/// <param name="Price">قیمت فروش تازه (تومان).</param>
/// <param name="DiscountPrice">قیمت تخفیف‌خوردهٔ تازه؛ null یعنی تخفیف ندارد/حذف شد.</param>
/// <param name="Changed">آیا قیمت فروش یا تخفیف عوض شد.</param>
/// <param name="Note">توضیح فارسی کوتاه برای گزارش تغییرات. مثال: «فروش ۵۰۰٬۰۰۰ ← ۵۱۹٬۰۰۰ (حفظ درصد سود)».</param>
public sealed record SupplierRepriceResult(decimal Price, decimal? DiscountPrice, bool Changed, string Note);

/// <summary>
/// محاسبهٔ خالص قیمت فروش تازه (بدون دیتابیس، کاملاً تست‌پذیر).
/// <para>قانون‌های ایمنی که در همهٔ روش‌ها برقرارند:</para>
/// <list type="number">
/// <item>با افزایش قیمت خرید، قیمت فروش هیچ‌وقت پایین نمی‌آید.</item>
/// <item>قیمت فروش (و تخفیف) هیچ‌وقت از «خرید × (۱ + کف سود)» کمتر نمی‌شود.</item>
/// <item>تخفیفی که به قیمت اصلی برسد یا از آن بیشتر شود حذف می‌شود.</item>
/// </list>
/// </summary>
public static class SupplierRepricing
{
    /// <summary>
    /// قیمت فروش تازه برای یک محصول.
    /// <para>مثال: خرید ۴۰۰→۴۱۵ هزار، فروش ۵۰۰ هزار، روش «حفظ درصد»، گرد کردن ۱۰۰۰ ← فروش ۵۱۹٬۰۰۰.</para>
    /// </summary>
    /// <param name="oldCost">قیمت خرید قبلی (ممکن است ثبت نشده باشد).</param>
    /// <param name="newCost">قیمت خرید تازه از منبع.</param>
    /// <param name="price">قیمت فروش فعلی.</param>
    /// <param name="discountPrice">قیمت تخفیف‌خوردهٔ فعلی.</param>
    /// <param name="policy">سیاست تنظیمات.</param>
    public static SupplierRepriceResult Calculate(decimal? oldCost, decimal newCost, decimal price, decimal? discountPrice, SupplierRepricePolicy policy)
    {
        if (newCost <= 0 || price <= 0)
            return new(price, discountPrice, false, "قیمت نامعتبر؛ قیمت فروش تغییری نکرد.");

        var mode = Enum.IsDefined(typeof(SupplierRepriceMode), policy.Mode) ? (SupplierRepriceMode)policy.Mode : SupplierRepriceMode.KeepMarginPercent;
        var known = oldCost is > 0;
        var increase = !known || newCost > oldCost!.Value;
        var decrease = known && newCost < oldCost!.Value;

        decimal newPrice;
        decimal? newDiscount;
        if ((decrease && !policy.OnDecrease) || mode == SupplierRepriceMode.PurchaseOnly)
        {
            // قیمت فروش ثابت می‌ماند؛ فقط کف سود پایین‌تر بررسی می‌شود.
            newPrice = price;
            newDiscount = discountPrice;
        }
        else
        {
            newPrice = Transform(price, oldCost, newCost, mode, policy);
            // تخفیف: در روش‌های «حفظ» با همان فرمول؛ در روش‌های «سود ثابت» به همان نسبتی که قیمت اصلی عوض شد.
            newDiscount = discountPrice is { } d
                ? mode is SupplierRepriceMode.FixedMarkupPercent or SupplierRepriceMode.FixedMarkupAmount
                    ? d * newPrice / price
                    : Transform(d, oldCost, newCost, mode, policy)
                : null;
        }

        // قانون ۱: افزایش خرید، فروش را پایین نمی‌آورد (مثلاً سود ثابت ۲۵٪ وقتی سود فعلی ۵۰٪ است).
        if (increase)
        {
            newPrice = Math.Max(newPrice, price);
            if (newDiscount is { } nd && discountPrice is { } od)
                newDiscount = Math.Max(nd, od);
        }

        // قانون ۲: کف سود.
        var floor = newCost * (1 + Math.Max(0, policy.MinMarginPercent) / 100m);
        newPrice = RoundUp(Math.Max(newPrice, floor), policy.Rounding);
        if (newDiscount is { } disc)
        {
            disc = RoundUp(Math.Max(disc, floor), policy.Rounding);
            // قانون ۳: تخفیف باید از قیمت اصلی کمتر باشد.
            newDiscount = disc < newPrice ? disc : null;
        }

        var changed = newPrice != price || newDiscount != discountPrice;
        var note = changed
            ? $"فروش {price:N0} ← {newPrice:N0}" + (newDiscount != discountPrice ? $"، تخفیف {Fmt(discountPrice)} ← {Fmt(newDiscount)}" : "") + $" ({ModeTitle(mode)})"
            : "قیمت فروش تغییری نکرد.";
        return new(newPrice, newDiscount, changed, note);
    }

    /// <summary>
    /// گرد کردن به بالا به نزدیک‌ترین مضرب. مثال: (۵۱۸٬۷۵۰، ۱۰۰۰) ← ۵۱۹٬۰۰۰؛ (۵۱۹٬۰۰۰، ۱۰۰۰) ← ۵۱۹٬۰۰۰.
    /// به بالا گرد می‌شود تا گرد کردن هیچ‌وقت سود را زیر کف نبرد.
    /// </summary>
    public static decimal RoundUp(decimal value, decimal step)
    {
        if (step <= 1)
            return Math.Ceiling(value);
        return Math.Ceiling(value / step) * step;
    }

    /// <summary>عنوان فارسی روش (برای گزارش و تنظیمات).</summary>
    public static string ModeTitle(SupplierRepriceMode mode) => mode switch
    {
        SupplierRepriceMode.PurchaseOnly => "فقط قیمت خرید",
        SupplierRepriceMode.KeepMarginPercent => "حفظ درصد سود",
        SupplierRepriceMode.KeepProfitAmount => "حفظ مبلغ سود",
        SupplierRepriceMode.FixedMarkupPercent => "درصد سود ثابت",
        SupplierRepriceMode.FixedMarkupAmount => "مبلغ سود ثابت",
        SupplierRepriceMode.HigherOfBoth => "بیشترِ درصد و مبلغ سود",
        _ => mode.ToString(),
    };

    /// <summary>فرمول هر روش برای یک مقدار فروش (قیمت اصلی یا تخفیف).</summary>
    private static decimal Transform(decimal sale, decimal? oldCost, decimal newCost, SupplierRepriceMode mode, SupplierRepricePolicy policy)
    {
        // بدون قیمت خرید قبلی، روش‌های «حفظ» معنا ندارند؛ قیمت فعلی می‌ماند و فقط کف سود اعمال می‌شود.
        var hasOld = oldCost is > 0;
        return mode switch
        {
            SupplierRepriceMode.KeepMarginPercent => hasOld ? sale * newCost / oldCost!.Value : sale,
            SupplierRepriceMode.KeepProfitAmount => hasOld ? sale + (newCost - oldCost!.Value) : sale,
            SupplierRepriceMode.HigherOfBoth => hasOld ? Math.Max(sale * newCost / oldCost!.Value, sale + (newCost - oldCost!.Value)) : sale,
            SupplierRepriceMode.FixedMarkupPercent => newCost * (1 + Math.Max(0, policy.MarkupPercent) / 100m),
            SupplierRepriceMode.FixedMarkupAmount => newCost + Math.Max(0, policy.MarkupAmount),
            _ => sale,
        };
    }

    private static string Fmt(decimal? value) => value is { } v ? v.ToString("N0") : "بدون تخفیف";
}
