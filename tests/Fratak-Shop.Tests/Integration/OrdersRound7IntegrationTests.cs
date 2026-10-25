using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Orders;
using Fratak_Shop.Core.Services.Wallets;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Orders;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

/// <summary>
/// تست‌های مرحلهٔ هفتم – فاز ۴ روی دیتابیس واقعی:
/// پیام «کالا ناموجود است» با حذف کالا و بازگشت مابه‌التفاوت به کیف پول، لغو وقتی تنها کالا حذف شود،
/// پس گرفتن سؤال، صفحهٔ عمومی لینک پرداخت و CRM مشتری (برچسب، یادداشت، RFM).
/// </summary>
[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class OrdersRound7IntegrationTests(SqlServerFixture db)
{
    private const string Staff = "کارمند آزمایشی";

    /// <summary>
    /// ساخت سفارش مستقیم در دیتابیس با اقلام دلخواه (قیمت واحد × تعداد) و هزینهٔ ارسال ۵۰ هزار تومان.
    /// </summary>
    private async Task<(Guid OrderId, List<Guid> ItemIds)> CreateOrderAsync(Guid userId, PaymentStatus payment, params (decimal Price, int Qty)[] lines)
    {
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        const decimal shipping = 50_000;
        var subtotal = lines.Sum(l => l.Price * l.Qty);
        var order = new Order
        {
            UserId = userId,
            OrderNumber = "T" + Random.Shared.NextInt64(1_000_000_000, 9_999_999_999),
            Status = OrderStatus.Confirmed,
            PaymentMethod = PaymentMethod.OnlineGateway,
            PaymentStatus = payment,
            PaidAt = payment == PaymentStatus.Paid ? DateTimeOffset.UtcNow : null,
            SubtotalAmount = subtotal,
            ShippingAmount = shipping,
            PayableAmount = subtotal + shipping,
            CustomerName = "مشتری آزمایشی",
            CustomerPhone = "09120000000",
            ShippingProvince = "تهران",
            ShippingCity = "تهران",
            ShippingAddress = "خیابان آزمایش",
            ShippingPostalCode = "1234567890",
            ShippingPhone = "09120000000",
        };
        var index = 0;
        foreach (var (price, qty) in lines)
        {
            index++;
            order.Items.Add(new OrderItem
            {
                ProductName = $"کالای آزمایشی {index}", SKU = $"T-{index}", UnitPrice = price, Quantity = qty, TotalAmount = price * qty,
            });
        }
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return (order.Id, order.Items.Select(i => i.Id).ToList());
    }

    private async Task<Guid> AskAsync(Guid orderId, Guid itemId)
    {
        await using var scope = db.Scope();
        var messages = scope.ServiceProvider.GetRequiredService<IOrderMessageService>();
        var asked = await messages.AskItemUnavailableAsync(orderId, itemId, null, zeroStock: false, null, Staff);
        Assert.True(asked.IsSuccess(), asked.Message);
        // سؤال دوم برای همان کالا تا وقتی پاسخ نگرفته مجاز نیست.
        Assert.False((await messages.AskItemUnavailableAsync(orderId, itemId, null, false, null, Staff)).IsSuccess());
        var list = await messages.ListAsync(orderId);
        var question = Assert.Single(list, m => m.IsOpenQuestion);
        return question.Id;
    }

    [Fact]
    public async Task RemoveItem_RecalculatesOrder_AndRefundsPaidDifferenceToWallet()
    {
        var userId = await db.CreateUserAsync();
        var (orderId, items) = await CreateOrderAsync(userId, PaymentStatus.Paid, (120_000, 1), (190_000, 2));
        var questionId = await AskAsync(orderId, items[0]);

        await using (var scope = db.Scope())
        {
            var messages = scope.ServiceProvider.GetRequiredService<IOrderMessageService>();
            // مشتری دیگر نمی‌تواند به سؤال سفارش این مشتری پاسخ دهد.
            var stranger = await db.CreateUserAsync();
            Assert.False((await messages.DecideAsync(orderId, questionId, stranger, OrderMessageDecision.RemoveItem)).IsSuccess());

            var decided = await messages.DecideAsync(orderId, questionId, userId, OrderMessageDecision.RemoveItem);
            Assert.True(decided.IsSuccess(), decided.Message);
            Assert.Contains("کیف پول", decided.Message);

            // کلیک دوباره اثری ندارد.
            Assert.False((await messages.DecideAsync(orderId, questionId, userId, OrderMessageDecision.RemoveItem)).IsSuccess());
        }

        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            var order = await context.Orders.Include(o => o.Items).FirstAsync(o => o.Id == orderId);
            Assert.Single(order.Items);
            Assert.Equal(380_000m, order.SubtotalAmount);
            Assert.Equal(430_000m, order.PayableAmount); // ۳۸۰ هزار کالا + ۵۰ هزار ارسال
            Assert.Equal(OrderStatus.Confirmed, order.Status);

            var wallet = scope.ServiceProvider.GetRequiredService<IWalletService>();
            Assert.Equal(120_000m, await wallet.GetBalanceAsync(userId));

            var messages = scope.ServiceProvider.GetRequiredService<IOrderMessageService>();
            var question = (await messages.ListAsync(orderId)).Single(m => m.Id == questionId);
            Assert.Equal(OrderMessageDecision.RemoveItem, question.Decision);
            Assert.NotNull(question.DecidedAt);
        }
    }

    [Fact]
    public async Task RemoveItem_OnUnpaidOrder_OnlyLowersTheAmount()
    {
        var userId = await db.CreateUserAsync();
        var (orderId, items) = await CreateOrderAsync(userId, PaymentStatus.Pending, (100_000, 1), (200_000, 1));
        var questionId = await AskAsync(orderId, items[1]);

        await using (var scope = db.Scope())
        {
            var decided = await scope.ServiceProvider.GetRequiredService<IOrderMessageService>()
                .DecideAsync(orderId, questionId, userId, OrderMessageDecision.RemoveItem);
            Assert.True(decided.IsSuccess(), decided.Message);
        }

        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            Assert.Equal(150_000m, await context.Orders.Where(o => o.Id == orderId).Select(o => o.PayableAmount).FirstAsync());
            Assert.Equal(0m, await scope.ServiceProvider.GetRequiredService<IWalletService>().GetBalanceAsync(userId));
        }
    }

    [Fact]
    public async Task RemovingTheOnlyItem_CancelsTheWholeOrder()
    {
        var userId = await db.CreateUserAsync();
        var (orderId, items) = await CreateOrderAsync(userId, PaymentStatus.Pending, (99_000, 1));
        var questionId = await AskAsync(orderId, items[0]);

        await using (var scope = db.Scope())
        {
            var decided = await scope.ServiceProvider.GetRequiredService<IOrderMessageService>()
                .DecideAsync(orderId, questionId, userId, OrderMessageDecision.RemoveItem);
            Assert.True(decided.IsSuccess(), decided.Message);
            Assert.Contains("لغو", decided.Message);
        }

        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            Assert.Equal(OrderStatus.Cancelled, await context.Orders.Where(o => o.Id == orderId).Select(o => o.Status).FirstAsync());
            var question = await context.OrderMessages.FirstAsync(m => m.Id == questionId);
            Assert.Equal(OrderMessageDecision.CancelOrder, question.Decision);
        }
    }

    [Fact]
    public async Task WithdrawnQuestion_CanNoLongerBeAnswered()
    {
        var userId = await db.CreateUserAsync();
        var (orderId, items) = await CreateOrderAsync(userId, PaymentStatus.Paid, (100_000, 1), (100_000, 1));
        var questionId = await AskAsync(orderId, items[0]);

        await using var scope = db.Scope();
        var messages = scope.ServiceProvider.GetRequiredService<IOrderMessageService>();
        Assert.True((await messages.WithdrawAsync(orderId, questionId, null, Staff)).IsSuccess());
        Assert.False((await messages.DecideAsync(orderId, questionId, userId, OrderMessageDecision.CancelOrder)).IsSuccess());
        Assert.Equal(0m, await scope.ServiceProvider.GetRequiredService<IWalletService>().GetBalanceAsync(userId));
    }

    [Fact]
    public async Task CustomerAndStaffMessages_AreThreadedAndCountedAsUnread()
    {
        var userId = await db.CreateUserAsync();
        var (orderId, _) = await CreateOrderAsync(userId, PaymentStatus.Paid, (100_000, 1));

        await using var scope = db.Scope();
        var messages = scope.ServiceProvider.GetRequiredService<IOrderMessageService>();
        Assert.True((await messages.SendFromStaffAsync(orderId, "سلام، سفارش شما فردا ارسال می‌شود.", null, Staff)).IsSuccess());
        Assert.False((await messages.SendFromStaffAsync(orderId, "   ", null, Staff)).IsSuccess());

        var before = await messages.CountUnreadForStaffAsync();
        Assert.True((await messages.ReplyFromCustomerAsync(orderId, userId, "ممنون، لطفاً زنگ بزنید.")).IsSuccess());
        Assert.Equal(before + 1, await messages.CountUnreadForStaffAsync());

        await messages.MarkReadByStaffAsync(orderId);
        Assert.Equal(before, await messages.CountUnreadForStaffAsync());

        var thread = await messages.ListAsync(orderId);
        Assert.Equal(2, thread.Count);
        Assert.True(thread[0].FromStaff);
        Assert.False(thread[1].FromStaff);
        Assert.DoesNotContain(thread, m => m.Body.Contains('<'));
    }

    [Fact]
    public async Task PaymentLinkPage_ShowsOnlyValidTokens()
    {
        var userId = await db.CreateUserAsync();
        var (orderId, _) = await CreateOrderAsync(userId, PaymentStatus.Pending, (100_000, 2));
        var token = StaffOrderService.NewToken();

        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            await context.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s
                .SetProperty(o => o.Status, OrderStatus.Pending)
                .SetProperty(o => o.PaymentToken, token)
                .SetProperty(o => o.PaymentDeadline, (DateTimeOffset?)DateTimeOffset.UtcNow.AddHours(2)));
        }

        await using (var scope = db.Scope())
        {
            var staffOrders = scope.ServiceProvider.GetRequiredService<IStaffOrderService>();
            Assert.Null(await staffOrders.GetPaymentViewAsync("bad"));
            Assert.Null(await staffOrders.GetPaymentViewAsync(StaffOrderService.NewToken()));

            var view = await staffOrders.GetPaymentViewAsync(token);
            Assert.NotNull(view);
            Assert.Equal(250_000m, view!.Payable);
            Assert.True(view.IsPayable);
            Assert.Equal("مشتری", view.CustomerFirstName); // فقط نام کوچک
        }

        await using (var scope = db.Scope())
        {
            var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
            await context.Orders.Where(o => o.Id == orderId).ExecuteUpdateAsync(s => s
                .SetProperty(o => o.PaymentDeadline, (DateTimeOffset?)DateTimeOffset.UtcNow.AddMinutes(-1)));
            var view = await scope.ServiceProvider.GetRequiredService<IStaffOrderService>().GetPaymentViewAsync(token);
            Assert.False(view!.IsPayable); // مهلت گذشته
        }
    }

    [Fact]
    public async Task Crm_TagsNotesAndRfm()
    {
        var userId = await db.CreateUserAsync();
        var authorId = Guid.NewGuid();
        await using var scope = db.Scope();
        var crm = scope.ServiceProvider.GetRequiredService<ICustomerCrmService>();

        Assert.True((await crm.AddTagAsync(userId, "  #مشتری   عمده ", Staff)).IsSuccess());
        Assert.False((await crm.AddTagAsync(userId, "مشتری عمده", Staff)).IsSuccess()); // تکراری
        Assert.False((await crm.AddTagAsync(userId, new string('ا', 41), Staff)).IsSuccess());
        Assert.False((await crm.AddTagAsync(Guid.NewGuid(), "VIP", Staff)).IsSuccess());
        var tag = Assert.Single(await crm.GetTagsAsync(userId));
        Assert.Equal("مشتری عمده", tag.Tag);
        Assert.Contains(await crm.PopularTagsAsync(), t => t.Tag == "مشتری عمده");

        Assert.True((await crm.RemoveTagAsync(userId, tag.Id)).IsSuccess());
        Assert.Empty(await crm.GetTagsAsync(userId));
        Assert.True((await crm.AddTagAsync(userId, "مشتری عمده", Staff)).IsSuccess()); // بعد از حذف نرم دوباره مجاز است

        Assert.True((await crm.AddNoteAsync(userId, "یادداشت معمولی", false, authorId, Staff)).IsSuccess());
        Assert.True((await crm.AddNoteAsync(userId, "هشدار مهم", true, authorId, Staff)).IsSuccess());
        Assert.False((await crm.AddNoteAsync(userId, " ", false, authorId, Staff)).IsSuccess());
        var notes = await crm.GetNotesAsync(userId);
        Assert.Equal("هشدار مهم", notes[0].Content); // سنجاق‌شده اول

        var plain = notes[1];
        Assert.False((await crm.DeleteNoteAsync(userId, plain.Id, Guid.NewGuid(), canDeleteOthers: false)).IsSuccess());
        Assert.True((await crm.DeleteNoteAsync(userId, plain.Id, authorId, canDeleteOthers: false)).IsSuccess());
        Assert.True((await crm.TogglePinAsync(userId, notes[0].Id)).IsSuccess());
        Assert.False((await crm.GetNotesAsync(userId)).Single().IsPinned);

        Assert.Equal(CustomerSegment.NoPurchase, (await crm.GetRfmAsync(userId)).Segment);
        await CreateOrderAsync(userId, PaymentStatus.Paid, (300_000, 1));
        var rfm = await crm.GetRfmAsync(userId);
        Assert.Equal(CustomerSegment.New, rfm.Segment);
        Assert.Equal(5, rfm.Recency);
        Assert.Equal(1, rfm.Frequency);
    }
}
