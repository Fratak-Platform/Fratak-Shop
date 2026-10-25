using System.Security.Cryptography;
using Fratak_Shop.Core.DTOs.Notifications;
using Fratak_Shop.Core.DTOs.Orders.Command;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Notifications;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Users;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Orders;

/// <summary>داده‌های فرم «ثبت سفارش برای مشتری» در پنل.</summary>
public sealed class StaffOrderCommand
{
    public Guid UserId { get; set; }

    /// <summary>یکی از آدرس‌های ثبت‌شدهٔ همان مشتری.</summary>
    public Guid AddressId { get; set; }

    /// <summary>روش ارسال (فقط روش‌های بدون بازهٔ زمانی؛ چون تأمین‌کننده مستقیم برای مشتری ارسال می‌کند).</summary>
    public Guid ShippingMethodId { get; set; }

    public List<OrderItemRequest> Items { get; set; } = [];

    public string? CouponCode { get; set; }

    /// <summary>توضیح سفارش که روی سفارش ثبت می‌شود (مثل توضیحات مشتری در تسویه حساب).</summary>
    public string? CustomerNote { get; set; }

    /// <summary>پیام اختیاری به مشتری که همراه سفارش در صفحهٔ سفارش او نمایش داده می‌شود.</summary>
    public string? Message { get; set; }
}

/// <summary>نتیجهٔ ثبت سفارش توسط کارمند.</summary>
public sealed record StaffOrderResult(Guid OrderId, string OrderNumber, string PaymentToken, decimal PayableAmount, DateTimeOffset ExpiresAt);

/// <summary>یک محصول در جست‌وجوی فرم ثبت سفارش پنل (قیمت نهایی با تخفیف فعال همان لحظه).</summary>
public sealed record StaffOrderProduct(Guid Id, string Name, string Sku, decimal Price, decimal FinalPrice, int Stock, string? Image);

/// <summary>یک آدرس مشتری در فرم ثبت سفارش.</summary>
public sealed record StaffOrderAddress(Guid Id, string Title, string Summary, string Recipient, bool IsDefault, DateTimeOffset CreatedAt);

/// <summary>مشتری انتخاب‌شده در فرم ثبت سفارش، با آدرس‌ها و خلاصهٔ سابقه.</summary>
public sealed record StaffOrderCustomer(Guid Id, string Phone, string Name, bool IsBlocked, int OrdersCount, List<StaffOrderAddress> Addresses);

/// <summary>روش ارسال قابل انتخاب برای سفارش پنل.</summary>
public sealed record StaffOrderShipping(Guid Id, string Title, decimal Cost, decimal? FreeThreshold, int MinDays, int MaxDays);

/// <summary>یک قلم در صفحهٔ عمومی پرداخت.</summary>
public sealed record PaymentLinkItem(string Name, int Quantity, decimal UnitPrice, decimal Total);

/// <summary>
/// اطلاعات صفحهٔ عمومی پرداخت سفارش (<c>/pay/{token}</c>). عمداً فقط چیزهایی که مشتری برای تصمیم لازم دارد:
/// اقلام، مبالغ، مقصد (فقط شهر) و مهلت؛ آدرس کامل و موبایل نمایش داده نمی‌شود چون لینک ممکن است دست دیگری بیفتد.
/// </summary>
public sealed class PaymentLinkView
{
    public Guid OrderId { get; init; }
    public Guid? UserId { get; init; }
    public string OrderNumber { get; init; } = null!;
    public string CustomerFirstName { get; init; } = null!;
    public string City { get; init; } = null!;
    public string? ShippingMethod { get; init; }
    public List<PaymentLinkItem> Items { get; init; } = [];
    public decimal Subtotal { get; init; }
    public decimal Discount { get; init; }
    public decimal Shipping { get; init; }
    public decimal Payable { get; init; }
    public OrderStatus Status { get; init; }
    public PaymentStatus PaymentStatus { get; init; }
    public DateTimeOffset? Deadline { get; init; }

