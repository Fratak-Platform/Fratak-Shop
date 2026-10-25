using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست‌های مرحلهٔ هفتم – فاز ۱ (اعلان زنده، قالب پیام با برچسب، پیام‌های آماده) روی دیتابیس واقعی.
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class RealtimeNotificationTests(SqlServerFixture db)
{
    /// <summary>
    /// هیچ قالب پیش‌فرضی نباید برچسبی داشته باشد که برای رویدادش تعریف نشده
    /// (وگرنه در پیام واقعی خالی می‌ماند و ویرایشگر قالب هم اجازهٔ ذخیره نمی‌دهد).
    /// </summary>
    [Fact]
    public async Task SeededTemplates_UseOnlyKnownPlaceholders()
    {
        var userId = await db.CreateUserAsync();
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var templates = await context.NotificationTemplates.ToListAsync();
        Assert.NotEmpty(templates);
        foreach (var template in templates)
        {
            var preview = await notifications.PreviewAsync(template.Event, userId, template.Title, template.Body, template.SmsText);
            Assert.True(preview.UnknownKeys.Count == 0, $"{template.Event}: {string.Join(", ", preview.UnknownKeys)}");
            Assert.DoesNotContain("{", preview.Title);
            Assert.DoesNotContain("{", preview.Body);
        }
    }

    [Fact]
    public async Task Preview_FillsCustomerPlaceholders_AndReportsTypos()
    {
        var userId = await db.CreateUserAsync();
        await using var scope = db.Scope();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var preview = await notifications.PreviewAsync(NotificationEvent.OrderShipped, userId,
            "سلام {FirstName}", "کد رهگیری: {TrackingCode} – {OrderNumbr}", "{SiteName}");

        Assert.Equal("سلام کاربر", preview.Title);
        Assert.Contains("کد رهگیری:", preview.Body);
        Assert.Equal(["OrderNumbr"], preview.UnknownKeys);
        Assert.False(string.IsNullOrWhiteSpace(preview.Sms));
    }

    [Fact]
    public async Task RenderForUser_ReplacesKnownKeys_AndKeepsUnknownBraces()
    {
        var userId = await db.CreateUserAsync();
        await using var scope = db.Scope();
        var notifications = scope.ServiceProvider.GetRequiredService<INotificationService>();

        var text = await notifications.RenderForUserAsync(userId, "سلام {FirstName}، تیکت {TicketNumber} – {not-a-key} {Other}",
            new Dictionary<string, string?> { ["TicketNumber"] = "T-100" });

        Assert.Equal("سلام کاربر، تیکت T-100 – {not-a-key} {Other}", text);
    }

    [Fact]
    public async Task StaffNotifications_CountMuteAndMarkRead()
    {
        var userId = await db.CreateUserAsync();
        await using var scope = db.Scope();
        var staff = scope.ServiceProvider.GetRequiredService<IStaffNotificationService>();

        await staff.NotifyUserAsync(userId, StaffNotificationKind.NewTicket, "تیکت جدید T-1", "متن", "/admin/tickets");
        await staff.NotifyUserAsync(userId, StaffNotificationKind.NewOrder, "سفارش جدید", null, "https://evil.example/");
        Assert.Equal(2, await staff.GetUnreadCountAsync(userId));

        var latest = await staff.GetLatestAsync(userId);
        Assert.Equal(2, latest.Count);
        // لینک خارجی هرگز در اعلان ذخیره نمی‌شود.
        Assert.Null(latest.Single(n => n.Kind == StaffNotificationKind.NewOrder).Url);

        // نوع بی‌صدا شده دیگر تحویل داده نمی‌شود.
        await staff.SetMutedKindsAsync(userId, [StaffNotificationKind.NewTicket]);
        Assert.Contains(StaffNotificationKind.NewTicket, await staff.GetMutedKindsAsync(userId));
        await staff.NotifyUserAsync(userId, StaffNotificationKind.NewTicket, "تیکت جدید T-2", null, null);
        Assert.Equal(2, await staff.GetUnreadCountAsync(userId));

        await staff.MarkReadAsync(userId, latest[0].Id);
        Assert.Equal(1, await staff.GetUnreadCountAsync(userId));
        await staff.MarkAllReadAsync(userId);
        Assert.Equal(0, await staff.GetUnreadCountAsync(userId));

        var page = await staff.GetPageAsync(userId, 1, 30, unreadOnly: true);
        Assert.Empty(page.Items);
    }

    [Fact]
    public async Task StaffNotifications_CannotBeReadByAnotherUser()
    {
        var owner = await db.CreateUserAsync();
        var other = await db.CreateUserAsync();
        await using var scope = db.Scope();
        var staff = scope.ServiceProvider.GetRequiredService<IStaffNotificationService>();

        await staff.NotifyUserAsync(owner, StaffNotificationKind.System, "آزمایش", null, null);
        var item = (await staff.GetLatestAsync(owner)).Single();

        await staff.MarkReadAsync(other, item.Id);
        Assert.Equal(1, await staff.GetUnreadCountAsync(owner));
        Assert.Empty(await staff.GetLatestAsync(other));
    }

    [Fact]
    public async Task ReadyMessages_ValidateAndFilterByScope()
    {
        await using var scope = db.Scope();
        var ready = scope.ServiceProvider.GetRequiredService<IReadyMessageService>();
        var name = "پیام آزمایشی " + Guid.NewGuid().ToString("N")[..6];

        Assert.False((await ready.SaveAsync(new SaveReadyMessageCommand { Name = name, Body = "متن", Scopes = ReadyMessageScope.None })).IsSuccess());
        Assert.False((await ready.SaveAsync(new SaveReadyMessageCommand { Name = name, Body = "متن", LinkUrl = "javascript:alert(1)" })).IsSuccess());
        Assert.False((await ready.SaveAsync(new SaveReadyMessageCommand { Name = name, Body = "" })).IsSuccess());

        var saved = await ready.SaveAsync(new SaveReadyMessageCommand { Name = name, Body = "سلام {FirstName}", Scopes = ReadyMessageScope.Ticket, LinkUrl = "/shop" });
        Assert.True(saved.IsSuccess(), saved.Message);

        Assert.Contains(await ready.ListAsync(ReadyMessageScope.Ticket), m => m.Name == name);
        Assert.DoesNotContain(await ready.ListAsync(ReadyMessageScope.Broadcast), m => m.Name == name);

        var item = (await ready.AllAsync()).Single(m => m.Name == name);
        await ready.MarkUsedAsync(item.Id);
        Assert.Equal(1, (await ready.AllAsync()).Single(m => m.Name == name).UsageCount);

        Assert.True((await ready.DeleteAsync(item.Id)).IsSuccess());
        Assert.DoesNotContain(await ready.AllAsync(), m => m.Name == name);
    }

    [Fact]
    public async Task DefaultReadyMessages_AreSeeded()
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        Assert.True(await context.ReadyMessages.IgnoreQueryFilters().CountAsync() >= 4);
    }

    [Fact]
    public async Task AdminPreferences_RoundTripPerUser()
    {
        var userId = await db.CreateUserAsync();
        var otherId = await db.CreateUserAsync();
        await using var scope = db.Scope();
        var preferences = scope.ServiceProvider.GetRequiredService<IAdminPreferenceService>();

        Assert.True(await preferences.SetAsync(userId, "pins", new[] { "/admin/orders", "/admin/tickets" }));
        Assert.Equal(["/admin/orders", "/admin/tickets"], await preferences.GetAsync<string[]>(userId, "pins"));
        Assert.Null(await preferences.GetAsync<string[]>(otherId, "pins"));

        Assert.True(await preferences.SetAsync(userId, "pins", new[] { "/admin" }));
        Assert.Equal(["/admin"], await preferences.GetAsync<string[]>(userId, "pins"));

        await preferences.RemoveAsync(userId, "pins");
        Assert.Null(await preferences.GetAsync<string[]>(userId, "pins"));
        Assert.True(await preferences.SetAsync(userId, "pins", new[] { "/admin/blog" }));
        Assert.False(await preferences.SetAsync(userId, new string('k', 151), 1));
    }
}
