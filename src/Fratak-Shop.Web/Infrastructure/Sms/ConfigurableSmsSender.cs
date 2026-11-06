using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Data.Entities.Settings;

namespace Fratak_Shop.Web.Infrastructure.Sms;

/// <summary>
/// آمار ارسال پیامک از زمان روشن شدن برنامه (فقط در حافظه؛ برای صفحهٔ «سرویس پیامک» و «سلامت سامانه»).
/// عمداً در پایگاه داده نوشته نمی‌شود تا هر پیامک یک نوشتن اضافه نداشته باشد.
/// </summary>
public sealed class SmsDiagnostics
{
    private long _sent;
    private long _failed;
    private readonly object _gate = new();

    public long Sent => Interlocked.Read(ref _sent);
    public long Failed => Interlocked.Read(ref _failed);
    public DateTimeOffset? LastSuccessAt { get; private set; }
    public DateTimeOffset? LastFailureAt { get; private set; }
    public string? LastFailure { get; private set; }

    public void Success()
    {
        Interlocked.Increment(ref _sent);
        lock (_gate)
            LastSuccessAt = DateTimeOffset.UtcNow;
    }

    public void Failure(string reason)
    {
        Interlocked.Increment(ref _failed);
        lock (_gate)
        {
            LastFailureAt = DateTimeOffset.UtcNow;
            LastFailure = reason.Length <= 500 ? reason : reason[..500];
        }
    }

    /// <summary>آخرین ارسال ناموفق‌تر از آخرین موفق است (برای هشدار سلامت).</summary>
    public bool LastAttemptFailed => LastFailureAt is { } failed && (LastSuccessAt is not { } ok || failed > ok);
}