    /// <summary>هنوز می‌شود پرداخت کرد (منتظر پرداخت و قبل از مهلت).</summary>
    public bool IsPayable { get; init; }

    public bool IsPaid => PaymentStatus == PaymentStatus.Paid;
}

/// <summary>
/// ثبت سفارش توسط کارمند برای مشتری و «لینک پرداخت».
/// <para>
/// سناریو: مشتری در اینستاگرام/تلفن سفارش می‌دهد. کارمند در <c>/admin/orders/new</c> مشتری (یا حساب تازه با موبایل)،
/// آدرس، روش ارسال و کالاها را انتخاب می‌کند. سیستم سفارش را با همان قوانین تسویه حساب سایت ثبت می‌کند
/// (قیمت روز، موجودی، کد تخفیف، هزینهٔ ارسال)، موجودی را رزرو می‌کند و یک لینک یکتا مثل
/// <c>https://site/pay/Xk29aB...</c> برای مشتری پیامک/اعلان می‌کند. مشتری بدون نیاز به ورود، اقلام و مبلغ را می‌بیند و
/// از درگاه زرین‌پال پرداخت می‌کند؛ ثبت پرداخت فقط توسط تأیید درگاه انجام می‌شود (هیچ ثبت دستی وجود ندارد).
/// اگر تا پایان مهلت پرداخت نشود، کار پس‌زمینهٔ «لغو سفارش‌های پرداخت‌نشده» سفارش را لغو و موجودی را آزاد می‌کند.
/// </para>
/// </summary>
public interface IStaffOrderService
{
    /// <summary>ثبت سفارش و ارسال لینک پرداخت. <paramref name="baseUrl"/> آدرس سایت برای ساخت لینک کامل پیامک است.</summary>
    Task<OperationResult<StaffOrderResult>> CreateAsync(StaffOrderCommand command, Guid staffId, string staffName, string baseUrl);

    /// <summary>ارسال دوبارهٔ لینک و تمدید مهلت (فقط برای سفارش منتظر پرداخت).</summary>
    Task<OperationResult> ResendLinkAsync(Guid orderId, Guid? staffId, string staffName, string baseUrl);

    /// <summary>اطلاعات صفحهٔ عمومی پرداخت با توکن؛ توکن نامعتبر → null.</summary>
    Task<PaymentLinkView?> GetPaymentViewAsync(string? token);

    /// <summary>توکن لینک پرداخت یک سفارش (برای بازگرداندن مشتری از درگاه به همان صفحه)؛ سفارش معمولی → null.</summary>
    Task<string?> GetTokenByOrderNumberAsync(string orderNumber);

    /// <summary>
    /// ساخت حساب مشتری تازه فقط با موبایل و نام (بدون رمز؛ مشتری بعداً با کد پیامکی وارد می‌شود).
    /// اگر حسابی با این موبایل باشد، همان برگردانده می‌شود.
    /// </summary>
    Task<OperationResult<Guid>> EnsureCustomerAsync(string? phone, string? firstName, string? lastName);

    /// <summary>مشتری با شناسه یا موبایل (هر دو اختیاری؛ اولی اولویت دارد). پیدا نشد → null.</summary>
    Task<StaffOrderCustomer?> FindCustomerAsync(Guid? userId, string? phone);

    /// <summary>جست‌وجوی محصول فعال و موجود با نام/SKU برای افزودن به سفارش (حداکثر ۱۰ نتیجه).</summary>
    Task<List<StaffOrderProduct>> SearchProductsAsync(string? query);

    /// <summary>روش‌های ارسال فعال و بدون بازهٔ زمانی (همان‌هایی که سفارش پنل می‌پذیرد).</summary>
    Task<List<StaffOrderShipping>> GetShippingMethodsAsync();

