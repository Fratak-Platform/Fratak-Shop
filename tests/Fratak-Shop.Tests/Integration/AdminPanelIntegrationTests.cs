using Fratak_Shop.Core.DTOs.Admin;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class AdminRoleTests(SqlServerFixture db)
{
    private static readonly AdminAccess Owner =
        new(Guid.NewGuid(), "مالک آزمایشی", "09120000009", true, new HashSet<string>(), []);

    private static AdminAccess Staff(Guid userId, params string[] permissions) =>
        new(userId, "کارمند آزمایشی", "09120000001", false, new HashSet<string>(permissions), ["نقش"]);

    private async Task<OperationResult<Guid>> SaveRoleAsync(AdminAccess actor, params string[] permissions)
    {
        await using var scope = db.Scope();
        return await scope.ServiceProvider.GetRequiredService<IAdminAccessService>().SaveRoleAsync(new SaveAdminRoleCommand
        {
            Name = "نقش آزمایشی " + Guid.NewGuid().ToString("N")[..8],
            Permissions = [.. permissions],
        }, actor);
    }

    [Fact]
    public async Task RoleAssignment_GrantsExactlyTheRolePermissions()
    {
        var role = await SaveRoleAsync(Owner, AdminPermissions.OrdersView, AdminPermissions.PurchaseInvoices, "not.a.permission");
        Assert.True(role.IsSuccess(), role.Message);

        var user = await db.CreateUserAsync();
        await using (var scope = db.Scope())
        {
            var access = scope.ServiceProvider.GetRequiredService<IAdminAccessService>();
            Assert.False((await access.GetAccessAsync(user)).IsStaff);

            var assign = await access.SetUserRolesAsync(user, [role.Data], Owner);
            Assert.True(assign.IsSuccess(), assign.Message);
        }

        await using (var scope = db.Scope())
        {
            var granted = await scope.ServiceProvider.GetRequiredService<IAdminAccessService>().GetAccessAsync(user);
            Assert.True(granted.IsStaff);
            Assert.True(granted.Has(AdminPermissions.OrdersView));
            Assert.True(granted.Has(AdminPermissions.PurchaseInvoices));
            Assert.False(granted.Has(AdminPermissions.FinanceView));
            Assert.False(granted.Has("not.a.permission"));

            // Removing the role takes the panel away again.
            var revoke = await scope.ServiceProvider.GetRequiredService<IAdminAccessService>().SetUserRolesAsync(user, [], Owner);
            Assert.True(revoke.IsSuccess(), revoke.Message);
        }

        await using (var scope = db.Scope())
            Assert.False((await scope.ServiceProvider.GetRequiredService<IAdminAccessService>().GetAccessAsync(user)).IsStaff);
    }

    [Fact]
    public async Task Staff_CannotEscalatePermissions()
    {
        var staffUser = await db.CreateUserAsync();
        var actor = Staff(staffUser, AdminPermissions.StaffManage, AdminPermissions.OrdersView);

        Assert.False((await SaveRoleAsync(actor, AdminPermissions.FinanceView)).IsSuccess());
        Assert.True((await SaveRoleAsync(actor, AdminPermissions.OrdersView)).IsSuccess());

        var powerful = await SaveRoleAsync(Owner, AdminPermissions.SettingsManage);
        Assert.True(powerful.IsSuccess(), powerful.Message);

        var target = await db.CreateUserAsync();
        await using var scope = db.Scope();
        var access = scope.ServiceProvider.GetRequiredService<IAdminAccessService>();
        Assert.False((await access.SetUserRolesAsync(target, [powerful.Data], actor)).IsSuccess());
        Assert.False((await access.SetUserRolesAsync(staffUser, [powerful.Data], actor)).IsSuccess()); // not oneself
    }
}

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class PurchaseInvoiceRuleTests(SqlServerFixture db)
{
    private async Task<Guid> CreateConfirmedOrderAsync()
    {
        var user = await db.CreateUserAsync();
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var order = new Order
        {
            UserId = user,
            OrderNumber = "T" + Random.Shared.NextInt64(1_000_000_000, 9_999_999_999),
            Status = OrderStatus.Confirmed,
            PaymentMethod = PaymentMethod.OnlineGateway,
            PaymentStatus = PaymentStatus.Paid,
            PaidAt = DateTimeOffset.UtcNow,
            SubtotalAmount = 500_000,
            PayableAmount = 500_000,
            CustomerName = "مشتری آزمایشی",
            CustomerPhone = "09120000000",
            ShippingProvince = "تهران",
            ShippingCity = "تهران",
            ShippingAddress = "خیابان آزمایش",
            ShippingPostalCode = "1234567890",
            ShippingPhone = "09120000000",
        };
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order.Id;
    }

    [Fact]
    public async Task Preparation_RequiresAnUploadedPurchaseInvoice()
    {
        var orderId = await CreateConfirmedOrderAsync();

        await using (var scope = db.Scope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IAdminOrderService>();
            var blocked = await admin.ChangeStatusAsync(orderId, OrderStatus.Processing, null, null, "آزمون");
            Assert.False(blocked.IsSuccess());
            Assert.Contains("فاکتور خرید", blocked.Message);

            // Shipping straight from "confirmed" would skip preparation – blocked too.
            Assert.False((await admin.ChangeStatusAsync(orderId, OrderStatus.Shipped, null, null, "آزمون")).IsSuccess());

            var invoice = await admin.AddPurchaseInvoiceAsync(new AddPurchaseInvoiceCommand
            {
                OrderId = orderId,
                SupplierName = "تأمین‌کننده آزمایشی",
                InvoiceNumber = "INV-1",
                Amount = 420_000,
                FileName = "test-invoice.pdf",
                ContentType = "application/pdf",
                SizeBytes = 1024,
            });
            Assert.True(invoice.IsSuccess(), invoice.Message);
        }

        await using (var scope = db.Scope())
        {
            var admin = scope.ServiceProvider.GetRequiredService<IAdminOrderService>();
            var moved = await admin.ChangeStatusAsync(orderId, OrderStatus.Processing, "خرید انجام شد", null, "آزمون");
            Assert.True(moved.IsSuccess(), moved.Message);

            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            Assert.Equal(OrderStatus.Processing, await context.Orders.Where(o => o.Id == orderId).Select(o => o.Status).SingleAsync());
            Assert.True(await context.OrderNotes.AnyAsync(n => n.OrderId == orderId && n.Content.Contains("خرید انجام شد")));
        }
    }

    [Fact]
    public async Task PurchaseInvoice_RejectsInvalidAmounts()
    {
        var orderId = await CreateConfirmedOrderAsync();
        await using var scope = db.Scope();
        var admin = scope.ServiceProvider.GetRequiredService<IAdminOrderService>();
        var result = await admin.AddPurchaseInvoiceAsync(new AddPurchaseInvoiceCommand
        {
            OrderId = orderId, Amount = -1, FileName = "x.pdf", ContentType = "application/pdf", SizeBytes = 10,
        });
        Assert.False(result.IsSuccess());
    }
}
