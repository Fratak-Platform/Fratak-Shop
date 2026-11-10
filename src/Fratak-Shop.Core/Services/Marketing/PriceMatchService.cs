using System.Security.Cryptography;
using Fratak_Shop.Core.DTOs.Notifications;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Services.Notifications;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Admin;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Notifications;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Marketing;

/// <summary>ورودی فرم «قیمت کمتر دیدید؟». مثال: (X، «https://digikala.com/product/dkp-1»، 1150000).</summary>
public sealed record PriceMatchInput(Guid ProductId, string? CompetitorUrl, decimal CompetitorPrice);

/// <summary>یک ردیف برای پنل مدیریت و صفحهٔ کالا.</summary>
public sealed record PriceMatchRow(
    Guid Id, Guid UserId, string? CustomerName, string? CustomerPhone,
    Guid ProductId, string ProductName, string? ProductSlug,
    string CompetitorUrl, string CompetitorHost, decimal CompetitorPrice, decimal OurPrice, decimal CurrentPrice,
    PriceMatchStatus Status, string? AdminNote, string? CouponCode,
    DateTimeOffset CreatedAt, DateTimeOffset? ReviewedAt, string? ReviewerName);

/// <summary>
/// دور ۲۴ (پیشنهاد ۱۱ – تضمین قیمت): مشتری واردشده می‌گوید همین کالا را جای دیگر ارزان‌تر دیده است.
/// <para>
/// روند: (۱) مشتری در صفحهٔ کالا نشانی صفحهٔ رقیب و قیمت را می‌فرستد (<see cref="SubmitAsync"/>)؛
/// (۲) به کارشناسان بازاریابی اعلان می‌رود؛ (۳) مدیر در «بازاریابی ← تضمین قیمت» صفحهٔ رقیب را می‌بیند و تأیید یا رد می‌کند؛
/// (۴) با تأیید یک کد تخفیف شخصی، یک‌بارمصرف، ۷۲ ساعته و فقط برای همان کالا به اندازهٔ اختلاف قیمت ساخته و برای مشتری فرستاده می‌شود.
/// </para>
/// <para>
/// امنیت (کاربر نمی‌تواند به فروشگاه ضرر بزند): فقط مشتری واردشده؛ روزی ۳ درخواست؛ برای هر کالا فقط یک درخواست باز؛
/// قیمت رقیب باید کمتر از قیمت ما و حداکثر ۳۰٪ پایین‌تر باشد (اختلاف بیشتر معمولاً یعنی کالای دیگر یا کلاه‌برداری)؛
/// نشانی فقط http/https و هرگز توسط سرور باز نمی‌شود (بدون SSRF)؛ کد فقط پس از تأیید انسان ساخته می‌شود و
/// اگر قیمت ما تا لحظهٔ تأیید پایین آمده باشد، فقط اختلاف باقی‌مانده تخفیف داده می‌شود.
/// </para>
/// <para>محل استفاده: Web/Infrastructure/Commerce/CommerceEndpoints.cs (POST /api/price-match)، صفحهٔ کالا (assets/js/commerce.js)
/// و Areas/Admin/Pages/PriceMatch/Index.cshtml.</para>
/// </summary>
public interface IPriceMatchService
{
    /// <summary>حداکثر درخواست هر مشتری در ۲۴ ساعت.</summary>
    const int DailyLimit = 3;

    /// <summary>حداکثر اختلاف پذیرفتنی (درصد).</summary>
    const int MaxGapPercent = 30;

    /// <summary>اعتبار کد تخفیف (ساعت).</summary>
    const int CouponHours = 72;

    /// <summary>
    /// ثبت درخواست. ownHost دامنهٔ خود سایت است (مثلاً «shop.fratak.ir») تا نشانی خودمان «رقیب» حساب نشود.
    /// خطاها متن فارسی ساده دارند (مثلاً «قیمت واردشده از قیمت ما کمتر نیست.»).
    /// </summary>
    Task<OperationResult> SubmitAsync(Guid userId, PriceMatchInput input, string? ownHost, CancellationToken cancellationToken = default);

    /// <summary>فهرست پنل (جدیدها اول). status خالی = همه.</summary>
    Task<List<PriceMatchRow>> ListAsync(PriceMatchStatus? status, int take = 100, CancellationToken cancellationToken = default);

    /// <summary>تعداد درخواست‌های در انتظار (نشان منوی پنل).</summary>
    Task<int> PendingCountAsync(CancellationToken cancellationToken = default);

    /// <summary>تأیید و ساخت کد تخفیف شخصی. خروجی موفق: متن شامل کد.</summary>
    Task<OperationResult> ApproveAsync(Guid id, string reviewer, string? note, CancellationToken cancellationToken = default);

