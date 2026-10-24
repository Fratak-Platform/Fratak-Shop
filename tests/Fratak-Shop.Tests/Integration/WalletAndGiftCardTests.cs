using Fratak_Shop.Core.DTOs.Engagement;
using Fratak_Shop.Core.DTOs.Wallets;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Wallets;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Wallets;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace Fratak_Shop.Tests.Integration;

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class WalletTests(SqlServerFixture db)
{
    private async Task<WalletSummaryDto> SummaryAsync(Guid userId)
    {
        await using var scope = db.Scope();
        return await scope.ServiceProvider.GetRequiredService<IWalletService>().GetSummaryAsync(userId);
    }

    private async Task<OperationResult<WalletMovement>> ApplyAsync(WalletEntry entry, bool credit)
    {
        await using var scope = db.Scope();
        var wallet = scope.ServiceProvider.GetRequiredService<IWalletService>();
        return credit ? await wallet.CreditAsync(entry) : await wallet.DebitAsync(entry);
    }

    private Task TopUpAsync(Guid userId, decimal amount) =>
        ApplyAsync(new WalletEntry { UserId = userId, Amount = amount, Type = WalletTransactionType.TopUp, Description = "test top-up" }, credit: true);

    private async Task GrantPromotionAsync(Guid userId, decimal amount)
    {
        await using var scope = db.Scope();
        var result = await scope.ServiceProvider.GetRequiredService<IWalletService>().GrantPromotionalCreditAsync(userId, amount, "test promotion");
        Assert.True(result.IsSuccess(), result.Message);
    }

    [Fact]
    public async Task Balance_KeepsOwnMoneyAndPromotionalCreditApart()
    {
        var user = await db.CreateUserAsync();
        await TopUpAsync(user, 300_000);
        await GrantPromotionAsync(user, 200_000);

        var summary = await SummaryAsync(user);
        Assert.Equal(500_000m, summary.Balance);
        Assert.Equal(200_000m, summary.BonusBalance);
        Assert.Equal(300_000m, summary.WithdrawableBalance);
    }

    [Fact]
    public async Task PromotionalCredit_CanNeverBeSpentAsOwnMoney()
    {
        var user = await db.CreateUserAsync();
        await GrantPromotionAsync(user, 200_000);

        var debit = await ApplyAsync(new WalletEntry
        {
            UserId = user, Amount = 100_000, Type = WalletTransactionType.GiftCardPurchase,
            DebitSource = WalletDebitSource.WithdrawableOnly,
        }, credit: false);

        Assert.False(debit.IsSuccess());
        Assert.Equal(200_000m, (await SummaryAsync(user)).Balance);
    }

    [Fact]
    public async Task Purchases_UsePromotionalCreditFirst()
    {
        var user = await db.CreateUserAsync();
        await TopUpAsync(user, 300_000);
        await GrantPromotionAsync(user, 200_000);

        var debit = await ApplyAsync(new WalletEntry { UserId = user, Amount = 250_000, Type = WalletTransactionType.OrderPayment }, credit: false);
        Assert.True(debit.IsSuccess(), debit.Message);

        var summary = await SummaryAsync(user);
        Assert.Equal(250_000m, summary.Balance);
        Assert.Equal(0m, summary.BonusBalance);
        Assert.Equal(250_000m, summary.WithdrawableBalance);
    }

    [Fact]
    public async Task Debit_AboveTheBalanceIsRejected()
    {
        var user = await db.CreateUserAsync();
        await TopUpAsync(user, 50_000);

        var debit = await ApplyAsync(new WalletEntry { UserId = user, Amount = 50_001, Type = WalletTransactionType.OrderPayment }, credit: false);

        Assert.False(debit.IsSuccess());
        Assert.Equal(50_000m, (await SummaryAsync(user)).Balance);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1000)]
    public async Task Movements_NeedAPositiveWholeAmount(int amount)
    {
        var user = await db.CreateUserAsync();
        var credit = await ApplyAsync(new WalletEntry { UserId = user, Amount = amount, Type = WalletTransactionType.TopUp }, credit: true);
        Assert.False(credit.IsSuccess());
    }

    [Fact]
    public async Task ParallelDebits_NeverSpendTheSameMoneyTwice()
    {
        var user = await db.CreateUserAsync();
        await TopUpAsync(user, 100_000);

        var results = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Task.Run(() =>
            ApplyAsync(new WalletEntry { UserId = user, Amount = 40_000, Type = WalletTransactionType.OrderPayment }, credit: false))));

        var succeeded = results.Count(r => r.IsSuccess());
        var balance = (await SummaryAsync(user)).Balance;
        Assert.InRange(succeeded, 1, 2);
        Assert.Equal(100_000m - succeeded * 40_000m, balance);
        Assert.True(balance >= 0);

        // The ledger agrees with the cached balance.
        await using var scope = db.Scope();
        var context = scope.ServiceProvider.GetRequiredService<FratakShopDbContext>();
        var debited = await context.WalletTransactions.Where(t => t.UserId == user && t.Type == WalletTransactionType.OrderPayment).CountAsync();
        Assert.Equal(succeeded, debited);
    }
}

