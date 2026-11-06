namespace Fratak_Shop.Core.Utilities;

/// <summary>
/// تبدیل عدد به حروف فارسی (برای فاکتور، اعلان‌ها و نمایش «مبلغ به حروف» در پنل).
/// مثال: <c>PersianNumberWords.ToWords(1250000)</c> → «یک میلیون و دویست و پنجاه هزار».
/// </summary>
/// <remarks>Amount in Persian words for invoices, e.g. 1250000 → "یک میلیون و دویست و پنجاه هزار".</remarks>
public static class PersianNumberWords
{
    private static readonly string[] Ones = ["", "یک", "دو", "سه", "چهار", "پنج", "شش", "هفت", "هشت", "نه"];
    private static readonly string[] Teens = ["ده", "یازده", "دوازده", "سیزده", "چهارده", "پانزده", "شانزده", "هفده", "هجده", "نوزده"];
    private static readonly string[] Tens = ["", "", "بیست", "سی", "چهل", "پنجاه", "شصت", "هفتاد", "هشتاد", "نود"];
    private static readonly string[] Hundreds = ["", "صد", "دویست", "سیصد", "چهارصد", "پانصد", "ششصد", "هفتصد", "هشتصد", "نهصد"];
    private static readonly string[] Scales = ["", "هزار", "میلیون", "میلیارد", "تریلیون", "کوادریلیون", "کوینتیلیون", "سکستیلیون", "سپتیلیون", "اکتیلیون"];

    public static string ToWords(decimal amount)
    {
        // محاسبه با decimal (نه long) تا هر مبلغی، حتی بزرگ‌تر از long، بدون OverflowException و بدون جاافتادن رقم تبدیل شود.
        // Scales تا ۱۰^۲۷ (اکتیلیون) می‌رسد که بیشینهٔ decimal (حدود ۷٫۹ × ۱۰^۲۸) را پوشش می‌دهد.
        var value = decimal.Truncate(Math.Abs(amount));
        if (value == 0)
            return "صفر";

        var groups = new List<string>();
        var scale = 0;
        while (value > 0 && scale < Scales.Length)
        {
            var group = (int)(value % 1000m);
            if (group > 0)
            {
                var words = GroupToWords(group);
                groups.Insert(0, scale == 0 ? words : $"{words} {Scales[scale]}");
            }

            value = decimal.Truncate(value / 1000m);
            scale++;
        }

        var text = string.Join(" و ", groups);
        return amount < 0 ? $"منفی {text}" : text;
    }

    private static string GroupToWords(int number)
    {
        var parts = new List<string>(3);
        var hundreds = number / 100;
        var rest = number % 100;

        if (hundreds > 0)
            parts.Add(Hundreds[hundreds]);

        if (rest >= 10 && rest < 20)
        {
            parts.Add(Teens[rest - 10]);
        }
        else
        {
            if (rest >= 20)
                parts.Add(Tens[rest / 10]);
            if (rest % 10 > 0)
                parts.Add(Ones[rest % 10]);
        }

        return string.Join(" و ", parts);
    }
}
