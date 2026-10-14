namespace Fratak_Shop.Core.DTOs.Orders.Command;

/// <summary>
/// دستور اضافه‌کردن یک قلم به سفارش موجود.
/// </summary>
public class AddOrderItemCommand
{
    public Guid OrderId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; } = 1;
}

/// <summary>
/// دستور تغییر تعداد یک قلم سفارش. مبلغ ردیف و مبالغ کل سفارش بازمحاسبه می‌شود.
/// </summary>
public class EditOrderItemCommand
{
    public Guid Id { get; set; }
    public int Quantity { get; set; }
}
