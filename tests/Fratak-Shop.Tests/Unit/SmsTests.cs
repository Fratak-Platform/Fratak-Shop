using System.Net;
using System.Text;
using System.Text.Json;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Data.Entities.Settings;
using Fratak_Shop.Web.Infrastructure.Sms;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;

namespace Fratak_Shop.Tests.Unit;

/// <summary>
/// تست‌های سرویس پیامک (دور هشتم – SMS.ir) بدون شبکه: یک HttpMessageHandler ساختگی نقش api.sms.ir را بازی می‌کند.
/// بخش‌ها: کلاینت REST (مسیر، سرآیند کلید، بدنهٔ PascalCase، خواندن پاسخ و خطاها)، ارسال‌کنندهٔ قابل‌تنظیم
/// (قالب/خط، نبودن کلید، کلید سرور، خطای سرویس) و مسیر شکست در OtpService.
/// </summary>
[Trait("Category", "Unit")]
public class SmsTests
{
    private const string Key = "TestApiKey_1234567890";

    /// <summary>جای api.sms.ir: درخواست‌ها را نگه می‌دارد و پاسخ دلخواه می‌دهد.</summary>
    private sealed class FakeSmsIr : HttpMessageHandler
    {
        public List<(HttpMethod Method, string Url, string? ApiKey, string? Body)> Requests { get; } = [];
        public HttpStatusCode Status { get; set; } = HttpStatusCode.OK;
        public string Response { get; set; } = """{"status":1,"message":"موفق","data":{"messageId":10,"cost":1.0}}""";
        public Exception? Throw { get; set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested(); // مثل handler واقعی
            var body = request.Content is null ? null : await request.Content.ReadAsStringAsync(cancellationToken);
            var key = request.Headers.TryGetValues("x-api-key", out var values) ? values.Single() : null;
            lock (Requests)
                Requests.Add((request.Method, request.RequestUri!.ToString(), key, body));
            if (Throw is not null)
                throw Throw;
            return new HttpResponseMessage(Status) { Content = new StringContent(Response, Encoding.UTF8, "application/json") };
        }
    }

