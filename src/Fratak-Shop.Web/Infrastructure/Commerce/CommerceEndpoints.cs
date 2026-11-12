using System.Text.Json;
using Fratak_Shop.Core.Services.Marketing;
using Fratak_Shop.Core.Services.Products;
using Fratak_Shop.Core.Services.Reports;
using Fratak_Shop.Core.Services.Searches;
using Fratak_Shop.Core.Services.Shopping;
using Fratak_Shop.Core.Services.Smart;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Infrastructure.Analytics;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Antiforgery;

namespace Fratak_Shop.Web.Infrastructure.Commerce;

// ═══════════════════════════════════════════════════════════════════════════
//  دور ۲۴ (بستهٔ B) – API های «تجربهٔ خرید» برای assets/js/commerce.js
//   POST /api/search/click        ثبت کلیک روی نتیجهٔ جست‌وجو (رتبه‌بندی یادگیرنده، پیشنهاد ۳۰)
//   POST /api/search/image        جست‌وجو با عکس (پیشنهاد ۲۹)
//   GET  /api/products/{id}/quick نمایش سریع کالا در پنجره (پیشنهاد ۳۱)
//   GET  /api/products/{id}/proof آمار واقعی خرید اخیر (پیشنهاد ۱۵)
//   GET  /api/compare | POST /api/compare  سینی مقایسه (پیشنهاد ۳۲)
//   POST /api/price-match         «قیمت کمتر دیدید؟» (پیشنهاد ۱۱)
//  همهٔ POST ها توکن ضدجعل (هدر RequestVerificationToken) می‌خواهند و بدنه‌شان سقف دارد. پاسخ‌های شخصی no-store هستند.
//  ثبت: Program.cs (MapCommerceEndpoints).
// ═══════════════════════════════════════════════════════════════════════════

/// <summary>API های تجربهٔ خرید (دور ۲۴). جزئیات هر مسیر در توضیح بالای همین فایل.</summary>
public static class CommerceEndpoints
{
    /// <summary>سقف بدنهٔ JSON (۲ کیلوبایت).</summary>
    public const int MaxBody = 2048;

    private const string Expired = "نشست شما منقضی شده است؛ صفحه را دوباره بارگذاری کنید.";

    public static void MapCommerceEndpoints(this IEndpointRouteBuilder app)
    {
        // ── رتبه‌بندی یادگیرنده: {"q":"هدفون","productId":"…"} ← 204. ربات‌ها شمرده نمی‌شوند.
        app.MapPost("/api/search/click", async (HttpContext http, IAntiforgery antiforgery, ISearchLearningService learning) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (http.Request.ContentLength is > MaxBody)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            if (!await antiforgery.IsRequestValidAsync(http))
                return Results.BadRequest(new { error = Expired });
            var input = await ReadAsync<ClickInput>(http);
            if (input is null || input.ProductId == Guid.Empty || string.IsNullOrWhiteSpace(input.Q))
                return Results.BadRequest(new { error = "درخواست نامعتبر است." });
            if (TrafficRules.IsBot(http.Request.Headers.UserAgent.ToString()))
                return Results.NoContent();
            await learning.RecordClickAsync(input.Q, input.ProductId, VisitorKey(http), http.RequestAborted);
            return Results.NoContent();
        }).DisableAntiforgery();

