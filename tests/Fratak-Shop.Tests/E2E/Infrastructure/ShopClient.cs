using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;

namespace Fratak_Shop.Tests.E2E.Infrastructure;

/// <summary>
/// یک «مرورگر» آزمون: کوکی‌ها را نگه می‌دارد، توکن ضد CSRF را مثل shop.js از <c>/api/antiforgery</c> می‌گیرد،
/// فرم می‌فرستد و HTML را با AngleSharp می‌خواند. ریدایرکت‌ها خودکار دنبال نمی‌شوند تا مقصدشان آزموده شود.
/// <para>مثال: <c>await client.LoginAsync("09120000000", "123456"); var page = await client.GetPageAsync("/profile");</c></para>
/// </summary>
public sealed class ShopClient(ShopApp app, HttpClient http, string ip) : IDisposable
{
    private static readonly HtmlParser Parser = new();

    public HttpClient Http { get; } = http;

    public string Ip { get; } = ip;

    public ShopApp App { get; } = app;

    public Task<HttpResponseMessage> GetAsync(string url) => Http.GetAsync(url);

    /// <summary>GET و بررسی کد وضعیت؛ خروجی: سند HTML.</summary>
    public async Task<HtmlPage> GetPageAsync(string url, HttpStatusCode expected = HttpStatusCode.OK)
    {
        var started = DateTimeOffset.UtcNow;
        using var response = await Http.GetAsync(url);
        var html = await response.Content.ReadAsStringAsync();
        if (response.StatusCode != expected)
        {
            var errors = App.Logs.ErrorsSince(started);
            throw new Xunit.Sdk.XunitException(
                $"GET {url}: expected {(int)expected} but got {(int)response.StatusCode} {response.Headers.Location}\n" +
                string.Join("\n", errors.Take(3)) + "\n" + Excerpt(html));
        }
        return new HtmlPage(url, (int)response.StatusCode, html, Parser.ParseDocument(html), response);
    }

    /// <summary>توکن ضد جعل درخواست برای کاربر فعلی (بعد از ورود/خروج باید دوباره گرفته شود؛ این متد هر بار تازه می‌گیرد).</summary>
    public async Task<string> AntiforgeryTokenAsync()
    {
        using var response = await Http.GetAsync("/api/antiforgery");
        response.EnsureSuccessStatusCode();
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        return document.RootElement.GetProperty("token").GetString()!;
    }

    /// <summary>فرم را با توکن ضد CSRF معتبر می‌فرستد.</summary>
    public async Task<HttpResponseMessage> PostFormAsync(string url, params (string Key, string Value)[] fields)
    {
        var token = await AntiforgeryTokenAsync();
        return await PostRawFormAsync(url, fields.Append(("__RequestVerificationToken", token)).ToArray());
    }

    /// <summary>فرم بدون توکن (یا با توکن دلخواه) – برای آزمون‌های حملهٔ CSRF.</summary>
    public Task<HttpResponseMessage> PostRawFormAsync(string url, params (string Key, string Value)[] fields) =>
        Http.PostAsync(url, new FormUrlEncodedContent(fields.Select(f => new KeyValuePair<string, string>(f.Key, f.Value))));

    public Task<HttpResponseMessage> PostJsonAsync<T>(string url, T body) => Http.PostAsJsonAsync(url, body);

    /// <summary>ورود با رمز عبور از فرم واقعی /login؛ موفق = ریدایرکت 302 و کوکی‌های fratak_at/fratak_rt.</summary>
    public async Task LoginAsync(string phone, string password = ShopApp.DemoPassword)
    {
        using var response = await PostFormAsync("/login",
            ("Input.PhoneNumber", phone), ("Input.Password", password), ("Input.RememberMe", "true"));
        var cookies = SetCookies(response);
        if (response.StatusCode != HttpStatusCode.Redirect || !cookies.Any(c => c.StartsWith("fratak_at=", StringComparison.Ordinal)))
        {
            var html = await response.Content.ReadAsStringAsync();
            throw new Xunit.Sdk.XunitException($"Login of {phone} failed: {(int)response.StatusCode} {response.Headers.Location}\n{Excerpt(html)}");
        }
    }

    public async Task LogoutAsync()
    {
        using var response = await PostFormAsync("/logout");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
    }

    /// <summary>ریدایرکت‌ها را تا رسیدن به یک پاسخ غیر 3xx (حداکثر ۸ بار) دنبال می‌کند؛ خروجی: آدرس نهایی و پاسخ.</summary>
    public async Task<(string Url, HttpResponseMessage Response)> FollowAsync(HttpResponseMessage response, string from)
    {
        var url = from;
        for (var i = 0; i < 8 && (int)response.StatusCode is >= 300 and < 400 && response.Headers.Location is { } location; i++)
        {
            var next = location.IsAbsoluteUri ? location : new Uri(new Uri(ShopApp.BaseUrl + url), location);
            if (!string.Equals(next.Host, new Uri(ShopApp.BaseUrl).Host, StringComparison.OrdinalIgnoreCase))
                return (next.ToString(), response);
            url = next.PathAndQuery;
            response.Dispose();
            response = await Http.GetAsync(url);
        }
        return (url, response);
    }

    public static IReadOnlyList<string> SetCookies(HttpResponseMessage response) =>
        response.Headers.TryGetValues("Set-Cookie", out var values) ? values.ToList() : [];

    /// <summary>مقدار یک کوکی از سرآیندهای Set-Cookie یک پاسخ.</summary>
    public static string? CookieValue(HttpResponseMessage response, string name)
    {
        foreach (var cookie in SetCookies(response))
        {
            if (cookie.StartsWith(name + "=", StringComparison.Ordinal))
            {
                var value = cookie[(name.Length + 1)..];
                var end = value.IndexOf(';');
                return end >= 0 ? value[..end] : value;
            }
        }
        return null;
    }

    public static string Excerpt(string html)
    {
        var text = System.Text.RegularExpressions.Regex.Replace(html, "<script[\\s\\S]*?</script>|<style[\\s\\S]*?</style>", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", " ");
        text = System.Text.RegularExpressions.Regex.Replace(text, "\\s+", " ").Trim();
        return text.Length > 1200 ? text[..1200] + "…" : text;
    }

    public void Dispose() => Http.Dispose();
}

/// <summary>صفحهٔ HTML دریافت‌شده + ابزار جست‌وجو در آن.</summary>
public sealed record HtmlPage(string Url, int Status, string Html, IDocument Document, HttpResponseMessage Response)
{
    public string Text => Document.Body?.TextContent ?? "";

    public IElement? One(string selector) => Document.QuerySelector(selector);

    public IReadOnlyList<IElement> All(string selector) => Document.QuerySelectorAll(selector).ToList();

    /// <summary>مقدار value اولین input با این نام (مثلاً Input.AddressId).</summary>
    public string? InputValue(string name) => Document.QuerySelector($"input[name='{name}']")?.GetAttribute("value");

    /// <summary>همهٔ لینک‌های داخلی صفحه (بدون #، بدون mailto/tel).</summary>
    public IReadOnlyList<string> InternalLinks() => Document.QuerySelectorAll("a[href]")
        .Select(a => a.GetAttribute("href") ?? "")
        .Where(h => h.StartsWith('/') && !h.StartsWith("//", StringComparison.Ordinal))
        .Select(h => h.Split('#')[0])
        .Where(h => h.Length > 0)
        .Distinct(StringComparer.Ordinal)
        .ToList();
}
