using System.Net.Http.Json;
using System.Text.Json;
using Fratak_Shop.Core.DTOs.Settings;

namespace Fratak_Shop.Web.Services.Payments;

/// <summary>Online payment provider abstraction (ZarinPal today; others can be added behind it).</summary>
public interface IPaymentGateway
{
    string Name { get; }

    /// <summary>Registers a payment and returns the authority + the URL the customer is redirected to.</summary>
    Task<GatewayRequestResult> RequestAsync(StoreSettingDto settings, GatewayPaymentRequest request, CancellationToken cancellationToken = default);

    /// <summary>Confirms a payment after the customer returned from the gateway.</summary>
    Task<GatewayVerifyResult> VerifyAsync(StoreSettingDto settings, string authority, decimal amountToman, CancellationToken cancellationToken = default);
}

public record GatewayPaymentRequest(decimal AmountToman, string Description, string CallbackUrl, string? Mobile, string? Email);

public record GatewayRequestResult(bool Succeeded, string? Authority, string? RedirectUrl, int? Code, string? Error);

/// <summary>
/// <see cref="IsTransient"/> = the gateway could not be reached / answered unexpectedly:
/// the payment state is unknown and verification may be retried.
/// </summary>
public record GatewayVerifyResult(bool Succeeded, string? RefId, string? CardPan, int? Code, string? Error, bool IsTransient = false);

/// <summary>
/// ZarinPal REST API v4 (https://www.zarinpal.com/docs). Amounts are sent in Rial
/// (the shop works in Toman). Sandbox / production and the merchant id come from the
/// store settings in the database, so switching to production needs no code change.
/// </summary>
public class ZarinPalGateway(HttpClient httpClient, ILogger<ZarinPalGateway> logger) : IPaymentGateway
{
    public const string ProductionHost = "https://payment.zarinpal.com";
    public const string SandboxHost = "https://sandbox.zarinpal.com";

    public string Name => "زرین‌پال";

    public static string HostFor(StoreSettingDto settings) => settings.ZarinPalSandbox ? SandboxHost : ProductionHost;

    public async Task<GatewayRequestResult> RequestAsync(StoreSettingDto settings, GatewayPaymentRequest request, CancellationToken cancellationToken = default)
    {
        if (!settings.HasZarinPal)
            return new GatewayRequestResult(false, null, null, null, "پرداخت اینترنتی فعال نیست.");

        var host = HostFor(settings);
        var metadata = new Dictionary<string, string>();
        if (!string.IsNullOrWhiteSpace(request.Mobile)) metadata["mobile"] = request.Mobile;
        if (!string.IsNullOrWhiteSpace(request.Email)) metadata["email"] = request.Email;

        var payload = new Dictionary<string, object>
        {
            ["merchant_id"] = settings.ZarinPalMerchantId!,
            ["amount"] = ToRial(request.AmountToman),
            ["description"] = request.Description.Length > 500 ? request.Description[..500] : request.Description,
            ["callback_url"] = request.CallbackUrl,
        };
        if (metadata.Count > 0) payload["metadata"] = metadata;

        try
        {
            using var response = await httpClient.PostAsJsonAsync($"{host}/pg/v4/payment/request.json", payload, cancellationToken);
            using var document = await ReadJsonAsync(response, cancellationToken);
            var (code, message, data) = Parse(document);

            if (code == 100 && data?.TryGetProperty("authority", out var authorityElement) == true
                && authorityElement.GetString() is { Length: > 0 } authority)
            {
                return new GatewayRequestResult(true, authority, $"{host}/pg/StartPay/{Uri.EscapeDataString(authority)}", code, null);
            }

            logger.LogWarning("ZarinPal request failed: {Code} {Message}", code, message);
            return new GatewayRequestResult(false, null, null, code, ErrorMessage(code, message));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(exception, "ZarinPal payment request could not be sent.");
            return new GatewayRequestResult(false, null, null, null, "ارتباط با درگاه پرداخت برقرار نشد؛ لطفاً چند لحظه بعد دوباره تلاش کنید.");
        }
    }

