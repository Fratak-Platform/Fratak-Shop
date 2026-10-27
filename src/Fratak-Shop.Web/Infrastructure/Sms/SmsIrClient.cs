using System.Net.Http.Json;
using System.Text.Json;

namespace Fratak_Shop.Web.Infrastructure.Sms;

/// <summary>
/// پاسخ یکسان همهٔ درخواست‌های SMS.ir. Ok = HTTP موفق و status == 1.
/// Data همان بخش «data» پاسخ است (مثلاً اعتبار حساب یا شناسهٔ پیام).
/// </summary>
public sealed record SmsIrResponse(bool Ok, int? HttpStatus, int? Status, string Message, JsonElement? Data = null);

/// <summary>
/// کلاینت REST سامانهٔ پیامکی SMS.ir (نسخهٔ ۱). همان مسیرها و بدنه‌های کتابخانهٔ رسمی IPE.SmsIrClient:
/// <list type="bullet">
/// <item><c>POST send/verify</c> — ارسال سریع با قالب: <c>{"Mobile","TemplateId","Parameters":[{"Name","Value"}]}</c></item>
/// <item><c>POST send/bulk</c> — پیامک متنی از خط: <c>{"LineNumber","MessageText","Mobiles":[...],"SendDateTime":null}</c></item>
/// <item><c>GET credit</c> — اعتبار حساب؛ <c>GET line</c> — خط‌های حساب.</item>
/// </list>
/// کلید API با سرآیند <c>x-api-key</c> فقط روی همان درخواست گذاشته می‌شود (نه روی HttpClient مشترک) و هرگز لاگ نمی‌شود.
/// برخلاف کتابخانهٔ رسمی، هیچ متدی استثنا نمی‌دهد؛ خطای شبکه/سرویس به‌صورت Ok=false با پیام فارسی برمی‌گردد.
/// محل استفاده: <see cref="ConfigurableSmsSender"/> و صفحهٔ پنل «سرویس پیامک» (آزمون اتصال).
/// </summary>
public sealed class SmsIrClient(HttpClient http)
{
    public const string ClientName = "sms-ir";
    public const string BaseAddress = "https://api.sms.ir/v1/";

    // JsonContent به‌طور پیش‌فرض camelCase می‌سازد؛ کتابخانهٔ رسمی بدون سیاست نام‌گذاری (PascalCase) می‌فرستد.
    private static readonly JsonSerializerOptions OfficialJson = new();

    /// <summary>تنظیم HttpClient نام‌دار (WebServiceCollectionExtensions).</summary>
    public static void Configure(HttpClient client)
    {
        client.BaseAddress = new Uri(BaseAddress);
        client.Timeout = TimeSpan.FromSeconds(15);
        client.DefaultRequestHeaders.Accept.ParseAdd("application/json");
        client.DefaultRequestHeaders.UserAgent.ParseAdd("Fratak-Shop/1.0");
    }

    /// <summary>ارسال کد با قالب. مثال: VerifyAsync(key, "09121234567", 718643, [("Code", "12345")]).</summary>
    public Task<SmsIrResponse> VerifyAsync(string apiKey, string mobile, int templateId, IReadOnlyList<(string Name, string Value)> parameters, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "send/verify", apiKey, new
        {
            Mobile = mobile,
            TemplateId = templateId,
            Parameters = parameters.Select(p => new { p.Name, p.Value }).ToArray(),
        }, cancellationToken);

    /// <summary>ارسال متن از خط. مثال: BulkAsync(key, 30000123456, "سفارش شما ارسال شد", ["09121234567"]).</summary>
    public Task<SmsIrResponse> BulkAsync(string apiKey, long lineNumber, string text, IReadOnlyList<string> mobiles, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Post, "send/bulk", apiKey, new
        {
            LineNumber = lineNumber,
            MessageText = text,
            Mobiles = mobiles.ToArray(),
            SendDateTime = (int?)null,
        }, cancellationToken);

    /// <summary>اعتبار حساب (Data عددی). برای آزمون درستی کلید در پنل.</summary>
    public Task<SmsIrResponse> CreditAsync(string apiKey, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, "credit", apiKey, null, cancellationToken);

