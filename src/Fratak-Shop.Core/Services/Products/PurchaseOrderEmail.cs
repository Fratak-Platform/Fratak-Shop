using System.Globalization;
using System.Text;

namespace Fratak_Shop.Core.Services.Products;

/// <summary>
/// دور ۲۴ (پیشنهاد ۶۹): متن ایمیل «سفارش خرید» برای تأمین‌کننده از روی پیش‌نویس سفارش خرید (پیشنهاد ۶۷).
/// <para>
/// فقط نام کالا، کد (SKU) و تعداد می‌رود؛ قیمت خرید، فروش روزانه و موجودی فروشگاه (اطلاعات تجاری داخلی) هرگز در ایمیل نیست.
/// ارسال فقط با دکمهٔ کارمند (هرگز خودکار) و به نشانی ثبت‌شدهٔ همان تأمین‌کننده.
/// </para>
/// محل استفاده: «محصولات ← انبار ← پیش‌نویس سفارش خرید» (دکمهٔ «ایمیل به تأمین‌کننده»).
/// </summary>
public static class PurchaseOrderEmail
{
    /// <summary>بیشترین ردیف در یک ایمیل (بقیه در ایمیل بعدی/اکسل).</summary>
    public const int MaxLines = 100;

    /// <summary>
    /// موضوع و متن ایمیل. مثال: (گروه «پخش البرز» با ۲ ردیف، «فراتک»، 1405/08/01) ←
    /// موضوع «درخواست خرید فراتک – 1405/08/01» و متن «سلام علی عزیز، … ۱) هدفون X (کد HX-1) – ۱۲ عدد …».
    /// </summary>
    public static (string Subject, string Body) Build(PurchaseDraftGroup group, string shopName, string? contactName, string date)
    {
        ArgumentNullException.ThrowIfNull(group);
        var shop = string.IsNullOrWhiteSpace(shopName) ? "فروشگاه" : shopName.Trim();
        var lines = group.Lines.Where(l => l.Quantity > 0).Take(MaxLines).ToList();
        var body = new StringBuilder();
        body.Append("سلام").Append(string.IsNullOrWhiteSpace(contactName) ? "" : " " + contactName.Trim()).AppendLine(" عزیز،");
        body.AppendLine($"لطفاً موجودی و قیمت روز کالاهای زیر را برای {shop} اعلام و در صورت امکان ارسال کنید:");
        body.AppendLine();
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            body.Append(Digits(i + 1)).Append(") ").Append(line.Name.Trim());
            if (!string.IsNullOrWhiteSpace(line.Sku))
                body.Append(" (کد ").Append(line.Sku.Trim()).Append(')');
            body.Append(" – ").Append(Digits(line.Quantity)).AppendLine(" عدد");
        }
        body.AppendLine();
        body.AppendLine($"جمع: {Digits(lines.Count)} قلم، {Digits(lines.Sum(l => l.Quantity))} عدد.");
        body.AppendLine("با سپاس");
        body.Append(shop);
        return ($"درخواست خرید {shop} – {date}", body.ToString());
    }

    private static string Digits(int value) => new(value.ToString(CultureInfo.InvariantCulture)
        .Select(c => char.IsAsciiDigit(c) ? (char)('۰' + (c - '0')) : c).ToArray());
}
