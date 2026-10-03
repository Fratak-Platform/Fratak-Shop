using Fratak_Shop.Core.DTOs.Orders.Command;
using Fratak_Shop.Core.DTOs.Payments.Query;
using Fratak_Shop.Core.DTOs.Shopping.Query;
using Fratak_Shop.Core.DTOs.Shipping.Query;
using Fratak_Shop.Data.Entities.Orders;

namespace Fratak_Shop.Core.DTOs.Checkout.Query;

/// <summary>هر آنچه صفحه تسویه حساب برای نمایش و محاسبه نیاز دارد.</summary>
public class CheckoutSummaryDto
{
    public CartDto Cart { get; set; } = new();
    public List<ShippingMethodDto> ShippingMethods { get; set; } = [];
    public Guid? SelectedShippingMethodId { get; set; }
    public List<PaymentMethod> AvailablePaymentMethods { get; set; } = [];
    public CardToCardInstructionsDto? BankTransfer { get; set; }
    public string? PaymentInstructions { get; set; }
    public bool IsOnlinePaymentEnabled { get; set; }
    public bool IsCashOnDeliveryEnabled { get; set; }
    public bool IsBankTransferEnabled { get; set; }

    public List<PaymentMethodOption> PaymentOptions => AvailablePaymentMethods
        .Select(method => new PaymentMethodOption
        {
            Method = method,
            Title = method switch
            {
                PaymentMethod.OnlineGateway => "پرداخت آنلاین",
                PaymentMethod.CashOnDelivery => "پرداخت در محل",
                PaymentMethod.BankTransfer => "کارت به کارت",
                _ => "سایر",
            },
            Description = method switch
            {
                PaymentMethod.OnlineGateway => "انتقال به درگاه پرداخت امن (حالت آزمایشی/سندباکس در این نسخه فعال است)",
                PaymentMethod.CashOnDelivery => "پرداخت نقدی هنگام تحویل مرسوله",
                PaymentMethod.BankTransfer => "واریز به شماره کارت فروشگاه و ارسال فیش",
                _ => string.Empty,
            },
        })
        .ToList();
}

public class PaymentMethodOption
{
    public PaymentMethod Method { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
}

/// <summary>نتیجه ثبت سفارش و مقصد بعدی کاربر.</summary>
public class CheckoutResultDto
{
    public Guid OrderId { get; set; }
    public string OrderNumber { get; set; } = string.Empty;
    public decimal PayableAmount { get; set; }
    public PaymentMethod PaymentMethod { get; set; }
    public string RedirectUrl { get; set; } = string.Empty;
    public bool RequiresLogin { get; set; }
}

/// <summary>اطلاعات آماده‌شده برای فرم تسویه (پیش‌فرض‌ها از پروفایل کاربر).</summary>
public class CheckoutFormDto
{
    public CheckoutCommand Command { get; set; } = new();
    public List<ShippingMethodDto> ShippingMethods { get; set; } = [];
}
