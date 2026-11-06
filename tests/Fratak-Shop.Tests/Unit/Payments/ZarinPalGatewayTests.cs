using System.Net;
using System.Text;
using System.Text.Json;
using Fratak_Shop.Core.DTOs.Settings;
using Fratak_Shop.Web.Services.Payments;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fratak_Shop.Tests.Unit.Payments;

/// <summary>
/// درگاه زرین‌پال در برابر پاسخ‌های درست، خطادار، ناقص و خراب (بدون شبکهٔ واقعی).
/// قاعدهٔ اصلی: هیچ پاسخی از درگاه — هر قدر هم عجیب — نباید استثنا بیرون بدهد؛ نتیجه همیشه «موفق» یا «ناموفق» است.
/// </summary>
[Trait("Area", "Payments")]
public class ZarinPalGatewayTests
{
    private static readonly StoreSettingDto Settings = new() { IsOnlinePaymentEnabled = true, ZarinPalMerchantId = "00000000-0000-0000-0000-000000000000", ZarinPalSandbox = true };
    private static readonly GatewayPaymentRequest Request = new(1_500m, "سفارش FR-1", "https://fratak.test/payment/callback", "09120000000", null);

    [Fact]
    public async Task Successful_request_returns_the_sandbox_start_pay_url_and_sends_rials()
    {
        var handler = new CannedHandler(HttpStatusCode.OK, """{"data":{"code":100,"message":"Success","authority":"A0000000000000000000000000000000001"},"errors":[]}""");
        var result = await Gateway(handler).RequestAsync(Settings, Request);

        Assert.True(result.Succeeded);
        Assert.Equal("A0000000000000000000000000000000001", result.Authority);
        Assert.Equal("https://sandbox.zarinpal.com/pg/StartPay/A0000000000000000000000000000000001", result.RedirectUrl);

        using var sent = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(15_000, sent.RootElement.GetProperty("amount").GetInt64()); // تومان × ۱۰
        Assert.Equal("https://sandbox.zarinpal.com/pg/v4/payment/request.json", handler.LastUri!.ToString());
    }

    [Fact]
    public async Task Production_mode_uses_the_production_host()
    {
        var production = new StoreSettingDto { IsOnlinePaymentEnabled = true, ZarinPalMerchantId = Settings.ZarinPalMerchantId, ZarinPalSandbox = false };
        var handler = new CannedHandler(HttpStatusCode.OK, """{"data":{"code":100,"authority":"A1"},"errors":[]}""");

        var result = await Gateway(handler).RequestAsync(production, Request);

        Assert.StartsWith("https://payment.zarinpal.com/pg/StartPay/", result.RedirectUrl);
    }

    [Fact]
    public async Task Disabled_online_payment_never_calls_the_gateway()
    {
        var handler = new CannedHandler(HttpStatusCode.OK, "{}");
        var result = await Gateway(handler).RequestAsync(new StoreSettingDto { IsOnlinePaymentEnabled = false, ZarinPalMerchantId = "x" }, Request);

        Assert.False(result.Succeeded);
        Assert.Equal(0, handler.Calls);
    }

    // بدترین حالت‌ها: هر کدام باید «ناموفق» برگردد، نه استثنا (استثنا = صفحهٔ خطای ۵۰۰ برای مشتری).
    [Theory]
    [InlineData(200, """{"data":[],"errors":{"code":-9,"message":"validation error"}}""")]
    [InlineData(200, """{"data":[],"errors":{"code":-11,"message":"merchant inactive"}}""")]
    [InlineData(200, """{"data":{"code":100,"authority":12345},"errors":[]}""")]       // authority عددی
    [InlineData(200, """{"data":{"code":100,"authority":""},"errors":[]}""")]          // authority خالی
    [InlineData(200, """{"data":{"code":100},"errors":[]}""")]                         // بدون authority
    [InlineData(200, """{"data":{"code":"100","authority":"A1"},"errors":[]}""")]      // کد رشته‌ای
    [InlineData(200, """{"unexpected":true}""")]
    [InlineData(200, """[]""")]
    [InlineData(200, """null""")]
    [InlineData(200, "<html>bad gateway</html>")]
    [InlineData(200, "")]
    [InlineData(502, "Bad Gateway")]
    [InlineData(500, """{"data":[],"errors":{"code":-1}}""")]
    public async Task Broken_or_rejecting_answers_become_a_failed_result(int status, string body)
    {
        var result = await Gateway(new CannedHandler((HttpStatusCode)status, body)).RequestAsync(Settings, Request);

        Assert.False(result.Succeeded);
        Assert.Null(result.RedirectUrl);
        Assert.False(string.IsNullOrWhiteSpace(result.Error));
    }

