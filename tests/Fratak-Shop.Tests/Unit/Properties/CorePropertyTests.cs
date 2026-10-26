using System.Net;
using System.Numerics;
using Fratak_Shop.Core.Services.Ops;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Products;
using FsCheck.Xunit;

namespace Fratak_Shop.Tests.Unit.Properties;

/// <summary>
/// آزمون‌های مبتنی بر ویژگی (Property-based) با FsCheck: به‌جای چند مثال دستی، برای هر قاعده صدها ورودی تصادفی
/// (از جمله رشته‌های عجیب یونیکد، اعداد مرزی و منفی) ساخته می‌شود و «ویژگی» باید برای همه برقرار بماند.
/// اگر FsCheck ورودی خراب‌کننده‌ای پیدا کند، آن را تا ساده‌ترین شکل کوچک می‌کند و در گزارش CI نشان می‌دهد.
/// </summary>
[Trait("Category", "Unit")]
[Trait("Area", "Properties")]
public class CorePropertyTests
{
    private static int Index(int seed, int count) => (int)((uint)seed % (uint)count);

    // ── نامک (Slug) ─────────────────────────────────────────────────────

    [Property(MaxTest = 500)]
    public bool Slugs_are_url_safe_bounded_and_stable(string? input, byte max)
    {
        var maxLength = 1 + max % 200;
        var slug = input.ToSlug(maxLength);

        return slug.Length <= maxLength
            && slug.All(c => char.IsLetterOrDigit(c) || c == '-')
            && !slug.StartsWith('-') && !slug.EndsWith('-')
            && !slug.Contains("--", StringComparison.Ordinal)
            && slug.ToSlug(maxLength) == slug;
    }

    // ── متن جست‌وجو ────────────────────────────────────────────────────

    [Property(MaxTest = 500)]
    public bool Search_text_is_normalized_bounded_and_idempotent(string? input)
    {
        var text = SearchText.Normalize(input);

        return text.Length <= SearchText.MaxLength
            && !text.Contains("  ", StringComparison.Ordinal)
            && !text.StartsWith(' ') && !text.EndsWith(' ')
            && !text.Any(c => c is 'ي' or 'ك' or '\u200C' or (>= '۰' and <= '۹') || char.IsControl(c))
            && SearchText.Terms(input).Count <= 5;
    }

    [Property(MaxTest = 300)]
    public bool Persian_and_arabic_digits_always_become_latin(int number)
    {
        var latin = Math.Abs((long)number).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var persian = new string(latin.Select(c => (char)('۰' + (c - '0'))).ToArray());
        var arabic = new string(latin.Select(c => (char)('٠' + (c - '0'))).ToArray());

        return SearchText.NormalizeDigits(persian) == latin && SearchText.NormalizeDigits(arabic) == latin
            && persian.ToSlug() == latin;
    }

    // ── فاصلهٔ ویرایشی (پیشنهاد «منظورتان این بود؟») ───────────────────

    [Property(MaxTest = 300)]
    public bool Edit_distance_is_a_symmetric_bounded_measure(string? a, string? b, char extra)
    {
        a ??= "";
        b ??= "";
        var distance = Fuzzy.Distance(a, b);

        return Fuzzy.Distance(a, a) == 0
            && distance == Fuzzy.Distance(b, a)
            && distance <= Math.Max(a.Length, b.Length)
            && distance >= Math.Abs(a.Length - b.Length)
            && Fuzzy.Distance(a, a + extra) == 1;
    }

    // ── اعداد به حروف (فاکتور) ─────────────────────────────────────────

    [Property(MaxTest = 300)]
    public bool Amounts_in_words_are_clean_persian_text(long amount)
    {
        var words = PersianNumberWords.ToWords(amount);

        return words.Length > 0
            && !words.Any(char.IsAsciiDigit)
            && !words.Contains("  ", StringComparison.Ordinal)
            && words == words.Trim()
            && (amount >= 0 || words == "منفی " + PersianNumberWords.ToWords(-(decimal)amount))
            && (amount <= 0 || PersianNumberWords.ToWords(-(decimal)amount) == "منفی " + words)
            && (amount != 0 || words == "صفر");
    }

