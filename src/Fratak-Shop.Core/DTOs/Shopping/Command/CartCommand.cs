namespace Fratak_Shop.Core.DTOs.Shopping.Command;

/// <summary>
/// دستور ایجاد سبد خرید برای کاربر یا مهمان (SessionKey).
/// </summary>
public class CreateCartCommand
{
    public Guid? UserId { get; set; }

    /// <summary>کلید سشن مهمان؛ برای کاربران ثبت‌نام‌شده لازم نیست.</summary>
    public string? SessionKey { get; set; }
}

/// <summary>
/// دستور افزودن محصول به سبد خرید. اگر محصول از قبل در سبد باشد، تعداد آن زیاد می‌شود.
/// </summary>
public class AddCartItemCommand
{
    public Guid CartId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; } = 1;
}

/// <summary>
/// دستور تغییر تعداد یک محصول در سبد خرید.
/// </summary>
public class UpdateCartItemCommand
{
    public Guid CartId { get; set; }
    public Guid ProductId { get; set; }
    public int Quantity { get; set; }
}

/// <summary>
/// دستور حذف یک محصول از سبد خرید.
/// </summary>
public class RemoveCartItemCommand
{
    public Guid CartId { get; set; }
    public Guid ProductId { get; set; }
}