    public async Task<GatewayVerifyResult> VerifyAsync(StoreSettingDto settings, string authority, decimal amountToman, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(settings.ZarinPalMerchantId))
            return new GatewayVerifyResult(false, null, null, null, "پرداخت اینترنتی فعال نیست.");

        var payload = new Dictionary<string, object>
        {
            ["merchant_id"] = settings.ZarinPalMerchantId,
            ["amount"] = ToRial(amountToman),
            ["authority"] = authority,
        };

        try
        {
            using var response = await httpClient.PostAsJsonAsync($"{HostFor(settings)}/pg/v4/payment/verify.json", payload, cancellationToken);
            using var document = await ReadJsonAsync(response, cancellationToken);
            var (code, message, data) = Parse(document);

            // 100 = verified now, 101 = already verified before (repeated callback).
            if (code is 100 or 101 && data is { } d)
            {
                var refId = d.TryGetProperty("ref_id", out var refElement)
                    ? refElement.ValueKind == JsonValueKind.Number ? refElement.GetRawText() : refElement.GetString()
                    : null;
                var cardPan = d.TryGetProperty("card_pan", out var panElement) ? panElement.GetString() : null;

                if (!string.IsNullOrWhiteSpace(refId))
                    return new GatewayVerifyResult(true, refId, cardPan, code, null);
            }

            if (code is null)
                return new GatewayVerifyResult(false, null, null, null, "پاسخ نامعتبر از درگاه پرداخت.", IsTransient: true);

            logger.LogWarning("ZarinPal verify failed for {Authority}: {Code} {Message}", authority, code, message);
            return new GatewayVerifyResult(false, null, null, code, ErrorMessage(code, message));
        }
        catch (Exception exception) when (exception is HttpRequestException or TaskCanceledException or JsonException)
        {
            logger.LogError(exception, "ZarinPal verify call failed for {Authority}.", authority);
            return new GatewayVerifyResult(false, null, null, null, "ارتباط با درگاه برای تایید پرداخت برقرار نشد.", IsTransient: true);
        }
    }

    private static long ToRial(decimal toman) => (long)decimal.Round(toman * 10, 0, MidpointRounding.AwayFromZero);

    private static async Task<JsonDocument> ReadJsonAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        await using var stream = await response.Content.ReadAsStreamAsync(cancellationToken);
        return await JsonDocument.ParseAsync(stream, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// ZarinPal returns <c>{"data":{...},"errors":[]}</c> on success and
    /// <c>{"data":[],"errors":{"code":-9,"message":"..."}}</c> on failure.
    /// </summary>
    private static (int? Code, string? Message, JsonElement? Data) Parse(JsonDocument document)
    {
        var root = document.RootElement;

        if (root.TryGetProperty("data", out var data) && data.ValueKind == JsonValueKind.Object
            && data.TryGetProperty("code", out var codeElement) && codeElement.TryGetInt32(out var code))
        {
            var message = data.TryGetProperty("message", out var m) ? m.GetString() : null;
            return (code, message, data);
        }

        if (root.TryGetProperty("errors", out var errors) && errors.ValueKind == JsonValueKind.Object
            && errors.TryGetProperty("code", out var errorCode) && errorCode.TryGetInt32(out var error))
        {
            var message = errors.TryGetProperty("message", out var m) ? m.GetString() : null;
            return (error, message, null);
        }

        return (null, null, null);
    }

    private static string ErrorMessage(int? code, string? message) => code switch
    {
        -9 => "اطلاعات ارسال‌شده به درگاه معتبر نیست.",
        -10 or -11 or -12 => "درگاه پرداخت فروشگاه غیرفعال یا نامعتبر است.",
        -50 => "مبلغ پرداخت‌شده با مبلغ سفارش مطابقت ندارد.",
        -51 => "پرداخت ناموفق بود؛ در صورت کسر وجه، مبلغ ظرف ۷۲ ساعت توسط بانک برگشت داده می‌شود.",
        -54 => "این تراکنش نامعتبر است.",
        _ => string.IsNullOrWhiteSpace(message) ? "پرداخت ناموفق بود." : $"پرداخت ناموفق بود ({code}).",
    };
}
