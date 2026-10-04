using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace Fratak_Shop.Core.Services.Payments;

public sealed class ZarinPalGateway(HttpClient httpClient)
{
    private const string SandboxBaseUrl = "https://sandbox.zarinpal.com/pg/v4/payment";

    public async Task<ZarinPalStartResult?> RequestAsync(
        string merchantId,
        long amountInRial,
        string callbackUrl,
        string description,
        CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync($"{SandboxBaseUrl}/request.json", new
        {
            merchant_id = merchantId,
            amount = amountInRial,
            callback_url = callbackUrl,
            description,
            currency = "IRT",
        }, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return null;

        var payload = await response.Content.ReadFromJsonAsync<ZarinPalResponse>(cancellationToken);
        if (payload?.Data?.Code != 100 || string.IsNullOrWhiteSpace(payload.Data.Authority))
            return null;

        return new ZarinPalStartResult(payload.Data.Authority,
            $"https://sandbox.zarinpal.com/pg/StartPay/{payload.Data.Authority}");
    }

    public async Task<bool> VerifyAsync(string merchantId, long amountInRial, string authority,
        CancellationToken cancellationToken)
    {
        var response = await httpClient.PostAsJsonAsync($"{SandboxBaseUrl}/verify.json", new
        {
            merchant_id = merchantId,
            amount = amountInRial,
            authority,
        }, cancellationToken);

        if (!response.IsSuccessStatusCode)
            return false;

        var payload = await response.Content.ReadFromJsonAsync<ZarinPalResponse>(cancellationToken);
        return payload?.Data?.Code is 100 or 101;
    }

    private sealed class ZarinPalResponse
    {
        [JsonPropertyName("data")]
        public ZarinPalData? Data { get; set; }
    }

    private sealed class ZarinPalData
    {
        [JsonPropertyName("code")]
        public int Code { get; set; }

        [JsonPropertyName("authority")]
        public string? Authority { get; set; }
    }
}

public sealed record ZarinPalStartResult(string Authority, string RedirectUrl);