    private sealed class Factory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name)
        {
            var client = new HttpClient(handler, disposeHandler: false);
            SmsIrClient.Configure(client);
            return client;
        }
    }

    private sealed class Env(string name) : IWebHostEnvironment
    {
        public string EnvironmentName { get; set; } = name;
        public string ApplicationName { get; set; } = "Fratak-Shop.Tests";
        public string WebRootPath { get; set; } = "";
        public IFileProvider WebRootFileProvider { get; set; } = new NullFileProvider();
        public string ContentRootPath { get; set; } = "";
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    /// <summary>تنظیمات ثابت به‌جای پایگاه داده؛ Throw = شبیه‌سازی قطعی پایگاه داده.</summary>
    private sealed class FixedSmsProvider(SmsRuntimeConfig config, bool fail = false) : ISmsProviderService
    {
        public Task<SmsProviderView> GetAsync(CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<Fratak_Shop.Core.Utilities.OperationResult> SaveAsync(SmsProviderInput input, CancellationToken cancellationToken = default) => throw new NotSupportedException();
        public Task<SmsRuntimeConfig> GetRuntimeAsync(CancellationToken cancellationToken = default) =>
            fail ? throw new InvalidOperationException("db down") : Task.FromResult(config);
    }

    private static (ConfigurableSmsSender Sender, FakeSmsIr Api, SmsDiagnostics Stats) Sender(SmsRuntimeConfig config,
        string environment = "Production", string? serverKey = null, bool dbFails = false)
    {
        var services = new ServiceCollection();
        services.AddScoped<ISmsProviderService>(_ => new FixedSmsProvider(config, dbFails));
        var configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(serverKey is null ? new Dictionary<string, string?>() : new Dictionary<string, string?> { [ConfigurableSmsSender.ConfigurationKey] = serverKey })
            .Build();
        var api = new FakeSmsIr();
        var stats = new SmsDiagnostics();
        var sender = new ConfigurableSmsSender(services.BuildServiceProvider().GetRequiredService<IServiceScopeFactory>(), new Factory(api),
            configuration, new Env(environment), stats, NullLogger<ConfigurableSmsSender>.Instance);
        return (sender, api, stats);
    }

    private static SmsRuntimeConfig Config(string? key = Key, long? line = 30000123456, int? template = 718643, SmsProvider provider = SmsProvider.SmsIr) =>
        new(provider, key, line, template, "Code");

    // ───────────── SmsIrClient ─────────────

    [Fact]
    public async Task Verify_PostsTemplatePayload_WithKeyHeaderOnRequestOnly()
    {
        var api = new FakeSmsIr();
        var http = new Factory(api).CreateClient(SmsIrClient.ClientName);
        var result = await new SmsIrClient(http).VerifyAsync(Key, "09121234567", 718643, [("Code", "12345")]);

        Assert.True(result.Ok);
        var request = Assert.Single(api.Requests);
        Assert.Equal(HttpMethod.Post, request.Method);
        Assert.Equal("https://api.sms.ir/v1/send/verify", request.Url);
        Assert.Equal(Key, request.ApiKey);
        Assert.False(http.DefaultRequestHeaders.Contains("x-api-key")); // کلید روی HttpClient مشترک نمی‌ماند

        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal("09121234567", body.RootElement.GetProperty("Mobile").GetString());
        Assert.Equal(718643, body.RootElement.GetProperty("TemplateId").GetInt32());
        var parameter = Assert.Single(body.RootElement.GetProperty("Parameters").EnumerateArray().ToList());
        Assert.Equal("Code", parameter.GetProperty("Name").GetString());
        Assert.Equal("12345", parameter.GetProperty("Value").GetString());
    }

    [Fact]
    public async Task Bulk_PostsLineTextAndMobiles()
    {
        var api = new FakeSmsIr();
        await new SmsIrClient(new Factory(api).CreateClient("x")).BulkAsync(Key, 30000123456, "سفارش شما ارسال شد", ["09121234567"]);

        var request = Assert.Single(api.Requests);
        Assert.Equal("https://api.sms.ir/v1/send/bulk", request.Url);
        using var body = JsonDocument.Parse(request.Body!);
        Assert.Equal(30000123456, body.RootElement.GetProperty("LineNumber").GetInt64());
        Assert.Equal("سفارش شما ارسال شد", body.RootElement.GetProperty("MessageText").GetString());
        Assert.Equal("09121234567", Assert.Single(body.RootElement.GetProperty("Mobiles").EnumerateArray().ToList()).GetString());
        Assert.Equal(JsonValueKind.Null, body.RootElement.GetProperty("SendDateTime").ValueKind);
    }

    [Fact]
    public async Task CreditAndLines_AreGets_AndReturnData()
    {
        var api = new FakeSmsIr { Response = """{"status":1,"message":"موفق","data":1520.5}""" };
        var client = new SmsIrClient(new Factory(api).CreateClient("x"));
        var credit = await client.CreditAsync(Key);
        Assert.True(credit.Ok);
        Assert.Equal(1520.5m, credit.Data!.Value.GetDecimal());

        api.Response = """{"status":1,"message":"موفق","data":[30000123456,50002]}""";
        var lines = await client.LinesAsync(Key);
        Assert.Equal(2, lines.Data!.Value.GetArrayLength());
        Assert.All(api.Requests, r => Assert.Equal(HttpMethod.Get, r.Method));
        Assert.Equal(new[] { "https://api.sms.ir/v1/credit", "https://api.sms.ir/v1/line" }, api.Requests.Select(r => r.Url).ToArray());
    }

    [Theory]
    [InlineData(200, """{"status":0,"message":"قالب یافت نشد"}""", "قالب یافت نشد")]
    [InlineData(401, """{"status":0,"message":"کلید نامعتبر"}""", "کلید API نامعتبر")]
    [InlineData(403, "", "دسترسی")]
    [InlineData(429, "Too many", "بیش از حد")]
    [InlineData(502, "<html>bad gateway</html>", "HTTP 502")]
    [InlineData(200, "not json", "رد شد")]
    [InlineData(200, "[1,2]", "رد شد")]
    [InlineData(200, """{"status":"1"}""", "رد شد")]
    public void Parse_FailureShapes_AreNotOk_WithPersianReason(int http, string body, string expected)
    {
        var result = SmsIrClient.Parse(http, http is >= 200 and < 300, body);
        Assert.False(result.Ok);
        Assert.Contains(expected, result.Message);
    }

    [Fact]
    public void Parse_IsCaseInsensitive_AndKeepsData()
    {
        var result = SmsIrClient.Parse(200, true, """{"Status":1,"Message":"OK","Data":{"PackId":"x"}}""");
        Assert.True(result.Ok);
        Assert.Equal("x", result.Data!.Value.GetProperty("PackId").GetString());
    }

    [Fact]
    public async Task NetworkErrors_NeverThrow()
    {
        var api = new FakeSmsIr { Throw = new HttpRequestException("dns") };
        var client = new SmsIrClient(new Factory(api).CreateClient("x"));
        var result = await client.VerifyAsync(Key, "09121234567", 1, [("Code", "1")]);
        Assert.False(result.Ok);
        Assert.Contains("ارتباط", result.Message);
        Assert.DoesNotContain(Key, result.Message);

        api.Throw = new TaskCanceledException("timeout"); // مهلت HttpClient (توکن کاربر لغو نشده)
        Assert.Contains("مهلت", (await client.CreditAsync(Key)).Message);
    }

    [Fact]
    public async Task CallerCancellation_IsPropagated()
    {
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        var api = new FakeSmsIr();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new SmsIrClient(new Factory(api).CreateClient("x")).CreditAsync(Key, cts.Token));
        Assert.Empty(api.Requests); // چیزی به SMS.ir نرفت
    }

    // ───────────── ConfigurableSmsSender ─────────────

    [Fact]
    public async Task Otp_UsesVerifyTemplate()
    {
        var (sender, api, stats) = Sender(Config());
        Assert.True(await sender.SendOtpAsync("09121234567", "54321", "کد ورود شما: 54321"));

        var request = Assert.Single(api.Requests);
        Assert.EndsWith("/send/verify", request.Url);
        Assert.Contains("\"Value\":\"54321\"", request.Body);
        Assert.Equal(1, stats.Sent);
    }

    [Fact]
    public async Task Otp_WithoutTemplate_FallsBackToLineText()
    {
        var (sender, api, _) = Sender(Config(template: null));
        Assert.True(await sender.SendOtpAsync("09121234567", "54321", "کد ورود شما: 54321"));
        var request = Assert.Single(api.Requests);
        Assert.EndsWith("/send/bulk", request.Url);
        Assert.Contains("54321", JsonDocument.Parse(request.Body!).RootElement.GetProperty("MessageText").GetString());
    }

    [Fact]
    public async Task Notification_UsesLine_AndWithoutLineFails()
    {
        var (sender, api, _) = Sender(Config());
        await sender.SendAsync("09121234567", "سفارش شما ارسال شد");
        Assert.EndsWith("/send/bulk", Assert.Single(api.Requests).Url);

        var (noLine, api2, stats) = Sender(Config(line: null));
        await noLine.SendAsync("09121234567", "متن");
        Assert.Empty(api2.Requests);
        Assert.Equal(1, stats.Failed);
        Assert.True(stats.LastAttemptFailed);
    }

    [Fact]
    public async Task NoKey_InProduction_FailsWithoutCallingApi_InDevelopmentLogsAndSucceeds()
    {
        var (prod, api, stats) = Sender(Config(key: null));
        Assert.False(await prod.SendOtpAsync("09121234567", "11111", "کد 11111"));
        Assert.Empty(api.Requests);
        Assert.Contains("کلید", stats.LastFailure);

        var (dev, devApi, _) = Sender(Config(key: null), environment: "Development");
        Assert.True(await dev.SendOtpAsync("09121234567", "11111", "کد 11111"));
        Assert.Empty(devApi.Requests);
    }

    [Fact]
    public async Task LogOnlyProvider_NeverCallsApi_EvenWithKey()
    {
        var (sender, api, _) = Sender(Config(provider: SmsProvider.LogOnly));
        Assert.False(await sender.SendOtpAsync("09121234567", "11111", "کد"));
        Assert.Empty(api.Requests);
    }

    [Fact]
    public async Task ServerConfigurationKey_IsFallback_PanelKeyWins()
    {
        var (fallback, api, _) = Sender(Config(key: null), serverKey: "ServerKey_ABCDEFGHIJ");
        Assert.True(await fallback.SendOtpAsync("09121234567", "11111", "کد"));
        Assert.Equal("ServerKey_ABCDEFGHIJ", Assert.Single(api.Requests).ApiKey);

        var (panel, api2, _) = Sender(Config(), serverKey: "ServerKey_ABCDEFGHIJ");
        await panel.SendOtpAsync("09121234567", "11111", "کد");
        Assert.Equal(Key, Assert.Single(api2.Requests).ApiKey);
    }

    [Fact]
    public async Task ProviderRejection_And_DatabaseOutage_ReturnFalse_NeverThrow()
    {
        var (sender, api, stats) = Sender(Config());
        api.Status = HttpStatusCode.Unauthorized;
        api.Response = """{"status":0,"message":"کلید نامعتبر"}""";
        Assert.False(await sender.SendOtpAsync("09121234567", "11111", "کد"));
        Assert.Contains("کلید API نامعتبر", stats.LastFailure);

        var (broken, brokenApi, brokenStats) = Sender(Config(), dbFails: true);
        Assert.False(await broken.SendOtpAsync("09121234567", "11111", "کد"));
        await broken.SendAsync("09121234567", "متن"); // بدون استثنا
        Assert.Empty(brokenApi.Requests);
        Assert.Equal(2, brokenStats.Failed);
    }

    [Fact]
    public async Task PanelTest_And_AccountCheck_UseCurrentConfig()
    {
        var (sender, api, _) = Sender(Config());
        Assert.True((await sender.TestAsync("09121234567", template: true)).Ok);
        Assert.True((await sender.TestAsync("09121234567", template: false)).Ok);
        Assert.Equal(new[] { "https://api.sms.ir/v1/send/verify", "https://api.sms.ir/v1/send/bulk" }, api.Requests.Select(r => r.Url).ToArray());

        var (noTemplate, _, _) = Sender(Config(template: null));
        Assert.False((await noTemplate.TestAsync("09121234567", template: true)).Ok);

        var (noKey, _, _) = Sender(Config(key: null));
        var (credit, lines) = await noKey.CheckAccountAsync();
        Assert.False(credit.Ok);
        Assert.Null(lines);
    }

    [Theory]
    [InlineData("09121234567", "0912***4567")]
    [InlineData("123", "***")]
    public void Mask_HidesMiddleDigits(string phone, string expected) => Assert.Equal(expected, ConfigurableSmsSender.Mask(phone));

    // ───────────── اعتبارسنجی ورودی پنل ─────────────

    [Theory]
    [InlineData("30000123456", true, 30000123456L)]
    [InlineData("۳۰۰۰۰۱۲۳۴۵۶", true, 30000123456L)]
    [InlineData("", true, null)]
    [InlineData("   ", true, null)]
    [InlineData("1234", false, null)]
    [InlineData("1234567890123456", false, null)]
    [InlineData("300001234a6", false, null)]
    [InlineData("-3000012345", false, null)]
    [InlineData("00000", false, null)]
    [InlineData("3000 0123456", false, null)]
    public void TryParsePositive_LineNumber(string text, bool valid, long? expected)
    {
        Assert.Equal(valid, SmsProviderService.TryParsePositive(text, 5, 15, out var value));
        Assert.Equal(expected, value);
    }

    [Theory]
    [InlineData("AbCdEf0123456789", true)]
    [InlineData("abc-DEF_123.456=+/", true)]
    [InlineData("short", false)]
    [InlineData("has space 12345", false)]
    [InlineData("کلیدفارسی۱۲۳۴۵۶۷", false)]
    [InlineData("key\r\nX-Evil: 1abc", false)]
    [InlineData("<script>alert(1)</script>", false)]
    public void ApiKey_Format(string key, bool valid) => Assert.Equal(valid, SmsProviderService.IsApiKey(key));

    // ───────────── OtpService ─────────────

    private sealed class FailingSms : ISmsSender
    {
        public int Calls;
        public bool Result { get; set; }
        public Task SendAsync(string phoneNumber, string message) => Task.CompletedTask;
        public Task<bool> SendOtpAsync(string phoneNumber, string code, string message)
        {
            Interlocked.Increment(ref Calls);
            LastCode = code;
            return Task.FromResult(Result);
        }
        public string? LastCode { get; private set; }
    }

    [Fact]
    public async Task Otp_WhenSmsFails_CodeIsInvalid_AndRetryIsImmediate()
    {
        var sms = new FailingSms { Result = false };
        var otp = new OtpService(new MemoryCache(new MemoryCacheOptions()), sms);

        var first = await otp.SendAsync("09121234567", OtpPurpose.Login);
        Assert.True(first.Failed);
        Assert.False(first.Sent);
        Assert.False(otp.Verify("09121234567", sms.LastCode!, OtpPurpose.Login)); // کد نرسیده معتبر نیست

        sms.Result = true;
        var second = await otp.SendAsync("09121234567", OtpPurpose.Login); // بدون انتظار ۶۰ ثانیه
        Assert.True(second.Sent);
        Assert.False(second.Failed);
        Assert.True(otp.Verify("09121234567", sms.LastCode!, OtpPurpose.Login));
    }

    [Fact]
    public async Task Otp_FailedSends_StillCountTowardHourlyLimit()
    {
        var sms = new FailingSms { Result = false };
        var otp = new OtpService(new MemoryCache(new MemoryCacheOptions()), sms);
        for (var i = 0; i < 6; i++)
            Assert.True((await otp.SendAsync("09120001111")).Failed);

        var blocked = await otp.SendAsync("09120001111");
        Assert.True(blocked.Blocked);
        Assert.Equal(6, sms.Calls);
    }

    private sealed class PlainSms : ISmsSender
    {
        public List<string> Messages { get; } = [];
        public Task SendAsync(string phoneNumber, string message)
        {
            Messages.Add(message);
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task DefaultSendOtp_FallsBackToText()
    {
        var plain = new PlainSms();
        Assert.True(await ((ISmsSender)plain).SendOtpAsync("09121234567", "12345", "کد تایید شما: 12345"));
        Assert.Equal("کد تایید شما: 12345", Assert.Single(plain.Messages));
    }
}