    [Fact]
    public async Task Network_failure_and_timeout_become_a_failed_result()
    {
        var offline = await Gateway(new CannedHandler(new HttpRequestException("connection refused"))).RequestAsync(Settings, Request);
        var timeout = await Gateway(new CannedHandler(new TaskCanceledException("timeout"))).RequestAsync(Settings, Request);
        var surprise = await Gateway(new CannedHandler(new InvalidOperationException("boom"))).RequestAsync(Settings, Request);

        Assert.False(offline.Succeeded);
        Assert.False(timeout.Succeeded);
        Assert.False(surprise.Succeeded);
    }

    [Fact]
    public async Task Cancellation_by_the_caller_is_not_swallowed()
    {
        using var cts = new CancellationTokenSource();
        await cts.CancelAsync();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            Gateway(new CannedHandler(HttpStatusCode.OK, "{}")).RequestAsync(Settings, Request, cts.Token));
    }

    [Theory]
    [InlineData("""{"data":{"code":100,"ref_id":201,"card_pan":"5022-29**-****-1234"},"errors":[]}""", "201")]
    [InlineData("""{"data":{"code":101,"ref_id":"201"},"errors":[]}""", "201")] // callback تکراری: قبلاً تأیید شده
    public async Task Verify_accepts_100_and_101_with_a_reference(string body, string refId)
    {
        var result = await Gateway(new CannedHandler(HttpStatusCode.OK, body)).VerifyAsync(Settings, "A1", 1_500m);

        Assert.True(result.Succeeded);
        Assert.Equal(refId, result.RefId);
    }

    [Theory]
    [InlineData("""{"data":[],"errors":{"code":-51,"message":"failed"}}""", false)]
    [InlineData("""{"data":[],"errors":{"code":-50,"message":"amount mismatch"}}""", false)]
    [InlineData("""{"data":{"code":100,"ref_id":201,"card_pan":1234},"errors":[]}""", true)] // card_pan عددی
    [InlineData("""{"unexpected":true}""", true)]
    [InlineData("<html></html>", true)]
    public async Task Verify_never_throws_and_marks_unclear_answers_as_transient(string body, bool transient)
    {
        var result = await Gateway(new CannedHandler(HttpStatusCode.OK, body)).VerifyAsync(Settings, "A1", 1_500m);

        Assert.False(result.Succeeded);
        Assert.Equal(transient, result.IsTransient);
    }

    [Fact]
    public async Task Verify_sends_the_same_rial_amount_as_the_request()
    {
        var handler = new CannedHandler(HttpStatusCode.OK, """{"data":{"code":100,"ref_id":1},"errors":[]}""");
        await Gateway(handler).VerifyAsync(Settings, "A1", 1_234.5m);

        using var sent = JsonDocument.Parse(handler.LastBody!);
        Assert.Equal(12_345, sent.RootElement.GetProperty("amount").GetInt64());
        Assert.Equal("A1", sent.RootElement.GetProperty("authority").GetString());
    }

    private static ZarinPalGateway Gateway(CannedHandler handler) => new(new HttpClient(handler), NullLogger<ZarinPalGateway>.Instance);

    /// <summary>پاسخ ثابت (یا استثنای ثابت) برای هر درخواست؛ آخرین درخواست را برای بررسی نگه می‌دارد.</summary>
    private sealed class CannedHandler : HttpMessageHandler
    {
        private readonly HttpStatusCode _status;
        private readonly string _body = string.Empty;
        private readonly Exception? _exception;

        public CannedHandler(HttpStatusCode status, string body) { _status = status; _body = body; }
        public CannedHandler(Exception exception) { _exception = exception; }

        public int Calls { get; private set; }
        public string? LastBody { get; private set; }
        public Uri? LastUri { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Calls++;
            LastUri = request.RequestUri;
            LastBody = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            if (_exception is not null)
                throw _exception;
            return new HttpResponseMessage(_status) { Content = new StringContent(_body, Encoding.UTF8, "application/json") };
        }
    }
}
