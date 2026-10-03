using Fratak_Shop.Core.DTOs.Payments.Command;
using Fratak_Shop.Core.DTOs.Payments.Query;
using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Core.Services.Payments;

/// <summary>
/// Internal «کارت به کارت» payment: publishing the shop cards, letting the customer
/// register the transfer and upload the receipt(s), and letting the staff approve or
/// reject it with a notification to the customer.
/// </summary>
public interface IBankTransferService
{
    // ------------------------------------------------------------- admin: cards
    Task<List<PaymentCardDto>> GetCardsAsync(bool onlyActive = false);

    Task<PaymentCardDto?> GetCardAsync(Guid id);

    Task<OperationResult> SaveCardAsync(Guid? id, PaymentCardDto model);

    Task<OperationResult> ToggleCardAsync(Guid id);

    Task<OperationResult> DeleteCardAsync(Guid id);

    // ------------------------------------------------------- storefront: submit
    /// <summary>اطلاعات کارت‌های مقصد برای مرحله پرداخت کارت به کارت.</summary>
    Task<CardToCardInstructionsDto> GetInstructionsAsync(CancellationToken cancellationToken = default);

    /// <summary>ساخت (در صورت نبود) و خواندن واریز کارت به کارت یک سفارش.</summary>
    Task<BankTransferDetailDto?> EnsureForOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<BankTransferDetailDto?> GetForOrderAsync(Guid orderId, CancellationToken cancellationToken = default);

    Task<OperationResult> SubmitAsync(SubmitBankTransferCommand command, CancellationToken cancellationToken = default);

    // ---------------------------------------------------------------- admin list
    Task<BankTransferFilterResult> GetFilterAsync(BankTransferFilterParams filterParams);

    Task<BankTransferDetailDto?> GetAsync(Guid id);

    Task<int> GetPendingCountAsync(CancellationToken cancellationToken = default);

    Task<OperationResult> ReviewAsync(ReviewBankTransferCommand command, CancellationToken cancellationToken = default);

    /// <summary>منقضی‌کردن واریزهایی که مشتری در مهلت مقرر اطلاعات آن‌ها را ثبت نکرده است.</summary>
    Task<int> ExpireOverdueAsync(CancellationToken cancellationToken = default);
}