[Collection(SqlServerCollection.Name)]
[Trait("Category", "Integration")]
public class GiftCardTests(SqlServerFixture db)
{
    private async Task<OperationResult<GiftCardPurchaseResult>> BuyWithWalletAsync(Guid userId, decimal amount)
    {
        await using var scope = db.Scope();
        return await scope.ServiceProvider.GetRequiredService<IGiftCardService>().PurchaseAsync(new PurchaseGiftCardCommand
        {
            UserId = userId,
            Amount = amount,
            PaymentMethod = GiftCardPaymentMethod.Wallet,
            RecipientName = "دوست من",
            Message = "تولدت مبارک",
        });
    }

    private async Task<OperationResult<decimal>> RedeemAsync(Guid userId, string? code)
    {
        await using var scope = db.Scope();
        return await scope.ServiceProvider.GetRequiredService<IGiftCardService>().RedeemAsync(userId, code);
    }

    private async Task<string> CodeOfAsync(Guid purchaser, Guid giftCardId)
    {
        await using var scope = db.Scope();
        var cards = await scope.ServiceProvider.GetRequiredService<IGiftCardService>().GetPurchasedAsync(purchaser);
        var code = cards.Single(c => c.Id == giftCardId).Code;
        Assert.False(string.IsNullOrEmpty(code));
        return code!;
    }

    private async Task<WalletSummaryDto> SummaryAsync(Guid userId)
    {
        await using var scope = db.Scope();
        return await scope.ServiceProvider.GetRequiredService<IWalletService>().GetSummaryAsync(userId);
    }

    private async Task<Guid> CustomerWithOwnMoneyAsync(decimal amount)
    {
        var user = await db.CreateUserAsync();
        await using var scope = db.Scope();
        var credit = await scope.ServiceProvider.GetRequiredService<IWalletService>()
            .CreditAsync(new WalletEntry { UserId = user, Amount = amount, Type = WalletTransactionType.TopUp, Description = "test top-up" });
        Assert.True(credit.IsSuccess(), credit.Message);
        return user;
    }

    [Fact]
    public async Task BuyWithWallet_ThenAFriendRedeemsIt()
    {
        var buyer = await CustomerWithOwnMoneyAsync(500_000);
        var friend = await db.CreateUserAsync();

        var purchase = await BuyWithWalletAsync(buyer, 200_000);
        Assert.True(purchase.IsSuccess(), purchase.Message);
        Assert.False(purchase.Data.RequiresPayment);
        Assert.Equal(300_000m, (await SummaryAsync(buyer)).Balance);

        var code = await CodeOfAsync(buyer, purchase.Data.GiftCardId);
        var redeem = await RedeemAsync(friend, code.ToLowerInvariant()); // customers type it any way
        Assert.True(redeem.IsSuccess(), redeem.Message);
        Assert.Equal(200_000m, redeem.Data);

        var friendWallet = await SummaryAsync(friend);
        Assert.Equal(200_000m, friendWallet.Balance);
        Assert.Equal(200_000m, friendWallet.BonusBalance); // gift credit is for shopping, not for withdrawal (default setting)

        await using var scope = db.Scope();
        var card = await scope.ServiceProvider.GetRequiredService<FratakShopDbContext>().GiftCards.SingleAsync(g => g.Id == purchase.Data.GiftCardId);
        Assert.Equal(GiftCardStatus.Redeemed, card.Status);
        Assert.Equal(friend, card.RedeemedByUserId);
        Assert.NotEqual(code, card.CodeHash); // only a hash is stored for lookups
    }

