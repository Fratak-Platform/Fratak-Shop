using System.Globalization;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Admin;

/// <summary>بخش‌هایی که جست‌وجوی سریع پنل در آن‌ها می‌گردد (لایهٔ وب بر اساس مجوز کارمند تعیین می‌کند).</summary>
[Flags]
public enum AdminSearchScope
{
    None = 0,
    Orders = 1,
    Customers = 2,
    Products = 4,
    Tickets = 8,
}

/// <summary>یک نتیجهٔ جست‌وجوی سریع. مثال: («سفارش‌ها»، «FR-140803-1021»، «علی رضایی · 1,250,000 تومان»، «/admin/orders/…»، «Orders»).</summary>
public sealed record AdminSearchHit(string Group, string Title, string? Subtitle, string Url, string Icon);

/// <summary>
/// جست‌وجوی سراسری پنل (پنجرهٔ <kbd>Ctrl</kbd>+<kbd>K</kbd>).
/// <para>
/// ورودی با <see cref="SearchText"/> یکدست می‌شود (ی/ک عربی، ارقام فارسی). اگر ورودی فقط عدد باشد در شمارهٔ سفارش،
/// موبایل، کد کالا و شمارهٔ تیکت می‌گردد؛ وگرنه در نام مشتری، نام محصول و موضوع تیکت.
/// از هر بخش حداکثر <c>take</c> نتیجه برمی‌گردد تا پاسخ سریع بماند.
/// </para>
/// محل استفاده: هندلر <c>Search</c> در صفحهٔ <c>/admin/ux</c>.
/// </summary>
public interface IAdminSearchService
{
    Task<List<AdminSearchHit>> SearchAsync(string? query, AdminSearchScope scopes, int take = 5, CancellationToken cancellationToken = default);
}

public sealed class AdminSearchService(FratakShopDbContext context) : IAdminSearchService
{
    private static readonly CultureInfo Invariant = CultureInfo.InvariantCulture;

    public async Task<List<AdminSearchHit>> SearchAsync(string? query, AdminSearchScope scopes, int take = 5, CancellationToken cancellationToken = default)
    {
        var text = SearchText.Normalize(query);
        if (text.Length < 2 || scopes == AdminSearchScope.None)
            return [];

        take = Math.Clamp(take, 1, 10);
        var digits = new string(text.Where(char.IsAsciiDigit).ToArray());
        var numeric = digits.Length >= 3 && text.All(c => char.IsAsciiDigit(c) || c is '-' or ' ' or '+');
        // موبایل با ۰ یا ۹۸ شروع می‌شود؛ برای جست‌وجو فقط ۱۰ رقم آخر مهم است (9121234567).
        var phone = numeric && digits.Length >= 4 ? (digits.StartsWith("98") && digits.Length > 10 ? digits[2..] : digits.TrimStart('0')) : null;
        var hits = new List<AdminSearchHit>();

        if (scopes.HasFlag(AdminSearchScope.Orders))
        {
            var orders = context.Orders.AsQueryable();
            orders = numeric
                ? orders.Where(o => o.OrderNumber.Contains(text) || o.OrderNumber.Contains(digits) || (phone != null && o.User != null && o.User.PhoneNumber.Contains(phone)))
                : orders.Where(o => o.OrderNumber.Contains(text) || (o.User != null && ((o.User.FirstName ?? "") + " " + (o.User.LastName ?? "")).Contains(text)));
            var rows = await orders.OrderByDescending(o => o.CreatedAt).Take(take)
                .Select(o => new { o.Id, o.OrderNumber, o.PayableAmount, First = o.User != null ? o.User.FirstName : null, Last = o.User != null ? o.User.LastName : null })
                .ToListAsync(cancellationToken);
            hits.AddRange(rows.Select(o => new AdminSearchHit("سفارش‌ها", o.OrderNumber,
                $"{Name(o.First, o.Last)} · {o.PayableAmount.ToString("N0", Invariant)} تومان", $"/admin/orders/{o.Id}", "Orders")));
        }

        if (scopes.HasFlag(AdminSearchScope.Customers))
        {
            var users = context.Users.AsQueryable();
            users = numeric
                ? users.Where(u => phone != null && u.PhoneNumber.Contains(phone))
                : users.Where(u => ((u.FirstName ?? "") + " " + (u.LastName ?? "")).Contains(text) || (u.Email != null && u.Email.Contains(text)));
            var rows = await users.OrderByDescending(u => u.CreatedAt).Take(take)
                .Select(u => new { u.Id, u.FirstName, u.LastName, u.PhoneNumber }).ToListAsync(cancellationToken);
            hits.AddRange(rows.Select(u => new AdminSearchHit("مشتریان", Name(u.FirstName, u.LastName), u.PhoneNumber, $"/admin/customers/{u.Id}", "User")));
        }

        if (scopes.HasFlag(AdminSearchScope.Products))
        {
            var terms = SearchText.Terms(text);
            var products = context.Products.AsQueryable();
            if (numeric)
                products = products.Where(p => p.SKU.Contains(digits) || p.Name.Contains(digits));
            else
                foreach (var term in terms)
                    products = products.Where(p => p.Name.Contains(term) || p.SKU.Contains(term) || (p.EnglishName != null && p.EnglishName.Contains(term)));
            var rows = await products.OrderByDescending(p => p.IsActive).ThenByDescending(p => p.SalesCount).Take(take)
                .Select(p => new { p.Id, p.Name, p.SKU, p.Price, p.StockQuantity }).ToListAsync(cancellationToken);
            hits.AddRange(rows.Select(p => new AdminSearchHit("محصولات", p.Name,
                $"{p.SKU} · {p.Price.ToString("N0", Invariant)} تومان · موجودی {p.StockQuantity}", $"/admin/products/edit/{p.Id}", "Package")));
        }

        if (scopes.HasFlag(AdminSearchScope.Tickets))
        {
            var tickets = context.SupportTickets.AsQueryable();
            tickets = numeric ? tickets.Where(t => t.TicketNumber.Contains(digits)) : tickets.Where(t => t.Subject.Contains(text) || t.TicketNumber.Contains(text));
            var rows = await tickets.OrderByDescending(t => t.CreatedAt).Take(take)
                .Select(t => new { t.Id, t.TicketNumber, t.Subject }).ToListAsync(cancellationToken);
            hits.AddRange(rows.Select(t => new AdminSearchHit("تیکت‌ها", t.Subject, t.TicketNumber, $"/admin/tickets/{t.Id}", "Lifebuoy")));
        }

        return hits;
    }

    private static string Name(string? first, string? last)
    {
        var name = $"{first} {last}".Trim();
        return name.Length == 0 ? "بدون نام" : name;
    }
}
