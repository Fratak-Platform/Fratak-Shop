using System.ComponentModel.DataAnnotations;
using Fratak_Shop.Data.Utilities;

namespace Fratak_Shop.Data.Entities.Products;

/// <summary>
/// کش ماندگار قیمت رقبا (مرحلهٔ سیزدهم).
/// <para>چرا: کش حافظه با هر ری‌استارت پاک می‌شود و ترب/دیجی‌کالا درخواست تکراری را محدود می‌کنند. با این جدول هر عبارت
/// حداکثر هر چند ساعت یک بار واقعاً جست‌وجو می‌شود و اگر منابع موقتاً محدود بودند، آخرین قیمت‌های معتبر (تا ۳ روز) با ذکر زمان نمایش داده می‌شود.</para>
/// <para>کجا استفاده می‌شود: CompetitorPriceService (Core/Services/Pricing/CompetitorPrices.cs).</para>
/// مثال: QueryKey = «هدفون هایلو t15»، OffersJson = [{"Shop":"دیجی‌کالا","Price":305000,…}]، FetchedAt = دیروز ساعت ۱۰.
/// </summary>
public class CompetitorPriceCache : BaseEntity
{
    /// <summary>عبارت جست‌وجوی نرمال‌شده (یکتا). مثال: «هدفون هایلو t15».</summary>
    [MaxLength(200)]
    public string QueryKey { get; set; } = "";

    /// <summary>فهرست پیشنهادهای فروشگاه‌ها به JSON (CompetitorOffer[]).</summary>
    public string OffersJson { get; set; } = "[]";

    /// <summary>عنوان کالایی که در ترب انتخاب شد.</summary>
    [MaxLength(200)]
    public string? MatchedTitle { get; set; }

    public int OfferCount { get; set; }

    /// <summary>زمان جست‌وجوی واقعی.</summary>
    public DateTimeOffset FetchedAt { get; set; }
}

/// <summary>
/// آخرین قیمت‌های رقبای یک محصول (مرحلهٔ پانزدهم) — برای اینکه تیم برای <b>هر محصول</b> ببیند رقبا چند می‌فروشند.
/// <para>چه زمانی پر می‌شود: دکمهٔ «پیشنهاد قیمت فروش» در فرم محصول، دکمهٔ «بررسی» در صفحهٔ «رصد قیمت رقبا»
/// و صف قیمت‌گذاری بازار محصولات درون‌ریزی‌شده. هر محصول فقط یک ردیف دارد (آخرین بررسی).</para>
/// <para>کجا نمایش داده می‌شود: کارت «قیمت رقبا» در صفحهٔ ویرایش محصول و صفحهٔ «محصولات › رصد قیمت رقبا».</para>
/// مثال: محصول «هدفون هایلو T15»، ارزان‌ترین فروشندهٔ ترب ۲۸۵ هزار از ۱۲ فروشنده، ارزان‌ترین کل ۲۷۹ هزار (باسلام)، بررسی: امروز ۱۰:۳۰.
/// </summary>
public class ProductCompetitorSnapshot : BaseEntity
{
    /// <summary>شناسهٔ محصول (یکتا؛ بدون کلید خارجی تا حذف محصول به این جدول گره نخورد).</summary>
    public Guid ProductId { get; set; }

    /// <summary>عبارتی که جست‌وجو شد. مثال: «هدفون هایلو T15».</summary>
    [MaxLength(200)]
    public string Query { get; set; } = "";

    /// <summary>عنوان کالایی که در ترب انتخاب شد (برای اطمینان از اینکه همان کالاست).</summary>
    [MaxLength(200)]
    public string? MatchedTitle { get; set; }

    /// <summary>همهٔ پیشنهادهای خام فروشگاه‌ها به JSON (CompetitorOffer[])؛ داوری هنگام نمایش دوباره انجام می‌شود.</summary>
    public string OffersJson { get; set; } = "[]";

    /// <summary>تعداد کل پیشنهادها.</summary>
    public int OfferCount { get; set; }

    /// <summary>تعداد پیشنهادهایی که سیستم معتبر دانست.</summary>
    public int AcceptedCount { get; set; }

    /// <summary>ارزان‌ترین قیمت معتبر بین همهٔ منابع (تومان).</summary>
    public decimal? LowestPrice { get; set; }

    /// <summary>میانهٔ قیمت‌های معتبر (تومان).</summary>
    public decimal? MedianPrice { get; set; }