    /// <summary>رد با دلیل (به مشتری اعلان می‌شود).</summary>
    Task<OperationResult> RejectAsync(Guid id, string reviewer, string? note, CancellationToken cancellationToken = default);
}

/// <summary>قواعد خالص و قابل آزمون تضمین قیمت.</summary>
public static class PriceMatchRules
{
    /// <summary>
    /// نشانی رقیب معتبر است؟ فقط http/https مطلق، با نام دامنه (نه IP یا localhost)، حداکثر ۵۰۰ نویسه و غیر از دامنهٔ خودمان.
    /// مثال: «https://www.digikala.com/product/dkp-1» ← (true، «digikala.com»)؛ «javascript:alert(1)» ← (false، null)؛ «http://127.0.0.1/x» ← (false، null).
    /// </summary>
    public static (bool Ok, string? Host) CheckUrl(string? url, string? ownHost = null)
    {
        if (string.IsNullOrWhiteSpace(url) || url.Length > 500)
            return (false, null);
        if (!Uri.TryCreate(url.Trim(), UriKind.Absolute, out var uri) || uri.Scheme is not ("http" or "https"))
            return (false, null);
        if (uri.HostNameType != UriHostNameType.Dns || !uri.Host.Contains('.') || uri.IsLoopback)
            return (false, null);
        var host = Host(uri.Host);
        if (!string.IsNullOrWhiteSpace(ownHost) && (host == Host(ownHost) || host.EndsWith("." + Host(ownHost), StringComparison.Ordinal)))
            return (false, null);
        return (true, host);
    }

    /// <summary>دامنه بدون www و با حروف کوچک. مثال: «WWW.Digikala.com» ← «digikala.com».</summary>
    public static string Host(string host)
    {
        var h = host.Trim().ToLowerInvariant();
        return h.StartsWith("www.", StringComparison.Ordinal) ? h[4..] : h;
    }

    /// <summary>
    /// قیمت رقیب پذیرفتنی است؟ مثال: (ما ۱٬۰۰۰٬۰۰۰، رقیب ۹۵۰٬۰۰۰) ← null (درست)؛ (ما ۱٬۰۰۰٬۰۰۰، رقیب ۱٬۰۰۰٬۰۰۰) ← «… کمتر نیست»؛
    /// (ما ۱٬۰۰۰٬۰۰۰، رقیب ۵۰۰٬۰۰۰) ← «اختلاف خیلی زیاد است …».
    /// </summary>
    public static string? CheckPrice(decimal ourPrice, decimal competitorPrice)
    {
        if (competitorPrice <= 0 || ourPrice <= 0)
            return "قیمت واردشده معتبر نیست.";
        if (competitorPrice >= ourPrice)
            return "قیمت واردشده از قیمت ما کمتر نیست.";
        if ((ourPrice - competitorPrice) * 100m / ourPrice > IPriceMatchService.MaxGapPercent)
            return "اختلاف قیمت خیلی زیاد است؛ معمولاً یعنی کالا یا شرایط فروش یکی نیست. اگر مطمئنید با پشتیبانی تماس بگیرید.";
        return null;
    }

    /// <summary>
    /// مبلغ تخفیف هنگام تأیید: اختلاف قیمت «فعلی» ما با قیمت رقیب، گرد به پایین تا هزار تومان. مثال: (۱٬۲۰۰٬۰۰۰، ۱٬۱۵۵٬۵۰۰) ← ۴۴٬۰۰۰.
    /// </summary>
    public static decimal Discount(decimal currentPrice, decimal competitorPrice) =>
        Math.Max(0, decimal.Floor((currentPrice - competitorPrice) / 1000m) * 1000m);
}

