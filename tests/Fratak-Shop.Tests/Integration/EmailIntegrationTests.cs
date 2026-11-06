using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Settings;
using Fratak_Shop.Data.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تنظیمات سرویس ایمیل روی دیتابیس واقعی (مرحلهٔ دوازدهم): ردیف پیش‌فرض «فقط لاگ»، رمزنگاری رمز SMTP،
/// «رمز خالی = بدون تغییر» و «حذف رمز»، حذف فاصله‌های App Password جیمیل، رد ورودی نامعتبر بدون تغییر داده
/// و تازه شدن فوری کش ارسال. جدول یک ردیف دارد؛ آزمون در پایان مقادیر پیش‌فرض را برمی‌گرداند.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class EmailIntegrationTests(SqlServerFixture db)
{
    private static EmailProviderInput Input(EmailMode mode = EmailMode.Smtp, string? host = "smtp.gmail.com", string? port = "587",
        EmailSecurity security = EmailSecurity.Auto, string? user = "shop@gmail.com", string? password = null, bool clear = false,
        string? from = "shop@gmail.com", string? fromName = "فراتک", string? replyTo = null, string? baseUrl = "https://shop.example",
        string? cap = "100", string? preset = "gmail") =>
        new(mode, preset, host, port, security, user, password, clear, from, fromName, replyTo, baseUrl, cap);

    private static EmailProviderInput Defaults() =>
        new(EmailMode.LogOnly, null, null, "587", EmailSecurity.Auto, null, null, true, null, null, null, null,
            Round12DefaultsSeeder.DefaultEmailMaxPerHour.ToString());

    [SqlFact]
    public async Task Defaults_AreSeeded_InLogOnlyMode()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var row = await context.EmailProviderConfigs.AsNoTracking().SingleAsync();
        Assert.Equal(EmailMode.LogOnly, row.Mode);
        Assert.Equal(587, row.Port);
        Assert.Null(row.PasswordProtected);

        await Round12DefaultsSeeder.SeedAsync(context);
        Assert.Equal(1, await context.EmailProviderConfigs.IgnoreQueryFilters().CountAsync());
    }

    [SqlFact]
    public async Task Save_EncryptsPassword_KeepsOnBlank_ClearsOnRequest_AndRefreshesRuntimeCache()
    {
        await using var scope = db.Scope();
        var email = scope.ServiceProvider.GetRequiredService<IEmailProviderService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        try
        {
            _ = await email.GetRuntimeAsync(); // کش با مقدار قبلی پر می‌شود

            var saved = await email.SaveAsync(Input(host: " SMTP.Gmail.com. ", password: "abcd efgh ijkl mnop", fromName: "فراتک \"<شاپ>\"", cap: "۲۰۰"), null);
            Assert.True(saved.IsSuccess(), saved.Message);
            Assert.Contains("ایمیل آزمایشی", saved.Message);

            var stored = await context.EmailProviderConfigs.AsNoTracking().SingleAsync();
            Assert.Equal("smtp.gmail.com", stored.Host);
            Assert.Equal("gmail", stored.Preset);
            Assert.Equal(200, stored.MaxPerHour);
            Assert.Equal("فراتک شاپ", stored.FromName);
            Assert.NotNull(stored.PasswordProtected);
            Assert.DoesNotContain("abcdefghijklmnop", stored.PasswordProtected);

            var runtime = await email.GetRuntimeAsync(); // کش پس از ذخیره پاک شده
            Assert.Equal(EmailMode.Smtp, runtime.Mode);
            Assert.Equal("abcdefghijklmnop", runtime.Password); // فاصله‌های App Password جیمیل حذف می‌شود
            Assert.Equal("پنل مدیریت", runtime.PasswordSource);
            Assert.True(runtime.IsSmtpReady);
            Assert.True((await email.GetAsync()).HasPassword);

            // رمز خالی = بدون تغییر.
            Assert.True((await email.SaveAsync(Input(password: ""), null)).IsSuccess());
            Assert.Equal("abcdefghijklmnop", (await email.GetRuntimeAsync()).Password);

            // حذف رمز → هشدار «رمز ثبت نشده».
            var cleared = await email.SaveAsync(Input(clear: true), null);
            Assert.True(cleared.IsSuccess());
            Assert.Contains("رمز SMTP ثبت نشده", cleared.Message);
            Assert.False((await email.GetAsync()).HasPassword);
            Assert.Null((await email.GetRuntimeAsync()).Password);
        }
        finally
        {
            await email.SaveAsync(Defaults(), null);
        }
    }

    [SqlFact]
    public async Task Save_RejectsInvalidInput_WithoutChangingData()
    {
        await using var scope = db.Scope();
        var email = scope.ServiceProvider.GetRequiredService<IEmailProviderService>();
        var before = await email.GetAsync();

        var invalid = new[]
        {
            Input(host: "https://smtp.gmail.com"),
            Input(host: null),
            Input(host: "smtp.gmail.com/x"),
            Input(port: "70000"),
            Input(port: "abc"),
            Input(from: null),
            Input(from: "not-an-email"),
            Input(from: "a@b.co, c@d.co"),
            Input(replyTo: "x"),
            Input(baseUrl: "ftp://shop.example"),
            Input(baseUrl: "https://shop.example/?q=1"),
            Input(cap: "-1"),
            Input(cap: "20000"),
            Input(password: "line1\nline2"),
            Input(mode: (EmailMode)99),
            Input(security: (EmailSecurity)99),
        };
        foreach (var input in invalid)
        {
            var result = await email.SaveAsync(input, null);
            Assert.False(result.IsSuccess(), $"{input} should be rejected");
            Assert.False(string.IsNullOrWhiteSpace(result.Message));
        }

        Assert.Equal(before, await email.GetAsync());
    }

    [SqlFact]
    public async Task Save_UsesRequestUrl_WhenSiteUrlIsBlank_AndAllowsLogOnlyWithoutServer()
    {
        await using var scope = db.Scope();
        var email = scope.ServiceProvider.GetRequiredService<IEmailProviderService>();
        try
        {
            var result = await email.SaveAsync(Input(mode: EmailMode.LogOnly, host: null, from: null, user: null, baseUrl: " ", cap: "0", preset: null),
                "https://Shop.Example/");
            Assert.True(result.IsSuccess(), result.Message);
            var view = await email.GetAsync();
            Assert.Equal("https://shop.example", view.SiteBaseUrl);
            Assert.Equal(0, view.MaxPerHour);
            Assert.Null(view.Preset);
            Assert.False((await email.GetRuntimeAsync()).IsSmtpReady);
        }
        finally
        {
            await email.SaveAsync(Defaults(), null);
        }
    }
}