        // ── جست‌وجو با عکس: فرم چندبخشی با فیلد image (حداکثر ۴ مگابایت) ← {products:[…]} یا {error}.
        app.MapPost("/api/search/image", async (HttpContext http, IAntiforgery antiforgery, ISmartConfigService smart, ImageSearchService images) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (!(await smart.GetAsync(http.RequestAborted)).ImageSearch)
                return Results.NotFound();
            // دور ۲۸ (علت اصلی خرابی): قبلاً درخواست بدون سرآیند Content-Length (HTTP/2 یا پراکسی Plesk که بدنه را تکه‌تکه
            // می‌فرستد) همیشه ۴۱۳ می‌گرفت. حالا فقط وقتی طول اعلام‌شده زیاد است رد می‌شود و سقف واقعی را خود Kestrel/IIS
            // (MaxRequestBodySize) و FormOptions هنگام خواندن فرم اعمال می‌کنند.
            if (http.Request.ContentLength is > ImageSignature.MaxBytes + 64 * 1024)
                return Results.Json(new { error = "حجم عکس حداکثر ۴ مگابایت است." }, statusCode: StatusCodes.Status413PayloadTooLarge);
            if (!http.Request.HasFormContentType)
                return Results.BadRequest(new { error = "عکسی انتخاب نشده است." });
            if (!await antiforgery.IsRequestValidAsync(http))
                return Results.BadRequest(new { error = Expired });

            var bodyLimit = http.Features.Get<Microsoft.AspNetCore.Http.Features.IHttpMaxRequestBodySizeFeature>();
            if (bodyLimit is { IsReadOnly: false })
                bodyLimit.MaxRequestBodySize = ImageSignature.MaxBytes + 64 * 1024;
            IFormCollection form;
            try
            {
                form = await http.Request.ReadFormAsync(new Microsoft.AspNetCore.Http.Features.FormOptions
                {
                    MultipartBodyLengthLimit = ImageSignature.MaxBytes + 64 * 1024,
                    ValueCountLimit = 8,
                }, http.RequestAborted);
            }
            catch (Exception exception) when (exception is InvalidDataException or BadHttpRequestException or IOException)
            {
                return Results.Json(new { error = "حجم عکس حداکثر ۴ مگابایت است." }, statusCode: StatusCodes.Status413PayloadTooLarge);
            }
            var file = form.Files.GetFile("image");
            if (file is null || file.Length == 0 || file.Length > ImageSignature.MaxBytes)
                return Results.BadRequest(new { error = "عکسی انتخاب نشده یا حجمش بیش از ۴ مگابایت است." });

            byte[] data;
            await using (var stream = file.OpenReadStream())
            {
                using var buffer = new MemoryStream((int)file.Length);
                await stream.CopyToAsync(buffer, http.RequestAborted);
                data = buffer.ToArray();
            }

            // دور ۲۹ (#۱): متن کادر جست‌وجو (اختیاری) برای دقیق‌تر کردن نتیجه؛ حداکثر ۶۰ نویسه.
            var hint = form["q"].ToString().Trim();
            var result = await images.SearchAsync(data, hint.Length > 60 ? hint[..60] : hint, http.RequestAborted);
            if (result.Busy)
                return Results.Json(new { error = "سرور الان شلوغ است؛ چند ثانیه بعد دوباره امتحان کنید." }, statusCode: StatusCodes.Status503ServiceUnavailable);
            if (result.Invalid)
                return Results.BadRequest(new { error = "این فایل عکس معتبری نیست (JPG، PNG یا WebP)." });
            return Results.Ok(new { products = result.Products.Select(Card), approximate = result.Approximate });
        }).DisableAntiforgery();

        // ── نمایش سریع کالا (بدون رفتن به صفحهٔ کالا). کش عمومی ۶۰ ثانیه.
        app.MapGet("/api/products/{id:guid}/quick", async (Guid id, HttpContext http, IProductService products) =>
        {
            var product = await products.GetProductByIdAsync(id);
            if (product is null || !product.IsActive)
                return Results.NotFound();
            http.Response.Headers.CacheControl = "public, max-age=60";
            var summary = (product.ShortDescription ?? product.Description).ToPlainText(280);
            return Results.Ok(new
            {
                id = product.Id,
                name = product.Name,
                url = "/product/" + product.Slug,
                brand = product.BrandName,
                category = product.CategoryName,
                images = product.Images.OrderByDescending(i => i.IsPrimary).ThenBy(i => i.DisplayOrder).Take(4)
                    .Select(i => new { src = ImageHelper.Product(i.ImageName), alt = i.AltText ?? product.Name }),
                price = Format.Toman(product.FinalPrice),
                oldPrice = product.HasActiveDiscount ? Format.Price(product.Price) : null,
                discount = product.DiscountPercent,
                inStock = product.IsInStock,
                rating = product.AverageRating > 0 ? Math.Round(product.AverageRating, 1) : (double?)null,
                reviews = product.ReviewCount,
                warranty = product.WarrantyTitle,
                summary = string.IsNullOrWhiteSpace(summary) ? null : summary,
                specs = product.Features.Where(f => f.ValueType != FeatureValueType.Object && f.Value.Length <= 120)
                    .OrderBy(f => f.DisplayOrder).Take(6).Select(f => new { key = f.Key, value = f.Value }),
            });
        });

        // ── آمار واقعی خرید اخیر ← {text} (text خالی یعنی چیزی نشان داده نشود). کش عمومی ۵ دقیقه.
        app.MapGet("/api/products/{id:guid}/proof", async (Guid id, HttpContext http, ISmartConfigService smart, ISocialProofService proof) =>
        {
            http.Response.Headers.CacheControl = "public, max-age=300";
            if (!(await smart.GetAsync(http.RequestAborted)).SocialProof)
                return Results.Ok(new { text = (string?)null });
            var result = await proof.GetAsync(id, http.RequestAborted);
            return Results.Ok(new { text = result.Text });
        });

        // ── سینی مقایسه: فهرست فعلی (از کوکی) ← {items:[{id,name,url,image}], max}.
        app.MapGet("/api/compare", async (HttpContext http, ICompareListService compare, IProductService products) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            var ids = compare.GetProductIds();
            var cards = ids.Count == 0 ? new List<Fratak_Shop.Core.DTOs.Products.Query.ProductFilterData>() : await products.GetCardsByIdsAsync(ids);
            return Results.Ok(new
            {
                max = ICompareListService.MaxItems,
                items = cards.Select(c => new { id = c.Id, name = c.Name, url = "/product/" + c.Slug, image = ImageHelper.Product(c.PrimaryImageName) }),
            });
        });

        // ── افزودن/حذف/خالی کردن مقایسه: {"productId":"…"} | {"productId":"…","remove":true} | {"clear":true}.
        app.MapPost("/api/compare", async (HttpContext http, IAntiforgery antiforgery, ICompareListService compare, IProductService products) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (http.Request.ContentLength is > MaxBody)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            if (!await antiforgery.IsRequestValidAsync(http))
                return Results.BadRequest(new { error = Expired });
            var input = await ReadAsync<CompareInput>(http);
            if (input is null)
                return Results.BadRequest(new { error = "درخواست نامعتبر است." });

            if (input.Clear)
            {
                compare.Clear();
                return Results.Ok(new { ok = true, count = 0, message = "فهرست مقایسه خالی شد." });
            }
            if (input.ProductId is not { } productId || productId == Guid.Empty)
                return Results.BadRequest(new { error = "کالا مشخص نیست." });
            if (input.Remove)
            {
                compare.Remove(productId);
                return Results.Ok(new { ok = true, count = compare.GetProductIds().Count(i => i != productId), message = "از مقایسه حذف شد." });
            }
            if ((await products.GetCardsByIdsAsync([productId])).Count == 0)
                return Results.NotFound(new { error = "کالا پیدا نشد." });
            var before = compare.GetProductIds();
            if (!compare.Add(productId))
                return Results.Ok(new { ok = false, count = before.Count, message = $"حداکثر {ICompareListService.MaxItems} کالا را می‌توانید مقایسه کنید." });
            var count = before.Contains(productId) ? before.Count : before.Count + 1;
            return Results.Ok(new { ok = true, count, message = "به مقایسه اضافه شد." });
        }).DisableAntiforgery();

        // ── تضمین قیمت: {"productId":"…","url":"https://…","price":1150000} ← {ok, message}. فقط مشتری واردشده.
        app.MapPost("/api/price-match", async (HttpContext http, IAntiforgery antiforgery, ISmartConfigService smart, IPriceMatchService priceMatch) =>
        {
            http.Response.Headers.CacheControl = "no-store";
            if (!(await smart.GetAsync(http.RequestAborted)).PriceMatchEnabled)
                return Results.NotFound();
            if (http.Request.ContentLength is > MaxBody)
                return Results.StatusCode(StatusCodes.Status413PayloadTooLarge);
            if (!await antiforgery.IsRequestValidAsync(http))
                return Results.BadRequest(new { error = Expired });
            if (http.User.GetUserId() is not { } userId)
                return Results.Json(new { error = "برای ثبت درخواست ابتدا وارد حساب شوید." }, statusCode: StatusCodes.Status401Unauthorized);
            var input = await ReadAsync<PriceMatchBody>(http);
            if (input is null || input.ProductId == Guid.Empty)
                return Results.BadRequest(new { error = "درخواست نامعتبر است." });

            var result = await priceMatch.SubmitAsync(userId, new PriceMatchInput(input.ProductId, input.Url, input.Price), http.Request.Host.Host, http.RequestAborted);
            return result.IsSuccess()
                ? Results.Ok(new { ok = true, message = result.Message })
                : Results.Ok(new { ok = false, message = result.Message });
        }).DisableAntiforgery();
    }

    /// <summary>بدنهٔ ثبت کلیک.</summary>
    public sealed record ClickInput(string? Q, Guid ProductId);

    /// <summary>بدنهٔ مقایسه.</summary>
    public sealed record CompareInput(Guid? ProductId, bool Remove = false, bool Clear = false);

    /// <summary>بدنهٔ تضمین قیمت (قیمت به تومان).</summary>
    public sealed record PriceMatchBody(Guid ProductId, string? Url, decimal Price);

    /// <summary>کارت کالا برای نتیجهٔ جست‌وجوی تصویری.</summary>
    private static object Card(Fratak_Shop.Core.DTOs.Products.Query.ProductFilterData p) => new
    {
        id = p.Id,
        name = p.Name,
        url = "/product/" + p.Slug,
        image = ImageHelper.Product(p.PrimaryImageName),
        price = p.IsInStock ? Format.Toman(p.FinalPrice) : "ناموجود",
        inStock = p.IsInStock,
    };

    /// <summary>
    /// شناسهٔ ناشناس بازدیدکننده برای جلوگیری از کلیک تکراری: کوکی آمار (fr_vid)، وگرنه کاربر واردشده، وگرنه IP.
    /// مثال: «v-abc…» یا «u-{guid}» یا «ip-1.2.3.4». هیچ‌جا ذخیره نمی‌شود (فقط کلید حافظهٔ موقت).
    /// </summary>
    public static string VisitorKey(HttpContext http)
    {
        var key = AnalyticsCookies.ReadVisitor(http) is { } visitor ? "v-" + visitor
            : http.User.GetUserId() is { } user ? "u-" + user.ToString("N")
            : "ip-" + (http.Connection.RemoteIpAddress?.ToString() ?? "none");
        return key.Length > 64 ? key[..64] : key;
    }

    /// <summary>خواندن بدنهٔ JSON با سقف <see cref="MaxBody"/>؛ بدنهٔ خراب یا بزرگ ← null.</summary>
    private static async Task<T?> ReadAsync<T>(HttpContext http) where T : class
    {
        var buffer = new byte[MaxBody + 1];
        var total = 0;
        int read;
        while (total <= MaxBody && (read = await http.Request.Body.ReadAsync(buffer.AsMemory(total, buffer.Length - total), http.RequestAborted)) > 0)
            total += read;
        if (total == 0 || total > MaxBody)
            return null;
        try
        {
            return JsonSerializer.Deserialize<T>(buffer.AsSpan(0, total), JsonSerializerOptions.Web);
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
