using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// مرحلهٔ هفدهم روی دیتابیس واقعی: مشکلات «مدیر دوم دسترسی ندارد» و «شمارهٔ مالک در تنظیمات عوض شد ولی اعمال نشد».
/// <list type="bullet">
/// <item>دو مدیر با Key تکراری ← هر دو ساخته می‌شوند (دومی با کلید شماره) و هشدار ثبت می‌شود.</item>
/// <item>شمارهٔ تنظیمات عوض شد و شمارهٔ تازه حساب ندارد ← شمارهٔ همان حساب عوض می‌شود (رمز می‌ماند، نشست‌ها بسته می‌شوند).</item>
/// <item>شمارهٔ تازه قبلاً حساب دارد ← همان حساب مدیر اصلی می‌شود و حساب استفاده‌نشدهٔ قبلی از پنل خارج می‌شود.</item>
/// <item>تغییر شماره از پنل (بدون تغییر تنظیمات) با ری‌استارت برگردانده نمی‌شود.</item>
/// </list>
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class Round17DefaultAdminIntegrationTests(SqlServerFixture db)
{
    [SqlFact]
    public async Task DuplicateKeys_StillCreateEveryAdmin_WithWarning()
    {
        var number = Random.Shared.Next(1_000_000, 9_999_998);
        var first = "0904" + number;
        var second = "0904" + (number + 1);
        var key = "dup-test-" + number;
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<IAdminAccessService>();
        try
        {
            var notes = await access.EnsureDefaultsAsync(
            [
                new DefaultAdminAccount(first, "مدیر", "یک", "12345678", Key: key),
                new DefaultAdminAccount(second, "مدیر", "دو", "12345678", Key: key),
            ]);

            var one = await context.Users.AsNoTracking().SingleAsync(u => u.PhoneNumber == first);
            var two = await context.Users.AsNoTracking().SingleAsync(u => u.PhoneNumber == second);
            Assert.Equal("key:" + key, one.SystemAdminKey);
            Assert.Equal("phone:" + second, two.SystemAdminKey);
            Assert.Equal(UserRole.SuperAdmin, two.Role);
            Assert.True((await access.GetAccessAsync(two.Id)).IsOwner);
            Assert.Contains(notes, n => n.StartsWith("هشدار", StringComparison.Ordinal) && n.Contains("تکراری", StringComparison.Ordinal));
            Assert.All(notes, n => Assert.DoesNotContain(second, n, StringComparison.Ordinal));

            // اجرای دوباره (ری‌استارت) چیزی تکراری نمی‌سازد.
            await access.EnsureDefaultsAsync(
            [
                new DefaultAdminAccount(first, "مدیر", "یک", "12345678", Key: key),
                new DefaultAdminAccount(second, "مدیر", "دو", "12345678", Key: key),
            ]);
            Assert.Equal(1, await context.Users.IgnoreQueryFilters().CountAsync(u => u.PhoneNumber == second));
        }
        finally
        {
            await CleanupAsync(context, [first, second], ["key:" + key, "phone:" + second]);
        }
    }

    [SqlFact]
    public async Task ConfiguredPhoneChange_IsApplied_ButPanelChangeIsKept()
    {
        var number = Random.Shared.Next(1_000_000, 9_999_997);
        var oldPhone = "0905" + number;
        var newPhone = "0905" + (number + 1);
        var panelPhone = "0905" + (number + 2);
        var key = "phone-test-" + number;
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<IAdminAccessService>();
        try
        {
            await access.EnsureDefaultsAsync([new DefaultAdminAccount(oldPhone, "مالک", "آزمون", "12345678", Key: key)]);
            var owner = await context.Users.AsNoTracking().SingleAsync(u => u.PhoneNumber == oldPhone);
            Assert.Equal(oldPhone, owner.SystemAdminPhone);

            // مالک رمزش را عوض کرده بود؛ بعد صاحب سرور شمارهٔ درست را در appsettings گذاشت.
            var tracked = await context.Users.AsTracking().SingleAsync(u => u.Id == owner.Id);
            tracked.PasswordHash = PasswordHasher.Hash("owner-own-pass");
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            var notes = await access.EnsureDefaultsAsync([new DefaultAdminAccount(newPhone, "مالک", "آزمون", "12345678", Key: key)]);
            var moved = await context.Users.AsNoTracking().SingleAsync(u => u.Id == owner.Id);
            Assert.Equal(newPhone, moved.PhoneNumber);
            Assert.Equal(newPhone, moved.SystemAdminPhone);
            Assert.True(PasswordHasher.Verify("owner-own-pass", moved.PasswordHash));
            Assert.NotEqual(owner.SecurityStamp, moved.SecurityStamp); // نشست‌های شمارهٔ قبلی بسته شد
            Assert.Equal(UserRole.SuperAdmin, moved.Role);
            Assert.Contains(notes, n => n.Contains("اصلاح شد", StringComparison.Ordinal));
            Assert.False(await context.Users.AnyAsync(u => u.PhoneNumber == oldPhone));

            // حالا خود مالک از پنل شماره‌اش را عوض کرد؛ تنظیمات همان است ← ری‌استارت شماره را برنمی‌گرداند.
            tracked = await context.Users.AsTracking().SingleAsync(u => u.Id == owner.Id);
            tracked.PhoneNumber = panelPhone;
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();
            await access.EnsureDefaultsAsync([new DefaultAdminAccount(newPhone, "مالک", "آزمون", "12345678", Key: key)]);
            var kept = await context.Users.AsNoTracking().SingleAsync(u => u.Id == owner.Id);
            Assert.Equal(panelPhone, kept.PhoneNumber);
            Assert.Equal(1, await context.Users.IgnoreQueryFilters().CountAsync(u => u.SystemAdminKey == "key:" + key));
        }
        finally
        {
            await CleanupAsync(context, [oldPhone, newPhone, panelPhone], ["key:" + key]);
        }
    }

    [SqlFact]
    public async Task ConfiguredPhoneOfExistingCustomer_BecomesTheAdmin_AndUnusedOldAccountIsDemoted()
    {
        var number = Random.Shared.Next(1_000_000, 9_999_998);
        var wrongPhone = "0906" + number;
        var realPhone = "0906" + (number + 1);
        var key = "relink-test-" + number;
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var access = scope.ServiceProvider.GetRequiredService<IAdminAccessService>();
        try
        {
            // حساب مدیر با شمارهٔ اشتباه ساخته شد (هرگز وارد نشد)؛ صاحب واقعی شماره با کد پیامکی ثبت‌نام کرده بود.
            await access.EnsureDefaultsAsync([new DefaultAdminAccount(wrongPhone, "شریک", "آزمون", "12345678", Key: key)]);
            context.Users.Add(new User { PhoneNumber = realPhone, IsPhoneVerified = true, SecurityStamp = PasswordHasher.NewSecurityStamp() });
            await context.SaveChangesAsync();
            context.ChangeTracker.Clear();

            await access.EnsureDefaultsAsync([new DefaultAdminAccount(realPhone, "شریک", "آزمون", "12345678", Key: key)]);

            var real = await context.Users.AsNoTracking().SingleAsync(u => u.PhoneNumber == realPhone);
            Assert.Equal("key:" + key, real.SystemAdminKey);
            Assert.Equal(realPhone, real.SystemAdminPhone);
            Assert.Equal(UserRole.SuperAdmin, real.Role);
            Assert.True(PasswordHasher.Verify("12345678", real.PasswordHash)); // حساب بی‌رمز، رمز اولیه گرفت
            Assert.True((await access.GetAccessAsync(real.Id)).IsOwner);

            var wrong = await context.Users.AsNoTracking().SingleAsync(u => u.PhoneNumber == wrongPhone);
            Assert.Null(wrong.SystemAdminKey);
            Assert.Null(wrong.SystemAdminPhone);
            Assert.Equal(UserRole.Customer, wrong.Role); // با رمز عمومی دیگر کسی وارد پنل نمی‌شود
            Assert.False(await context.UserAdminRoles.AnyAsync(m => m.UserId == wrong.Id));
        }
        finally
        {
            await CleanupAsync(context, [wrongPhone, realPhone], ["key:" + key]);
        }
    }

    private static async Task CleanupAsync(FratakShopDbContext context, string[] phones, string[] keys)
    {
        context.ChangeTracker.Clear();
        foreach (var user in await context.Users.IgnoreQueryFilters().AsTracking()
                     .Where(u => phones.Contains(u.PhoneNumber) || (u.SystemAdminKey != null && keys.Contains(u.SystemAdminKey))).ToListAsync())
        {
            user.SystemAdminKey = null;
            user.SystemAdminPhone = null;
            context.Users.Remove(user);
        }
        await context.SaveChangesAsync();
        context.ChangeTracker.Clear();
    }
}