/// <inheritdoc cref="IPriceMatchService"/>
public sealed class PriceMatchService(
    FratakShopDbContext context,
    INotificationService notifications,
    IStaffNotificationService staff) : IPriceMatchService
{
    public async Task<OperationResult> SubmitAsync(Guid userId, PriceMatchInput input, string? ownHost, CancellationToken cancellationToken = default)
    {
        if (userId == Guid.Empty)
            return OperationResult.Error("برای ثبت درخواست ابتدا وارد حساب شوید.");

        var (urlOk, host) = PriceMatchRules.CheckUrl(input.CompetitorUrl, ownHost);
        if (!urlOk)
            return OperationResult.Error("نشانی صفحهٔ فروشگاه دیگر معتبر نیست (مثلاً https://…).");

        var now = DateTimeOffset.UtcNow;
        var product = await context.Products
            .Where(p => p.Id == input.ProductId && p.IsActive)
            .Select(p => new { p.Id, p.Name, p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt, p.StockQuantity })
            .FirstOrDefaultAsync(cancellationToken);
        if (product is null)
            return OperationResult.NotFound("کالا پیدا نشد.");
        if (product.StockQuantity <= 0)
            return OperationResult.Error("این کالا الان موجود نیست.");

        var ourPrice = FratakShopDbContext.FinalPrice(product.Price, product.DiscountPrice, product.DiscountStartAt, product.DiscountEndAt, now);
        if (PriceMatchRules.CheckPrice(ourPrice, input.CompetitorPrice) is { } priceError)
            return OperationResult.Error(priceError);

        var since = now.AddDays(-1);
        if (await context.PriceMatchRequests.CountAsync(r => r.UserId == userId && r.CreatedAt >= since, cancellationToken) >= IPriceMatchService.DailyLimit)
            return OperationResult.Error("امروز به اندازهٔ کافی درخواست ثبت کرده‌اید؛ لطفاً فردا دوباره امتحان کنید.");
        if (await context.PriceMatchRequests.AnyAsync(r => r.UserId == userId && r.ProductId == product.Id && r.Status == PriceMatchStatus.Pending, cancellationToken))
            return OperationResult.Error("درخواست قبلی شما برای این کالا در حال بررسی است.");

        var request = new PriceMatchRequest
        {
            UserId = userId,
            ProductId = product.Id,
            ProductName = product.Name.Length > 300 ? product.Name[..300] : product.Name,
            CompetitorUrl = input.CompetitorUrl!.Trim(),
            CompetitorPrice = decimal.Floor(input.CompetitorPrice),
            OurPrice = ourPrice,
            Status = PriceMatchStatus.Pending,
        };
        context.PriceMatchRequests.Add(request);
        await context.SaveChangesAsync(cancellationToken);

        await staff.NotifyAsync(new StaffAlert(StaffNotificationKind.Approval,
            "درخواست تضمین قیمت",
            $"{request.ProductName} – {host}: {request.CompetitorPrice:N0} تومان (قیمت ما {ourPrice:N0})",
            "/admin/marketing/price-match",
            [AdminPermissions.MarketingManage]));

        return OperationResult.Success("درخواست شما ثبت شد. پس از بررسی (معمولاً کمتر از یک روز کاری) نتیجه را در «اعلان‌ها» می‌بینید.");
    }

    public async Task<List<PriceMatchRow>> ListAsync(PriceMatchStatus? status, int take = 100, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        var query = context.PriceMatchRequests.AsQueryable();
        if (status is { } s)
            query = query.Where(r => r.Status == s);
        var rows = await query
            .OrderBy(r => r.Status == PriceMatchStatus.Pending ? 0 : 1).ThenByDescending(r => r.CreatedAt)
            .Take(Math.Clamp(take, 1, 500))
            .Select(r => new
            {
                Request = r,
                User = context.Users.Where(u => u.Id == r.UserId).Select(u => new { u.FirstName, u.LastName, u.PhoneNumber }).FirstOrDefault(),
                Product = context.Products.Where(p => p.Id == r.ProductId).Select(p => new { p.Slug, p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt }).FirstOrDefault(),
            })
            .ToListAsync(cancellationToken);

        return rows.Select(x =>
        {
            var r = x.Request;
            var name = x.User is null ? null : $"{x.User.FirstName} {x.User.LastName}".Trim();
            var current = x.Product is null ? r.OurPrice
                : FratakShopDbContext.FinalPrice(x.Product.Price, x.Product.DiscountPrice, x.Product.DiscountStartAt, x.Product.DiscountEndAt, now);
            return new PriceMatchRow(r.Id, r.UserId, string.IsNullOrWhiteSpace(name) ? null : name, x.User?.PhoneNumber,
                r.ProductId, r.ProductName, x.Product?.Slug, r.CompetitorUrl, PriceMatchRules.CheckUrl(r.CompetitorUrl).Host ?? "-",
                r.CompetitorPrice, r.OurPrice, current, r.Status, r.AdminNote, r.CouponCode, r.CreatedAt, r.ReviewedAt, r.ReviewerName);
        }).ToList();
    }

    public Task<int> PendingCountAsync(CancellationToken cancellationToken = default) =>
        context.PriceMatchRequests.CountAsync(r => r.Status == PriceMatchStatus.Pending, cancellationToken);

    public async Task<OperationResult> ApproveAsync(Guid id, string reviewer, string? note, CancellationToken cancellationToken = default)
    {
        var request = await context.PriceMatchRequests.AsTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
            return OperationResult.NotFound("درخواست پیدا نشد.");
        if (request.Status != PriceMatchStatus.Pending)
            return OperationResult.Error("این درخواست قبلاً بررسی شده است.");

        var now = DateTimeOffset.UtcNow;
        var product = await context.Products
            .Where(p => p.Id == request.ProductId)
            .Select(p => new { p.Slug, p.Price, p.DiscountPrice, p.DiscountStartAt, p.DiscountEndAt })
            .FirstOrDefaultAsync(cancellationToken);
        if (product is null)
            return OperationResult.NotFound("کالا دیگر وجود ندارد.");

        var current = FratakShopDbContext.FinalPrice(product.Price, product.DiscountPrice, product.DiscountStartAt, product.DiscountEndAt, now);
        var amount = PriceMatchRules.Discount(current, request.CompetitorPrice);
        if (amount <= 0)
            return OperationResult.Error("قیمت فعلی ما دیگر بیشتر از قیمت رقیب نیست؛ لطفاً درخواست را با توضیح «قیمت به‌روز شد» رد کنید.");

        string code;
        do
        {
            code = "PM-" + RandomCode(6);
        }
        while (await context.Coupons.IgnoreQueryFilters().AnyAsync(c => c.Code == code, cancellationToken));

        var coupon = new Coupon
        {
            Code = code,
            Title = Cut($"تضمین قیمت – {request.ProductName}", 200),
            Description = "کد شخصی تضمین قیمت؛ فقط برای همین کالا و یک بار.",
            Type = CouponType.FixedAmount,
            Value = amount,
            MinOrderAmount = 0,
            StartsAt = now,
            EndsAt = now.AddHours(IPriceMatchService.CouponHours),
            UsageLimit = 1,
            UsageLimitPerUser = 1,
            UserId = request.UserId,
            IsPublic = false,
            IsActive = true,
        };
        coupon.Targets.Add(new CouponTarget { TargetType = CouponTargetType.Product, TargetId = request.ProductId });
        context.Coupons.Add(coupon);

        request.Status = PriceMatchStatus.Approved;
        request.CouponCode = code;
        request.AdminNote = Cut(note, 500);
        request.ReviewedAt = now;
        request.ReviewerName = Cut(reviewer, 80);
        await context.SaveChangesAsync(cancellationToken);

        await notifications.NotifyAsync(new NotificationRequest
        {
            UserId = request.UserId,
            Category = NotificationCategory.Promotion,
            LinkUrl = product.Slug is null ? null : "/product/" + product.Slug,
            Content = new NotificationContent
            {
                Title = "درخواست تضمین قیمت تأیید شد",
                Body = $"برای «{request.ProductName}» کد تخفیف {code} به مبلغ {amount:N0} تومان ساخته شد. این کد فقط برای همین کالا و تا {IPriceMatchService.CouponHours} ساعت معتبر است.",
                LinkText = "دیدن کالا",
                SmsText = $"تضمین قیمت تأیید شد. کد تخفیف شما: {code} ({amount:N0} تومان، {IPriceMatchService.CouponHours} ساعت)",
            },
        });
        return OperationResult.Success($"تأیید شد؛ کد {code} به مبلغ {amount:N0} تومان برای مشتری ساخته و ارسال شد.");
    }

    public async Task<OperationResult> RejectAsync(Guid id, string reviewer, string? note, CancellationToken cancellationToken = default)
    {
        var request = await context.PriceMatchRequests.AsTracking().FirstOrDefaultAsync(r => r.Id == id, cancellationToken);
        if (request is null)
            return OperationResult.NotFound("درخواست پیدا نشد.");
        if (request.Status != PriceMatchStatus.Pending)
            return OperationResult.Error("این درخواست قبلاً بررسی شده است.");

        var reason = string.IsNullOrWhiteSpace(note) ? "کالا یا شرایط فروش در فروشگاه دیگر یکسان نبود." : note.Trim();
        request.Status = PriceMatchStatus.Rejected;
        request.AdminNote = Cut(reason, 500);
        request.ReviewedAt = DateTimeOffset.UtcNow;
        request.ReviewerName = Cut(reviewer, 80);
        await context.SaveChangesAsync(cancellationToken);

        await notifications.NotifyAsync(new NotificationRequest
        {
            UserId = request.UserId,
            Category = NotificationCategory.Promotion,
            Content = new NotificationContent
            {
                Title = "نتیجهٔ درخواست تضمین قیمت",
                Body = $"درخواست شما برای «{request.ProductName}» تأیید نشد. دلیل: {request.AdminNote}",
                Push = false,
            },
        });
        return OperationResult.Success("درخواست رد شد و به مشتری اطلاع داده شد.");
    }

    private static string? Cut(string? value, int max) =>
        string.IsNullOrWhiteSpace(value) ? null : value.Trim().Length > max ? value.Trim()[..max] : value.Trim();

    private static string RandomCode(int length)
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        return string.Create(length, alphabet, (span, chars) =>
        {
            for (var i = 0; i < span.Length; i++)
                span[i] = chars[RandomNumberGenerator.GetInt32(chars.Length)];
        });
    }
}