    /// <summary>مشخصات چند محصول با شناسه (برای بازسازی فرم بعد از خطای ثبت، تا کارمند اقلام را دوباره وارد نکند).</summary>
    Task<List<StaffOrderProduct>> GetProductsAsync(IReadOnlyCollection<Guid> ids);
}

public class StaffOrderService(
    FratakShopDbContext context,
    IOrderService orders,
    IStoreSettingService settings,
    IOrderMessageService messages,
    INotificationService notifications,
    IMonitoringService monitoring) : IStaffOrderService
{
    /// <summary>طول توکن: ۲۴ بایت تصادفی ← ۳۲ نویسهٔ base64url (حدس زدنش عملاً ناممکن است).</summary>
    private const int TokenBytes = 24;

    /// <summary>
    /// ساخت توکن تصادفی امن و URL-safe. مثال خروجی: <c>Xk29aB-4fQ_r7LmN0pZs8YtW2cVb1eHd</c>.
    /// </summary>
    public static string NewToken() =>
        Convert.ToBase64String(RandomNumberGenerator.GetBytes(TokenBytes)).TrimEnd('=').Replace('+', '-').Replace('/', '_');

    /// <summary>
    /// نرمال‌سازی موبایل: ارقام فارسی/عربی → انگلیسی، «+98» → «0». نامعتبر → null.
    /// مثال: «۰۹۱۲ ۱۲۳ ۴۵۶۷» → «09121234567».
    /// </summary>
    public static string? NormalizePhone(string? phone)
    {
        var number = new string(SearchText.NormalizeDigits(phone).Where(c => char.IsAsciiDigit(c) || c == '+').ToArray());
        if (number.StartsWith("+98", StringComparison.Ordinal))
            number = "0" + number[3..];
        else if (number.StartsWith("98", StringComparison.Ordinal) && number.Length == 12)
            number = "0" + number[2..];
        return number.Length == 11 && number.StartsWith("09", StringComparison.Ordinal) && number.All(char.IsAsciiDigit) ? number : null;
    }

    /// <summary>مسیر نسبی صفحهٔ پرداخت؛ مثال: <c>/pay/Xk29...</c>.</summary>
    public static string LinkPath(string token) => "/pay/" + token;

    /// <summary>توکن معتبر فقط حروف/ارقام انگلیسی، «-» و «_» با طول ۲۰ تا ۶۴ است (جلوگیری از ورودی‌های عجیب).</summary>
    public static bool IsWellFormedToken(string? token) =>
        token is { Length: >= 20 and <= 64 } && token.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');

    public async Task<OperationResult<StaffOrderResult>> CreateAsync(StaffOrderCommand command, Guid staffId, string staffName, string baseUrl)
    {
        var settingsDto = await settings.GetAsync();
        if (!settingsDto.HasZarinPal)
            return OperationResult<StaffOrderResult>.Error("برای ارسال لینک پرداخت، پرداخت آنلاین (زرین‌پال) باید در تنظیمات فعال باشد.");

        if (command.Items.Count == 0 || command.Items.All(i => i.Quantity <= 0))
            return OperationResult<StaffOrderResult>.Error("حداقل یک کالا به سفارش اضافه کنید.");
        if (command.Items.Any(i => i.Quantity is <= 0 or > 1000))
            return OperationResult<StaffOrderResult>.Error("تعداد هر کالا باید بین ۱ تا ۱۰۰۰ باشد.");

        var user = await context.Users.Where(u => u.Id == command.UserId)
            .Select(u => new { u.Id, u.PhoneNumber, u.FirstName, u.LastName, u.IsBlocked }).FirstOrDefaultAsync();
        if (user is null)
            return OperationResult<StaffOrderResult>.NotFound("مشتری یافت نشد.");
        if (user.IsBlocked)
            return OperationResult<StaffOrderResult>.Error("حساب این مشتری مسدود است.");

        var address = await context.UserAddresses
            .Where(a => a.Id == command.AddressId && a.UserId == user.Id)
            .Select(a => new
            {
                a.ProvinceId, a.CityId, a.FirstName, a.LastName, a.AddressLine, a.BuildingNumber, a.Unit, a.PostalCode, a.PhoneNumber,
                a.Latitude, a.Longitude,
                ProvinceName = a.Province != null ? a.Province.Name : null,
                CityName = a.City != null ? a.City.Name : null,
            })
            .FirstOrDefaultAsync();
        if (address is null)
            return OperationResult<StaffOrderResult>.Error("آدرس انتخاب‌شده متعلق به این مشتری نیست.");

        var method = await context.ShippingMethods.Where(m => m.Id == command.ShippingMethodId && m.IsActive)
            .Select(m => new { m.RequiresTimeSlot }).FirstOrDefaultAsync();
        if (method is null)
            return OperationResult<StaffOrderResult>.Error("روش ارسال انتخاب‌شده فعال نیست.");
        if (method.RequiresTimeSlot)
            return OperationResult<StaffOrderResult>.Error("روش‌های ارسال دارای بازهٔ زمانی تحویل برای سفارش پنل پشتیبانی نمی‌شوند؛ روش پستی را انتخاب کنید.");

        var recipient = $"{address.FirstName} {address.LastName}".Trim();
        var customerName = recipient.Length > 0 ? recipient : $"{user.FirstName} {user.LastName}".Trim();

        var created = await orders.CreateOrderAsync(new CreateOrderCommand
        {
            UserId = user.Id,
            PaymentMethod = PaymentMethod.OnlineGateway,
            ShippingMethodId = command.ShippingMethodId,
            ShippingProvinceId = address.ProvinceId,
            ShippingCityId = address.CityId,
            CustomerName = customerName.Length > 0 ? customerName : user.PhoneNumber,
            CustomerPhone = user.PhoneNumber,
            ShippingProvince = address.ProvinceName ?? string.Empty,
            ShippingCity = address.CityName ?? string.Empty,
            ShippingAddress = address.AddressLine,
            ShippingBuildingNumber = address.BuildingNumber,
            ShippingUnit = address.Unit,
            ShippingPostalCode = address.PostalCode,
            ShippingPhone = address.PhoneNumber,
            ShippingLatitude = address.Latitude,
            ShippingLongitude = address.Longitude,
            CustomerNote = string.IsNullOrWhiteSpace(command.CustomerNote) ? null : command.CustomerNote.Trim(),
            CouponCode = string.IsNullOrWhiteSpace(command.CouponCode) ? null : command.CouponCode.Trim(),
            Items = command.Items.Where(i => i.Quantity > 0).ToList(),
            SkipPlacedEvent = true,
        });
        if (!created.IsSuccess() || created.Data is null)
            return OperationResult<StaffOrderResult>.Error(created.Message);

        var order = created.Data;
        var token = NewToken();
        var expires = DateTimeOffset.UtcNow.AddHours(settingsDto.PaymentLinkHours);
        await context.Orders.Where(o => o.Id == order.Id)
            .ExecuteUpdateAsync(s => s
                .SetProperty(o => o.PaymentToken, token)
                .SetProperty(o => o.PaymentDeadline, (DateTimeOffset?)expires)
                .SetProperty(o => o.CreatedByStaffId, (Guid?)staffId)
                .SetProperty(o => o.CreatedByStaffName, staffName));

        context.OrderNotes.Add(new OrderNote
        {
            OrderId = order.Id, Kind = OrderNoteKind.System, AuthorUserId = staffId, AuthorName = staffName,
            Content = $"سفارش در پنل ثبت شد و لینک پرداخت (معتبر تا {PersianDate.Format(expires)}) برای مشتری ارسال شد.",
        });
        await context.SaveChangesAsync();

        if (!string.IsNullOrWhiteSpace(command.Message))
            await messages.SendFromStaffAsync(order.Id, command.Message, staffId, staffName);

        await SendLinkAsync(user.Id, order.OrderNumber, order.PayableAmount, token, expires, baseUrl);
        await monitoring.LogActionAsync("order.create", "Order", order.Id, $"{order.OrderNumber}: ثبت در پنل – {order.PayableAmount:N0} تومان");

        return OperationResult<StaffOrderResult>.Success(new StaffOrderResult(order.Id, order.OrderNumber, token, order.PayableAmount, expires),
            $"سفارش {order.OrderNumber} ثبت و لینک پرداخت برای مشتری ارسال شد.");
    }

    public async Task<OperationResult> ResendLinkAsync(Guid orderId, Guid? staffId, string staffName, string baseUrl)
    {
        var order = await context.Orders.Where(o => o.Id == orderId)
            .Select(o => new { o.UserId, o.OrderNumber, o.PaymentToken, o.Status, o.PaymentStatus, o.PayableAmount }).FirstOrDefaultAsync();
        if (order is null)
            return OperationResult.NotFound("سفارش یافت نشد.");
        if (order.PaymentToken is null || order.UserId is null)
            return OperationResult.Error("این سفارش لینک پرداخت ندارد.");
        if (order.Status != OrderStatus.Pending || order.PaymentStatus == PaymentStatus.Paid)
            return OperationResult.Error("این سفارش دیگر منتظر پرداخت نیست.");

        var hours = (await settings.GetAsync()).PaymentLinkHours;
        var expires = DateTimeOffset.UtcNow.AddHours(hours);
        // فقط سفارشی که هنوز منتظر پرداخت است تمدید می‌شود (اگر هم‌زمان لغو/پرداخت شد، کاری انجام نمی‌شود).
        var affected = await context.Orders
            .Where(o => o.Id == orderId && o.Status == OrderStatus.Pending && o.PaymentStatus != PaymentStatus.Paid)
            .ExecuteUpdateAsync(s => s.SetProperty(o => o.PaymentDeadline, (DateTimeOffset?)expires));
        if (affected == 0)
            return OperationResult.Error("وضعیت سفارش هم‌زمان تغییر کرد؛ صفحه را تازه کنید.");

        context.OrderNotes.Add(new OrderNote
        {
            OrderId = orderId, Kind = OrderNoteKind.System, AuthorUserId = staffId, AuthorName = staffName,
            Content = $"لینک پرداخت دوباره ارسال و مهلت تا {PersianDate.Format(expires)} تمدید شد.",
        });
        await context.SaveChangesAsync();

        await SendLinkAsync(order.UserId.Value, order.OrderNumber, order.PayableAmount, order.PaymentToken, expires, baseUrl);
        return OperationResult.Success($"لینک پرداخت دوباره ارسال شد (معتبر تا {PersianDate.Format(expires)}).");
    }

    public async Task<PaymentLinkView?> GetPaymentViewAsync(string? token)
    {
        if (!IsWellFormedToken(token))
            return null;

        var order = await context.Orders.Where(o => o.PaymentToken == token)
            .Select(o => new
            {
                o.Id, o.UserId, o.OrderNumber, o.CustomerName, o.ShippingCity, o.ShippingMethod, o.SubtotalAmount, o.DiscountAmount,
                o.ShippingAmount, o.PayableAmount, o.Status, o.PaymentMethod, o.PaymentStatus, o.PaymentDeadline,
                ItemsDiscount = o.Items.Sum(i => (decimal?)i.DiscountAmount) ?? 0,
                Items = o.Items.OrderBy(i => i.CreatedAt).Select(i => new PaymentLinkItem(i.ProductName, i.Quantity, i.UnitPrice, i.TotalAmount)).ToList(),
            })
            .FirstOrDefaultAsync();
        if (order is null)
            return null;

        var now = DateTimeOffset.UtcNow;
        return new PaymentLinkView
        {
            OrderId = order.Id,
            UserId = order.UserId,
            OrderNumber = order.OrderNumber,
            // فقط نام کوچک برای خوشامد؛ نام کامل، موبایل و آدرس در صفحهٔ عمومی نمایش داده نمی‌شود.
            CustomerFirstName = order.CustomerName.Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "",
            City = order.ShippingCity,
            ShippingMethod = order.ShippingMethod,
            Items = order.Items,
            Subtotal = order.SubtotalAmount,
            Discount = order.ItemsDiscount + order.DiscountAmount,
            Shipping = order.ShippingAmount,
            Payable = order.PayableAmount,
            Status = order.Status,
            PaymentStatus = order.PaymentStatus,
            Deadline = order.PaymentDeadline,
            IsPayable = OrderRules.IsPayable(order.Status, order.PaymentMethod, order.PaymentStatus, order.PaymentDeadline, now),
        };
    }

    public async Task<string?> GetTokenByOrderNumberAsync(string orderNumber) =>
        await context.Orders.Where(o => o.OrderNumber == orderNumber).Select(o => o.PaymentToken).FirstOrDefaultAsync();

    public async Task<OperationResult<Guid>> EnsureCustomerAsync(string? phone, string? firstName, string? lastName)
    {
        if (NormalizePhone(phone) is not { } number)
            return OperationResult<Guid>.Error("شمارهٔ موبایل معتبر نیست (مثال: 09121234567).");

        var existing = await context.Users.Where(u => u.PhoneNumber == number).Select(u => (Guid?)u.Id).FirstOrDefaultAsync();
        if (existing is { } id)
            return OperationResult<Guid>.Success(id, "این مشتری قبلاً حساب داشته است.");

        var first = firstName.ToPlainText(50);
        var last = lastName.ToPlainText(50);
        var user = new User
        {
            PhoneNumber = number,
            FirstName = string.IsNullOrWhiteSpace(first) ? null : first,
            LastName = string.IsNullOrWhiteSpace(last) ? null : last,
            Role = UserRole.Customer,
            // مالکیت شماره هنوز با کد پیامکی ثابت نشده؛ با اولین ورود پیامکی تأیید می‌شود.
            IsPhoneVerified = false,
            SecurityStamp = PasswordHasher.NewSecurityStamp(),
        };
        context.Users.Add(user);
        try
        {
            await context.SaveChangesAsync();
        }
        catch (DbUpdateException)
        {
            // هم‌زمان با همین شماره ثبت‌نام شده است.
            context.ChangeTracker.Clear();
            var again = await context.Users.Where(u => u.PhoneNumber == number).Select(u => (Guid?)u.Id).FirstOrDefaultAsync();
            return again is { } againId
                ? OperationResult<Guid>.Success(againId, "این مشتری قبلاً حساب داشته است.")
                : OperationResult<Guid>.Error("ساخت حساب انجام نشد؛ دوباره تلاش کنید.");
        }

        await monitoring.LogActionAsync("customer.create", "User", user.Id, $"ساخت حساب مشتری {number} برای سفارش پنل");
        return OperationResult<Guid>.Success(user.Id, "حساب مشتری ساخته شد.");
    }

    public async Task<StaffOrderCustomer?> FindCustomerAsync(Guid? userId, string? phone)
    {
        var query = context.Users.AsQueryable();
        if (userId is { } id)
            query = query.Where(u => u.Id == id);
        else if (NormalizePhone(phone) is { } number)
            query = query.Where(u => u.PhoneNumber == number);
        else
            return null;

        var user = await query.Select(u => new
        {
            u.Id, u.PhoneNumber, u.FirstName, u.LastName, u.IsBlocked,
            OrdersCount = u.Orders.Count(),
        }).FirstOrDefaultAsync();
        if (user is null)
            return null;

        var addresses = await context.UserAddresses.Where(a => a.UserId == user.Id)
            .OrderByDescending(a => a.IsDefault).ThenByDescending(a => a.CreatedAt)
            .Select(a => new
            {
                a.Id, a.Title, a.FirstName, a.LastName, a.AddressLine, a.PostalCode, a.IsDefault, a.CreatedAt,
                Province = a.Province.Name, City = a.City.Name,
            })
            .ToListAsync();

        var name = $"{user.FirstName} {user.LastName}".Trim();
        return new StaffOrderCustomer(user.Id, user.PhoneNumber, name.Length > 0 ? name : "بدون نام", user.IsBlocked, user.OrdersCount,
            addresses.Select(a => new StaffOrderAddress(a.Id, a.Title, $"{a.Province}، {a.City}، {a.AddressLine} – کد پستی {a.PostalCode}",
                $"{a.FirstName} {a.LastName}".Trim(), a.IsDefault, a.CreatedAt)).ToList());
    }

    public async Task<List<StaffOrderProduct>> SearchProductsAsync(string? query)
    {
        var text = SearchText.Normalize(query);
        if (text.Length < 2)
            return [];

        var products = context.Products.Where(p => p.IsActive && p.StockQuantity > 0);
        foreach (var term in SearchText.Terms(text).Take(5))
            products = products.Where(p => p.Name.Contains(term) || p.SKU.Contains(term) || (p.EnglishName != null && p.EnglishName.Contains(term)));

        return await ProjectAsync(products.OrderByDescending(p => p.SalesCount).Take(10));
    }

    public async Task<List<StaffOrderProduct>> GetProductsAsync(IReadOnlyCollection<Guid> ids)
    {
        if (ids.Count == 0)
            return [];
        var list = ids.Distinct().Take(100).ToList();
        return await ProjectAsync(context.Products.Where(p => list.Contains(p.Id)));
    }

    /// <summary>خواندن ستون‌های لازم محصول و محاسبهٔ قیمت نهایی (مشترک بین جست‌وجو و بازسازی فرم).</summary>
    private static async Task<List<StaffOrderProduct>> ProjectAsync(IQueryable<Product> products)
    {
        var rows = await products
            .Select(p => new
            {
                p.Id, p.Name, p.SKU, p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt, p.StockQuantity,
                Image = p.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Select(i => i.ImageName).FirstOrDefault(),
            })
            .ToListAsync();

        // قیمت نهایی با همان PriceCalculator سبد و سفارش محاسبه می‌شود تا عدد فرم با سفارش ثبت‌شده یکی باشد.
        var now = DateTimeOffset.UtcNow;
        return rows.Select(p =>
        {
            var final = new Product { Price = p.Price, DiscountPrice = p.DiscountPrice, DiscountStartAt = p.DiscountStartAt, DiscountEndAt = p.DiscountEndAt }
                .GetFinalPrice(now);
            return new StaffOrderProduct(p.Id, p.Name, p.SKU, p.Price, final, p.StockQuantity, p.Image);
        }).ToList();
    }

    public async Task<List<StaffOrderShipping>> GetShippingMethodsAsync() =>
        await context.ShippingMethods.Where(m => m.IsActive && !m.RequiresTimeSlot)
            .OrderBy(m => m.DisplayOrder)
            .Select(m => new StaffOrderShipping(m.Id, m.Title, m.Cost, m.FreeShippingThreshold, m.MinDeliveryDays, m.MaxDeliveryDays))
            .ToListAsync();

    private Task SendLinkAsync(Guid userId, string orderNumber, decimal amount, string token, DateTimeOffset expires, string baseUrl)
    {
        var link = baseUrl.TrimEnd('/') + LinkPath(token);
        return notifications.NotifyAsync(new NotificationRequest
        {
            UserId = userId,
            Event = NotificationEvent.OrderPaymentLink,
            Category = NotificationCategory.Order,
            LinkUrl = LinkPath(token),
            Values =
            {
                ["OrderNumber"] = orderNumber,
                ["Amount"] = amount.ToString("N0"),
                ["PaymentLink"] = link,
                ["LinkExpiry"] = PersianDate.Format(expires),
            },
        });
    }
}