    [Theory]
    [InlineData("1000000000000000000", "یک کوینتیلیون")]
    [InlineData("1250000", "یک میلیون و دویست و پنجاه هزار")]
    [InlineData("-9223372036854775808", "منفی نه کوینتیلیون و دویست و بیست و سه کوادریلیون و سیصد و هفتاد و دو تریلیون و سی و شش میلیارد و هشتصد و پنجاه و چهار میلیون و هفتصد و هفتاد و پنج هزار و هشتصد و هشت")]
    public void Very_large_amounts_are_written_completely_without_overflow(string amount, string expected) =>
        Assert.Equal(expected, PersianNumberWords.ToWords(decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture)));

    // ── شماره کارت و شبا ───────────────────────────────────────────────

    [Property(MaxTest = 300)]
    public bool Luhn_valid_cards_pass_and_any_single_digit_typo_fails(int a, int b, int c, int typo)
    {
        var body = $"{Index(a, 100000):D5}{Index(b, 100000):D5}{Index(c, 100000):D5}";
        var check = LuhnCheckDigit(body);
        var card = body + check;
        if (card.Distinct().Count() == 1)
            return !BankAccountValidator.IsValidCardNumber(card);

        var position = Index(typo, 16);
        var changed = (char)('0' + ((card[position] - '0' + 1 + Index(typo, 9)) % 10));
        var wrong = card[..position] + changed + card[(position + 1)..];

        return BankAccountValidator.IsValidCardNumber(card) && !BankAccountValidator.IsValidCardNumber(wrong);
    }

    private static char LuhnCheckDigit(string body)
    {
        var sum = 0;
        for (var i = 0; i < body.Length; i++)
        {
            var digit = body[body.Length - 1 - i] - '0';
            if (i % 2 == 0)
            {
                digit *= 2;
                if (digit > 9)
                    digit -= 9;
            }
            sum += digit;
        }
        return (char)('0' + (10 - sum % 10) % 10);
    }

    [Property(MaxTest = 300)]
    public bool Iranian_ibans_with_a_correct_checksum_validate_in_any_written_form(int a, int b, int c, int d)
    {
        var bban = $"{Index(a, 100000):D5}{Index(b, 100000):D5}{Index(c, 100000):D5}{Index(d, 10000000):D7}";
        var checksum = 98 - (int)(BigInteger.Parse(bban + "182700") % 97);
        var iban = $"IR{checksum:D2}{bban}";
        var persian = new string(iban.Select(ch => char.IsAsciiDigit(ch) ? (char)('۰' + (ch - '0')) : ch).ToArray());
        var spaced = string.Join(' ', Enumerable.Range(0, (iban.Length + 3) / 4).Select(i => iban.Substring(i * 4, Math.Min(4, iban.Length - i * 4))));
        var tampered = iban[..10] + (char)('0' + ((iban[10] - '0' + 1) % 10)) + iban[11..];

        return IbanValidator.IsValid(iban)
            && IbanValidator.Normalize(persian) == iban
            && IbanValidator.Normalize(spaced.ToLowerInvariant()) == iban
            && IbanValidator.Normalize(iban[2..]) == iban
            && !IbanValidator.IsValid(tampered);
    }

    // ── کارت هدیه ──────────────────────────────────────────────────────

    [Property(MaxTest = 200)]
    public bool Gift_card_codes_round_trip_through_every_way_a_customer_types_them(bool lower, bool persianDigits, bool noDashes)
    {
        var code = GiftCardCode.Generate();
        var typed = code;
        if (lower) typed = typed.ToLowerInvariant();
        if (noDashes) typed = typed.Replace("-", " ", StringComparison.Ordinal);
        if (persianDigits) typed = new string(typed.Select(ch => char.IsAsciiDigit(ch) ? (char)('۰' + (ch - '0')) : ch).ToArray());

        var normalized = GiftCardCode.Normalize(typed);
        return normalized is not null
            && normalized.Length == GiftCardCode.Length
            && GiftCardCode.Format(normalized) == code
            && GiftCardCode.Hash(normalized) == GiftCardCode.Hash(GiftCardCode.Normalize(code)!);
    }

    [Property(MaxTest = 300)]
    public bool Random_text_is_never_accepted_as_a_gift_card_unless_it_fits_the_alphabet(string? input)
    {
        var normalized = GiftCardCode.Normalize(input);
        return normalized is null || (normalized.Length == GiftCardCode.Length && normalized.All(GiftCardCode.Alphabet.Contains));
    }

    // ── قیمت ───────────────────────────────────────────────────────────

    [Property(MaxTest = 500)]
    public bool Final_price_is_never_negative_nor_above_the_list_price(int price, int discount, bool hasDiscount, sbyte startDays, sbyte endDays)
    {
        var now = DateTimeOffset.UtcNow;
        var product = new Product
        {
            Price = Index(price, 1_000_000_000),
            DiscountPrice = hasDiscount ? discount : null,
            DiscountStartAt = startDays == 0 ? null : now.AddDays(startDays),
            DiscountEndAt = endDays == 0 ? null : now.AddDays(endDays),
        };

        var final = product.GetFinalPrice(now);
        return final >= 0 && final <= product.Price && product.GetDiscountAmount(now) == product.Price - final;
    }

    // ── قواعد سفارش ────────────────────────────────────────────────────

    [Property(MaxTest = 200)]
    public bool Customers_can_never_cancel_what_the_shop_cannot(byte raw)
    {
        var status = (OrderStatus)(raw % 12);
        return !OrderRules.IsCancellableByCustomer(status) || OrderRules.IsCancellableByShop(status);
    }

    [Property(MaxTest = 300)]
    public bool Only_pending_unpaid_online_orders_before_the_deadline_are_payable(byte status, byte method, byte payment, sbyte minutes)
    {
        var now = DateTimeOffset.UtcNow;
        var s = (OrderStatus)(status % 10);
        var m = (PaymentMethod)(method % 6);
        var p = (PaymentStatus)(payment % 6);
        DateTimeOffset? deadline = minutes == 0 ? null : now.AddMinutes(minutes);

        var payable = OrderRules.IsPayable(s, m, p, deadline, now);
        var expected = s == OrderStatus.Pending && m == PaymentMethod.OnlineGateway && p != PaymentStatus.Paid && p != PaymentStatus.Refunded
            && (deadline is null || deadline > now);
        return payable == expected;
    }

    // ── فایروال: بازه‌های IP ───────────────────────────────────────────

    [Property(MaxTest = 500)]
    public bool An_ip_range_contains_its_own_addresses_and_nothing_outside(uint address, byte prefixSeed, uint other)
    {
        var prefix = 8 + prefixSeed % 25; // 8..32
        var ip = new IPAddress(BitConverter.GetBytes(NetworkOrder(address)));
        if (!IpRange.TryParse($"{ip}/{prefix}", out var range))
            return false;

        var mask = prefix == 32 ? uint.MaxValue : ~(uint.MaxValue >> prefix);
        var otherIp = new IPAddress(BitConverter.GetBytes(NetworkOrder(other)));
        var sameNetwork = (address & mask) == (other & mask);

        return range.Contains(ip) && range.Contains(otherIp) == sameNetwork
            && IpRange.TryParse(range.Text, out var again) && again.Text == range.Text;
    }

    /// <summary>عدد ۳۲بیتی (ترتیب شبکه) → بایت‌ها برای IPAddress، مستقل از Endianness ماشین.</summary>
    private static uint NetworkOrder(uint value) =>
        BitConverter.IsLittleEndian ? System.Buffers.Binary.BinaryPrimitives.ReverseEndianness(value) : value;

    [Property(MaxTest = 300)]
    public bool Garbage_is_never_parsed_as_an_ip_range_that_blocks_everyone(string? input)
    {
        if (!IpRange.TryParse(input, out var range))
            return true;
        // هر چیزی که پذیرفته شود باید شکل استاندارد و قابل بازخوانی داشته باشد.
        return IpRange.TryParse(range.Text, out var again) && again.Text == range.Text;
    }
}