    /// <summary>ارزان‌ترین فروشندهٔ معتبر ترب (تومان) — قیمت ما باید از این کمتر باشد.</summary>
    public decimal? TorobLowestPrice { get; set; }

    /// <summary>تعداد فروشندگان معتبر همین کالا در ترب.</summary>
    public int TorobSellerCount { get; set; }

    /// <summary>قیمت فروش ما هنگام بررسی (تومان؛ برای مقایسه با قیمت فعلی).</summary>
    public decimal? OurPrice { get; set; }

    /// <summary>خلاصهٔ مشکل منابع (مثلاً «ترب: کپچا»)، حداکثر ۱۰۰۰ نویسه.</summary>
    [MaxLength(1000)]
    public string? Problems { get; set; }

    /// <summary>زمان بررسی.</summary>
    public DateTimeOffset CheckedAt { get; set; }
}

/// <summary>وضعیت یک بررسی قیمت بازار.</summary>
public enum MarketPriceCheckStatus
{
    /// <summary>در صف (یا منتظر تلاش دوباره بعد از محدودیت منابع).</summary>
    Pending = 0,

    /// <summary>قیمت بازار پیدا شد و قیمت فروش محصول طبق پیشنهاد تنظیم شد.</summary>
    Applied = 1,

    /// <summary>قیمت بازاری پیدا نشد؛ قیمت اولیه (سود هدف) ماند.</summary>
    Kept = 2,

    /// <summary>محصول حذف شد یا قیمتش را کسی دستی عوض کرد؛ به قیمت دست نزدیم.</summary>
    Skipped = 3,
}

/// <summary>
/// صف «قیمت‌گذاری بر اساس بازار» محصولات درون‌ریزی‌شده (مرحلهٔ سیزدهم).
/// <para>روند: در «درون‌ریزی از سایت» با گزینهٔ «بر اساس قیمت بازار»، محصول با قیمت اولیهٔ سود هدف ساخته و یک ردیف اینجا ثبت می‌شود؛
/// کار پس‌زمینهٔ MarketPricingWorker هر چند ثانیه یک ردیف را برمی‌دارد، قیمت همان کالا را در ترب/دیجی‌کالا/باسلام می‌گیرد و
/// با PriceAdvisor (همان دکمهٔ «پیشنهاد قیمت فروش») قیمت فروش را تنظیم می‌کند — فقط اگر قیمت محصول هنوز همان قیمت اولیه باشد.</para>
/// مثال: «هدفون هایلو T15»، قیمت خرید ۲۰۰ هزار، قیمت اولیه ۲۶۰ هزار → ارزان‌ترین رقیب ۲۸۵ هزار → قیمت فروش ۲۷۹ هزار (Applied).
/// </summary>
public class MarketPriceCheck : BaseEntity
{
    public Guid ProductId { get; set; }

    /// <summary>عبارت جست‌وجو (نام محصول). مثال: «هندزفری بلوتوثی هایلو T15».</summary>
    [MaxLength(200)]
    public string Query { get; set; } = "";

    public MarketPriceCheckStatus Status { get; set; }

    /// <summary>تعداد تلاش‌ها (تلاش دوباره فقط وقتی منابع محدود/در دسترس نبودند).</summary>
    public int Attempts { get; set; }

    /// <summary>زودترین زمان تلاش بعدی.</summary>
    public DateTimeOffset NextAttemptAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }

    /// <summary>قیمت فروش اولیه هنگام ساخت محصول؛ اگر قیمت فعلی با این فرق کند یعنی کسی دستی عوض کرده و دست نمی‌زنیم.</summary>
    public decimal InitialPrice { get; set; }

    /// <summary>قیمت خرید (قیمت سایت مبدأ).</summary>
    public decimal? PurchasePrice { get; set; }

    /// <summary>ارزان‌ترین قیمت معتبر رقبا (پس از حذف قیمت‌های پرت).</summary>
    public decimal? LowestPrice { get; set; }

    /// <summary>میانهٔ قیمت رقبا.</summary>
    public decimal? MedianPrice { get; set; }

    public int OfferCount { get; set; }

    /// <summary>قیمت پیشنهادی (حتی اگر اعمال نشد، برای مقایسه).</summary>
    public decimal? SuggestedPrice { get; set; }

    [MaxLength(200)]
    public string? MatchedTitle { get; set; }

    /// <summary>توضیح فارسی نتیجه. مثال: «ارزان‌ترین رقیب ۲۸۵ هزار؛ ۲٪ ارزان‌تر …».</summary>
    [MaxLength(1000)]
    public string? Note { get; set; }
}
