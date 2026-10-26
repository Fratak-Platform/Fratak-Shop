using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;

namespace Fratak_Shop.Tests.E2E.Infrastructure;

/// <summary>
/// شبیه‌ساز درگاه زرین‌پال (API نسخهٔ ۴) برای آزمون‌ها: همان آدرس‌ها و همان قالب JSON که <c>ZarinPalGateway</c>
/// واقعی می‌فرستد/می‌خواند، بدون اینترنت. رفتار بانک را هم می‌شود عوض کرد (قطعی، رد مبلغ، ...) تا حالت‌های بد آزموده شوند.
/// <list type="bullet">
/// <item>request.json ← کد 100 و یک Authority تازه (مبلغ ریالی ذخیره می‌شود).</item>
/// <item>verify.json ← اگر Authority شناخته‌شده و مبلغ همان باشد: بار اول 100، بارهای بعد 101 (مثل زرین‌پال واقعی).
/// مبلغ متفاوت ← -50؛ Authority ناشناخته ← -54.</item>
/// </list>
/// </summary>
public sealed class FakeZarinPal
{
    private readonly ConcurrentDictionary<string, Payment> _payments = new();
    private int _refCounter = 100_000;

    /// <summary>درگاه در دسترس نیست (خطای شبکه) – برای آزمون «خطای موقت، دوباره تلاش».</summary>
    public bool Offline { get; set; }

    /// <summary>درخواست پرداخت رد شود (مثلاً مرچنت نامعتبر: -10).</summary>
    public int? RequestErrorCode { get; set; }

    /// <summary>تأیید پرداخت با این کد رد شود (مثلاً -51 پرداخت ناموفق).</summary>
    public int? VerifyErrorCode { get; set; }

    public int VerifyCalls;

    public IReadOnlyCollection<Payment> Payments => _payments.Values.ToList();

    public Payment? Find(string authority) => _payments.TryGetValue(authority, out var payment) ? payment : null;

    public void Reset()
    {
        Offline = false;
        RequestErrorCode = null;
        VerifyErrorCode = null;
    }

    internal (int Status, string Json) Handle(string path, JsonElement body)
    {
        if (path.EndsWith("/pg/v4/payment/request.json", StringComparison.Ordinal))
        {
            if (RequestErrorCode is { } error)
                return (200, Error(error, "request rejected"));

            var authority = "A" + Guid.NewGuid().ToString("N").ToUpperInvariant()[..35];
            var payment = new Payment(authority, body.GetProperty("amount").GetInt64(), body.GetProperty("callback_url").GetString() ?? "",
                body.GetProperty("merchant_id").GetString() ?? "");
            _payments[authority] = payment;
            return (200, JsonSerializer.Serialize(new { data = new { code = 100, message = "Success", authority, fee_type = "Merchant", fee = 0 }, errors = Array.Empty<object>() }));
        }

        if (path.EndsWith("/pg/v4/payment/verify.json", StringComparison.Ordinal))
        {
            Interlocked.Increment(ref VerifyCalls);
            if (VerifyErrorCode is { } error)
                return (200, Error(error, "verify rejected"));

            var authority = body.GetProperty("authority").GetString() ?? "";
            if (!_payments.TryGetValue(authority, out var payment))
                return (200, Error(-54, "invalid authority"));

            if (payment.AmountRial != body.GetProperty("amount").GetInt64())
                return (200, Error(-50, "amount mismatch"));

            var code = Interlocked.Exchange(ref payment.Verified, 1) == 0 ? 100 : 101;
            payment.RefId ??= Interlocked.Increment(ref _refCounter).ToString(System.Globalization.CultureInfo.InvariantCulture);
            return (200, JsonSerializer.Serialize(new
            {
                data = new { code, message = code == 100 ? "Verified" : "Verified before", card_hash = "X", card_pan = "502229******5995", ref_id = long.Parse(payment.RefId, System.Globalization.CultureInfo.InvariantCulture), fee_type = "Merchant", fee = 0 },
                errors = Array.Empty<object>(),
            }));
        }

        return (404, "{}");
    }

    private static string Error(int code, string message) =>
        JsonSerializer.Serialize(new { data = Array.Empty<object>(), errors = new { code, message, validations = Array.Empty<object>() } });

    public sealed class Payment(string authority, long amountRial, string callbackUrl, string merchantId)
    {
        public string Authority { get; } = authority;
        public long AmountRial { get; } = amountRial;
        public string CallbackUrl { get; } = callbackUrl;
        public string MerchantId { get; } = merchantId;
        public string? RefId { get; set; }
        public int Verified;
    }
}

/// <summary>HttpMessageHandler ای که درخواست‌های HttpClient درگاه را به <see cref="FakeZarinPal"/> می‌دهد.</summary>
public sealed class FakeZarinPalHandler(FakeZarinPal fake) : HttpMessageHandler
{
    protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        if (fake.Offline)
            throw new HttpRequestException("ZarinPal is unreachable (simulated).");

        var host = request.RequestUri?.Host ?? "";
        if (host is not ("sandbox.zarinpal.com" or "payment.zarinpal.com"))
            return new HttpResponseMessage(HttpStatusCode.BadGateway);

        var json = request.Content is null ? "{}" : await request.Content.ReadAsStringAsync(cancellationToken);
        using var document = JsonDocument.Parse(json);
        var (status, body) = fake.Handle(request.RequestUri!.AbsolutePath, document.RootElement);
        return new HttpResponseMessage((HttpStatusCode)status) { Content = new StringContent(body, Encoding.UTF8, "application/json") };
    }
}
