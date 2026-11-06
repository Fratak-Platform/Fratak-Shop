using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Settings;
using Fratak_Shop.Data.Seeders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تنظیمات سرویس پیامک روی دیتابیس واقعی (دور هشتم): ردیف پیش‌فرض seed، رمزنگاری کلید API (متن خام در جدول نیست)،
/// «خالی = بدون تغییر» و «حذف کلید»، رد ورودی نامعتبر بدون تغییر داده و تازه شدن فوری کش ارسال‌کننده پس از ذخیره.
/// چون جدول یک ردیف دارد، آزمون در پایان مقادیر پیش‌فرض را برمی‌گرداند.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class SmsIntegrationTests(SqlServerFixture db)
{
    private const string Key = "IntegrationKey_0123456789abcdef";

    private static SmsProviderInput Input(string? key = null, bool clear = false, string? line = "30000123456", string? template = "718643",
        string? parameter = "Code", SmsProvider provider = SmsProvider.SmsIr) => new(provider, key, clear, line, template, parameter);

    [SqlFact]
    public async Task Defaults_AreSeeded_WithoutKey()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var row = await context.SmsProviderConfigs.AsNoTracking().SingleAsync();
        Assert.Equal(SmsProvider.SmsIr, row.Provider);
        Assert.Equal(Round8DefaultsSeeder.DefaultSmsOtpTemplateId, row.OtpTemplateId);
        Assert.Equal(Round8DefaultsSeeder.DefaultSmsLineNumber, row.LineNumber);
        Assert.Equal("Code", row.OtpParameterName);

        // اجرای دوباره seed ردیف تکراری نمی‌سازد.
        await Round8DefaultsSeeder.SeedAsync(context);
        Assert.Equal(1, await context.SmsProviderConfigs.IgnoreQueryFilters().CountAsync());
    }

    [SqlFact]
    public async Task Save_EncryptsKey_KeepsOnBlank_ClearsOnRequest_AndRefreshesRuntimeCache()
    {
        await using var scope = db.Scope();
        var sms = scope.ServiceProvider.GetRequiredService<ISmsProviderService>();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        try
        {
            _ = await sms.GetRuntimeAsync(); // کش با مقدار قبلی پر می‌شود

            Assert.True((await sms.SaveAsync(Input(key: "  " + Key + "  ", line: "۳۰۰۰۰۹۹۹۹۹", template: "۱۲۳۴۵", parameter: "#Otp#"))).IsSuccess());
            var stored = await context.SmsProviderConfigs.AsNoTracking().SingleAsync();
            Assert.NotNull(stored.ApiKeyProtected);
            Assert.DoesNotContain(Key, stored.ApiKeyProtected);
            Assert.Equal(3000099999L, stored.LineNumber); // رقم فارسی پذیرفته می‌شود
            Assert.Equal(12345, stored.OtpTemplateId);
            Assert.Equal("Otp", stored.OtpParameterName);

            var runtime = await sms.GetRuntimeAsync(); // کش پس از ذخیره پاک شده
            Assert.Equal(Key, runtime.ApiKey);
            Assert.Equal(12345, runtime.OtpTemplateId);
            Assert.True((await sms.GetAsync()).HasApiKey);

            // کلید خالی = بدون تغییر؛ قالب خالی = ارسال کد با متن از خط.
            Assert.True((await sms.SaveAsync(Input(key: "", template: ""))).IsSuccess());
            runtime = await sms.GetRuntimeAsync();
            Assert.Equal(Key, runtime.ApiKey);
            Assert.Null(runtime.OtpTemplateId);

            // حذف کلید؛ پیام هشدار «کلید ثبت نشده».
            var cleared = await sms.SaveAsync(Input(clear: true));
            Assert.True(cleared.IsSuccess());
            Assert.Contains("کلید", cleared.Message);
            Assert.False((await sms.GetAsync()).HasApiKey);
            Assert.Null((await sms.GetRuntimeAsync()).ApiKey);
        }
        finally
        {
            await sms.SaveAsync(Input(clear: true));
        }
    }

    [SqlTheory]
    [InlineData("bad key with spaces", "30000123456", "718643", "Code")]
    [InlineData(null, "12ab", "718643", "Code")]
    [InlineData(null, "1234", "718643", "Code")]
    [InlineData(null, "30000123456", "0", "Code")]
    [InlineData(null, "30000123456", "99999999999", "Code")]
    [InlineData(null, "30000123456", "718643", "Co de")]
    [InlineData(null, "30000123456", "718643", "<b>")]
    [InlineData(null, "", "", "Code")]
    public async Task Save_RejectsInvalidInput_WithoutChangingData(string? key, string line, string template, string parameter)
    {
        await using var scope = db.Scope();
        var sms = scope.ServiceProvider.GetRequiredService<ISmsProviderService>();
        var before = await sms.GetAsync();

        var result = await sms.SaveAsync(Input(key: key, line: line, template: template, parameter: parameter));

        Assert.False(result.IsSuccess());
        Assert.Equal(before, await sms.GetAsync());
    }

    [SqlFact]
    public async Task LogOnly_AllowsEmptyLineAndTemplate()
    {
        await using var scope = db.Scope();
        var sms = scope.ServiceProvider.GetRequiredService<ISmsProviderService>();
        try
        {
            Assert.True((await sms.SaveAsync(Input(line: "", template: "", provider: SmsProvider.LogOnly))).IsSuccess());
            Assert.Equal(SmsProvider.LogOnly, (await sms.GetRuntimeAsync()).Provider);
        }
        finally
        {
            await sms.SaveAsync(Input(clear: true));
        }
    }
}