    [Fact]
    public async Task ACode_WorksOnlyOnce_EvenInParallel()
    {
        var buyer = await CustomerWithOwnMoneyAsync(300_000);
        var purchase = await BuyWithWalletAsync(buyer, 100_000);
        Assert.True(purchase.IsSuccess(), purchase.Message);
        var code = await CodeOfAsync(buyer, purchase.Data.GiftCardId);

        var users = await Task.WhenAll(Enumerable.Range(0, 5).Select(_ => db.CreateUserAsync()));
        var results = await Task.WhenAll(users.Select(u => Task.Run(() => RedeemAsync(u, code))));

        Assert.Equal(1, results.Count(r => r.IsSuccess()));
        var total = 0m;
        foreach (var user in users)
            total += (await SummaryAsync(user)).Balance;
        Assert.Equal(100_000m, total);

        var again = await RedeemAsync(users[0], code);
        Assert.False(again.IsSuccess());
    }

    [Fact]
    public async Task PromotionalCredit_CannotBeTurnedIntoAGiftCard()
    {
        var user = await db.CreateUserAsync();
        await using (var scope = db.Scope())
            await scope.ServiceProvider.GetRequiredService<IWalletService>().GrantPromotionalCreditAsync(user, 1_000_000, "campaign");

        var purchase = await BuyWithWalletAsync(user, 200_000);

        Assert.False(purchase.IsSuccess());
        Assert.Equal(1_000_000m, (await SummaryAsync(user)).Balance);
        await using var check = db.Scope();
        Assert.False(await check.ServiceProvider.GetRequiredService<FratakShopDbContext>().GiftCards.AnyAsync(g => g.PurchaserUserId == user));
    }

    [Theory]
    [InlineData(99_000)] // below the minimum
    [InlineData(150_500)] // not a multiple of 1,000
    [InlineData(20_000_000)] // above the maximum
    public async Task InvalidAmounts_AreRejected(int amount)
    {
        var buyer = await CustomerWithOwnMoneyAsync(30_000_000);
        var purchase = await BuyWithWalletAsync(buyer, amount);
        Assert.False(purchase.IsSuccess());
        Assert.Equal(30_000_000m, (await SummaryAsync(buyer)).Balance);
    }

    [Fact]
    public async Task UnknownCodes_AreThrottled()
    {
        var buyer = await CustomerWithOwnMoneyAsync(300_000);
        var purchase = await BuyWithWalletAsync(buyer, 100_000);
        var realCode = await CodeOfAsync(buyer, purchase.Data.GiftCardId);
        var guesser = await db.CreateUserAsync();

        for (var i = 0; i < 5; i++)
            Assert.False((await RedeemAsync(guesser, GiftCardCode.Generate())).IsSuccess());

        // Locked out for an hour – even a valid code is refused now.
        var blocked = await RedeemAsync(guesser, realCode);
        Assert.False(blocked.IsSuccess());
        Assert.Equal(0m, (await SummaryAsync(guesser)).Balance);

        // The card is still usable by someone else.
        Assert.True((await RedeemAsync(await db.CreateUserAsync(), realCode)).IsSuccess());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("1234")]
    [InlineData("ABCD-EFGH-IJKL-MNOP")]
    public async Task MalformedCodes_AreRejected(string? code)
    {
        var user = await db.CreateUserAsync();
        Assert.False((await RedeemAsync(user, code)).IsSuccess());
    }
}