/// <summary>
/// ارسال‌کنندهٔ پیامک برنامه (جایگزین ISmsSender). بر اساس تنظیمات پنل («فنی ← سرویس پیامک»):
/// <list type="bullet">
/// <item>SMS.ir + کلید API: کد یک‌بارمصرف با قالب (send/verify) و اگر قالب تعریف نشده بود با متن از خط ارسال؛
/// پیامک‌های متنی (اعلان سفارش و …) با send/bulk از خط ارسال.</item>
/// <item>«فقط لاگ» یا نبودن کلید: پیامکی فرستاده نمی‌شود. بیرون از محیط Production متن در لاگ نوشته می‌شود (برای توسعه)؛
/// در Production متن (که ممکن است کد ورود باشد) هرگز لاگ نمی‌شود.</item>
/// </list>
/// کلید API: اول از پایگاه داده (رمزنگاری‌شده)، وگرنه از تنظیمات سرور <c>SmsIr:ApiKey</c> (متغیر محیطی SmsIr__ApiKey یا user-secrets).
/// هیچ متدی استثنا نمی‌دهد؛ ثبت‌نام، ورود و سفارش هرگز به خاطر پیامک خراب نمی‌شوند.
/// </summary>
public sealed class ConfigurableSmsSender(
    IServiceScopeFactory scopes,
    IHttpClientFactory httpClients,
    IConfiguration configuration,
    IWebHostEnvironment environment,
    SmsDiagnostics diagnostics,
    ILogger<ConfigurableSmsSender> logger) : ISmsSender
{
    /// <summary>کلید پیکربندی سرور برای کلید API (وقتی در پنل ثبت نشده باشد).</summary>
    public const string ConfigurationKey = "SmsIr:ApiKey";

    /// <summary>آمار ارسال (برای پنل و سلامت سامانه).</summary>
    public SmsDiagnostics Diagnostics => diagnostics;

    public async Task SendAsync(string phoneNumber, string message) =>
        await DeliverAsync(phoneNumber, message, code: null);

    public Task<bool> SendOtpAsync(string phoneNumber, string code, string message) =>
        DeliverAsync(phoneNumber, message, code);

    /// <summary>وضعیت فعلی برای پنل و سلامت سامانه: (سرویس، کلید از کجا، پیام).</summary>
    public async Task<(SmsProvider Provider, string? KeySource, SmsRuntimeConfig Config)> DescribeAsync(CancellationToken cancellationToken = default)
    {
        var config = await LoadAsync(cancellationToken);
        var source = !string.IsNullOrEmpty(config.ApiKey) ? "پنل مدیریت" : !string.IsNullOrWhiteSpace(configuration[ConfigurationKey]) ? "تنظیمات سرور" : null;
        return (config.Provider, source, config);
    }

    /// <summary>کلید مؤثر: پنل، وگرنه تنظیمات سرور.</summary>
    public string? EffectiveApiKey(SmsRuntimeConfig config) =>
        !string.IsNullOrEmpty(config.ApiKey) ? config.ApiKey : configuration[ConfigurationKey]?.Trim() is { Length: > 0 } fromServer ? fromServer : null;

    public SmsIrClient CreateClient() => new(httpClients.CreateClient(SmsIrClient.ClientName));

    /// <summary>
    /// آزمون از پنل: یک پیامک واقعی با تنظیمات فعلی. template=true → کد نمونهٔ ۱۲۳۴۵ با قالب کد تأیید؛
    /// false → متن کوتاه از خط ارسال. نتیجه (موفق/علت خطا) در آمار هم ثبت می‌شود.
    /// </summary>
    public async Task<SmsIrResponse> TestAsync(string phone, bool template, CancellationToken cancellationToken = default)
    {
        var config = await LoadAsync(cancellationToken);
        if (EffectiveApiKey(config) is not { } apiKey)
            return new SmsIrResponse(false, null, null, "کلید API ثبت نشده است.");

        var client = CreateClient();
        SmsIrResponse response;
        if (template)
        {
            if (config.OtpTemplateId is not { } templateId)
                return new SmsIrResponse(false, null, null, "شناسهٔ قالب کد تأیید ثبت نشده است.");
            response = await client.VerifyAsync(apiKey, phone, templateId, [(config.OtpParameterName, "12345")], cancellationToken);
        }
        else
        {
            if (config.LineNumber is not { } line)
                return new SmsIrResponse(false, null, null, "شمارهٔ خط ارسال ثبت نشده است.");
            response = await client.BulkAsync(apiKey, line, "پیامک آزمایشی فروشگاه: اتصال سرویس پیامک برقرار است.", [phone], cancellationToken);
        }

        if (response.Ok)
            diagnostics.Success();
        else
            diagnostics.Failure(response.Message);
        return response;
    }

    /// <summary>اعتبار و خط‌های حساب SMS.ir (برای دکمهٔ «بررسی اتصال»؛ پیامکی ارسال نمی‌شود).</summary>
    public async Task<(SmsIrResponse Credit, SmsIrResponse? Lines)> CheckAccountAsync(CancellationToken cancellationToken = default)
    {
        var config = await LoadAsync(cancellationToken);
        if (EffectiveApiKey(config) is not { } apiKey)
            return (new SmsIrResponse(false, null, null, "کلید API ثبت نشده است."), null);

        var client = CreateClient();
        var credit = await client.CreditAsync(apiKey, cancellationToken);
        return credit.Ok ? (credit, await client.LinesAsync(apiKey, cancellationToken)) : (credit, null);
    }

    private async Task<bool> DeliverAsync(string phoneNumber, string message, string? code)
    {
        var phone = phoneNumber?.Trim() ?? string.Empty;
        try
        {
            var config = await LoadAsync(CancellationToken.None);
            var apiKey = EffectiveApiKey(config);
            if (config.Provider != SmsProvider.SmsIr || apiKey is null)
                return LogOnly(phone, message, config.Provider == SmsProvider.SmsIr ? "کلید API سرویس SMS.ir ثبت نشده است" : null);

            var client = CreateClient();
            SmsIrResponse response;
            if (code is not null && config.OtpTemplateId is { } templateId)
                response = await client.VerifyAsync(apiKey, phone, templateId, [(config.OtpParameterName, code)]);
            else if (config.LineNumber is { } line)
                response = await client.BulkAsync(apiKey, line, message, [phone]);
            else
                return Fail(phone, code is null
                    ? "شمارهٔ خط ارسال SMS.ir ثبت نشده است؛ پیامک متنی فرستاده نشد."
                    : "نه قالب کد تأیید و نه خط ارسال SMS.ir ثبت شده است.");

            if (!response.Ok)
                return Fail(phone, response.Message);

            diagnostics.Success();
            return true;
        }
        catch (Exception exception)
        {
            // مثلاً پایگاه داده در دسترس نیست؛ ارسال پیامک هرگز نباید درخواست کاربر را با خطا تمام کند.
            logger.LogError(exception, "SMS to {Phone} could not be sent.", Mask(phone));
            diagnostics.Failure(exception.GetType().Name + ": " + exception.Message);
            return false;
        }
    }

    private bool LogOnly(string phone, string message, string? problem)
    {
        if (!environment.IsProduction())
        {
            logger.LogInformation("SMS to {PhoneNumber}: {Message}", phone, message);
            return true; // در توسعه کد از لاگ/صفحه خوانده می‌شود؛ «ارسال‌شده» حساب می‌شود.
        }

        return Fail(phone, problem ?? "سرویس پیامک روی «فقط لاگ» است؛ پیامکی ارسال نمی‌شود.");
    }

    private bool Fail(string phone, string reason)
    {
        logger.LogWarning("SMS to {Phone} was not sent: {Reason}", Mask(phone), reason);
        diagnostics.Failure(reason);
        return false;
    }

    private async Task<SmsRuntimeConfig> LoadAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<ISmsProviderService>().GetRuntimeAsync(cancellationToken);
    }

    /// <summary>شماره در لاگ نصفه نمایش داده می‌شود. مثال: 09121234567 → 0912***4567.</summary>
    public static string Mask(string phone) => phone.Length >= 8 ? phone[..4] + "***" + phone[^4..] : "***";
}