    /// <summary>خط‌های حساب (Data آرایهٔ عدد).</summary>
    public Task<SmsIrResponse> LinesAsync(string apiKey, CancellationToken cancellationToken = default) =>
        SendAsync(HttpMethod.Get, "line", apiKey, null, cancellationToken);

    private async Task<SmsIrResponse> SendAsync(HttpMethod method, string path, string apiKey, object? body, CancellationToken cancellationToken)
    {
        // درخواستی که کاربرش رفته (مثلاً صفحه بسته شد) اصلاً فرستاده نمی‌شود؛ لغو به فراخواننده می‌رسد، نه «خطای سرویس».
        cancellationToken.ThrowIfCancellationRequested();
        try
        {
            using var request = new HttpRequestMessage(method, path);
            request.Headers.TryAddWithoutValidation("x-api-key", apiKey);
            if (body is not null)
                request.Content = JsonContent.Create(body, options: OfficialJson); // نام ویژگی‌ها PascalCase، مثل کتابخانهٔ رسمی

            using var response = await http.SendAsync(request, cancellationToken);
            var text = await response.Content.ReadAsStringAsync(cancellationToken);
            return Parse((int)response.StatusCode, response.IsSuccessStatusCode, text);
        }
        catch (Exception exception) when (exception is not OperationCanceledException || !cancellationToken.IsCancellationRequested)
        {
            var reason = exception is TaskCanceledException ? "مهلت پاسخ SMS.ir تمام شد" : "ارتباط با SMS.ir برقرار نشد";
            return new SmsIrResponse(false, null, null, $"{reason} ({exception.GetType().Name}).");
        }
    }

    /// <summary>
    /// خواندن پاسخ <c>{"status":1,"message":"موفق","data":...}</c>. پاسخ غیر JSON یا بدون status هم بدون استثنا ناموفق می‌شود.
    /// مثال: (200, «{"status":1,"message":"موفق","data":{"messageId":1}}») → Ok؛ (401, «{"status":0,"message":"کلید نامعتبر"}») → ناموفق.
    /// </summary>
    public static SmsIrResponse Parse(int httpStatus, bool httpSuccess, string? body)
    {
        int? status = null;
        string? message = null;
        JsonElement? data = null;
        try
        {
            if (!string.IsNullOrWhiteSpace(body))
            {
                using var document = JsonDocument.Parse(body);
                var root = document.RootElement;
                if (root.ValueKind == JsonValueKind.Object)
                {
                    if (TryGet(root, "status", out var s) && s.ValueKind == JsonValueKind.Number && s.TryGetInt32(out var code))
                        status = code;
                    if (TryGet(root, "message", out var m) && m.ValueKind == JsonValueKind.String)
                        message = m.GetString();
                    if (TryGet(root, "data", out var d) && d.ValueKind is not (JsonValueKind.Null or JsonValueKind.Undefined))
                        data = d.Clone();
                }
            }
        }
        catch (JsonException)
        {
            // پاسخ HTML/متنی (مثلاً خطای پراکسی) → پایین‌تر پیام عمومی.
        }

        var ok = httpSuccess && status == 1;
        if (ok)
            return new SmsIrResponse(true, httpStatus, status, message ?? "موفق", data);

        var explained = httpStatus switch
        {
            401 => "کلید API نامعتبر یا غیرفعال است",
            403 => "دسترسی این کلید به این عملیات بسته است (IP مجاز یا مجوز کلید را در پنل SMS.ir بررسی کنید)",
            429 => "تعداد درخواست‌ها بیش از حد مجاز SMS.ir است",
            >= 500 => "خطای داخلی سرویس SMS.ir",
            _ => "درخواست توسط SMS.ir رد شد",
        };
        return new SmsIrResponse(false, httpStatus, status, string.IsNullOrWhiteSpace(message) ? $"{explained} (HTTP {httpStatus})." : $"{explained}: {message}", data);
    }

    // SMS.ir نام‌ها را camelCase می‌فرستد؛ برای اطمینان بدون حساسیت به حروف خوانده می‌شود.
    private static bool TryGet(JsonElement obj, string name, out JsonElement value)
    {
        foreach (var property in obj.EnumerateObject())
        {
            if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase))
            {
                value = property.Value;
                return true;
            }
        }
        value = default;
        return false;
    }
}
