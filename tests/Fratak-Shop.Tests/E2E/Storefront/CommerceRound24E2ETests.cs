using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;
using Fratak_Shop.Data.Entities.Marketing;
using Fratak_Shop.Data.Entities.Orders;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Tests.E2E.Infrastructure;
using Fratak_Shop.Web.Infrastructure.Commerce;
using Fratak_Shop.Web.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SkiaSharp;

namespace Fratak_Shop.Tests.E2E.Storefront;

/// <summary>
/// تست سرتاسری دور ۲۴ (بستهٔ B – تجربهٔ خرید) روی برنامهٔ واقعی (TestServer + SQL Server).
/// <list type="bullet">
/// <item>نمایش سریع: JSON کالای فعال؛ کالای غیرفعال/ناموجود ← 404.</item>
/// <item>مقایسه: بدون توکن ← 400؛ افزودن ← کوکی شمارش fr_cmp و فهرست /api/compare؛ خالی کردن.</item>
/// <item>تضمین قیمت: مهمان ← 401؛ بدون توکن ← 400؛ مشتری ← ثبت؛ نشانی مخرب ← رد؛ پنل مدیر تأیید می‌کند و کوپن ساخته می‌شود؛ مشتری پنل را نمی‌بیند.</item>
/// <item>سفارش دوباره از صفحهٔ سفارش ← 302 به /cart و کالا در سبد.</item>
/// <item>جست‌وجو با عکس: فایل غیرتصویری ← 400؛ بدون توکن ← 400؛ عکس واقعی کالا ← همان کالا پیدا می‌شود.</item>
/// <item>جست‌وجو: پیش‌فرض «مرتبط‌ترین»، نشانهٔ data-search-q و ثبت کلیک.</item>
/// <item>نشانه‌های رابط کاربری (نمایش سریع، مقایسه، صوتی، عکس، آمار خرید، تضمین قیمت) در صفحه‌ها هست.</item>
/// </list>
/// </summary>
[Collection(E2ECollection.Name)]
[Trait("Category", "E2E")]
[Trait("Area", "Commerce")]
public class CommerceRound24E2ETests(ShopApp app)
{
    private static async Task<HttpResponseMessage> PostJsonAsync(ShopClient client, string url, object body, string? token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, url)
        {
            Content = new StringContent(JsonSerializer.Serialize(body), Encoding.UTF8, "application/json"),
        };
        if (token is not null)
            request.Headers.Add("RequestVerificationToken", token);
        return await client.Http.SendAsync(request);
    }

    private static async Task<JsonElement> JsonAsync(HttpResponseMessage response)
    {
        var text = await response.Content.ReadAsStringAsync();
        using var document = JsonDocument.Parse(text);
        return document.RootElement.Clone();
    }

    private static byte[] Png(SKColor tint, bool leftToRight = true)
    {
        using var bitmap = new SKBitmap(new SKImageInfo(64, 64, SKColorType.Rgba8888, SKAlphaType.Unpremul));
        for (var x = 0; x < 64; x++)
        {
            var t = leftToRight ? x / 63f : 1 - x / 63f;
            var c = new SKColor((byte)(tint.Red * (1 - t)), (byte)(tint.Green * (1 - t)), (byte)(tint.Blue * (1 - t)));
            for (var y = 0; y < 64; y++)
                bitmap.SetPixel(x, y, c);
        }
        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Png, 100);
        return data.ToArray();
    }

    private static async Task<HttpResponseMessage> PostImageAsync(ShopClient client, byte[] bytes, string fileName, string contentType, string? token)
    {
        using var form = new MultipartFormDataContent();
        var file = new ByteArrayContent(bytes);
        file.Headers.ContentType = new MediaTypeHeaderValue(contentType);
        form.Add(file, "image", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/search/image") { Content = form };
        if (token is not null)
            request.Headers.Add("RequestVerificationToken", token);
        return await client.Http.SendAsync(request);
    }

    // ── نمایش سریع ──────────────────────────────────────────────

    [SqlFact]
    public async Task QuickView_returns_product_json_and_hides_inactive()
    {
        var (id, slug) = await app.CreateProductAsync(price: 420_000, stock: 4, discountPrice: 400_000);
        var (inactive, _) = await app.CreateProductAsync(active: false);
        using var client = app.CreateClient();

        using var response = await client.GetAsync($"/api/products/{id}/quick");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await JsonAsync(response);
        Assert.Equal("/product/" + slug, json.GetProperty("url").GetString());
        Assert.StartsWith("کالای آزمون", json.GetProperty("name").GetString());
        Assert.True(json.GetProperty("inStock").GetBoolean());
        Assert.Contains("400,000", json.GetProperty("price").GetString());
        Assert.Equal("420,000", json.GetProperty("oldPrice").GetString());
        Assert.Equal(JsonValueKind.Array, json.GetProperty("images").ValueKind);
        Assert.Equal(JsonValueKind.Array, json.GetProperty("specs").ValueKind);

        using var hidden = await client.GetAsync($"/api/products/{inactive}/quick");
        Assert.Equal(HttpStatusCode.NotFound, hidden.StatusCode);
        using var missing = await client.GetAsync($"/api/products/{Guid.NewGuid()}/quick");
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using var bad = await client.GetAsync("/api/products/not-a-guid/quick");
        Assert.Equal(HttpStatusCode.NotFound, bad.StatusCode);
    }

    // ── مقایسه ─────────────────────────────────────────────────

    [SqlFact]
    public async Task Compare_requires_token_sets_count_cookie_and_lists_items()
    {
        var (first, _) = await app.CreateProductAsync();
        var (second, _) = await app.CreateProductAsync();
        using var client = app.CreateClient();

        using (var noToken = await PostJsonAsync(client, "/api/compare", new { productId = first }, null))
            Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        using (var forged = await PostJsonAsync(client, "/api/compare", new { productId = first }, "forged-token"))
            Assert.Equal(HttpStatusCode.BadRequest, forged.StatusCode);

        var token = await client.AntiforgeryTokenAsync();
        using (var added = await PostJsonAsync(client, "/api/compare", new { productId = first }, token))
        {
            Assert.Equal(HttpStatusCode.OK, added.StatusCode);
            var json = await JsonAsync(added);
            Assert.True(json.GetProperty("ok").GetBoolean());
            Assert.Equal(1, json.GetProperty("count").GetInt32());
            Assert.Equal("1", ShopClient.CookieValue(added, CompareListService.CountCookieName));
        }
        using (var again = await PostJsonAsync(client, "/api/compare", new { productId = second }, token))
            Assert.Equal(2, (await JsonAsync(again)).GetProperty("count").GetInt32());
        using (var missing = await PostJsonAsync(client, "/api/compare", new { productId = Guid.NewGuid() }, token))
            Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        using (var empty = await PostJsonAsync(client, "/api/compare", new { }, token))
            Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);

        using (var list = await client.GetAsync("/api/compare"))
        {
            var json = await JsonAsync(list);
            var ids = json.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetGuid()).ToList();
            Assert.Contains(first, ids);
            Assert.Contains(second, ids);
            Assert.Equal(4, json.GetProperty("max").GetInt32());
            Assert.Contains("no-store", list.Headers.CacheControl?.ToString());
        }

        // صفحهٔ مقایسه همان کالاها را نشان می‌دهد.
        var page = await client.GetPageAsync("/compare");
        Assert.Contains("کالای آزمون", page.Text);

        using (var cleared = await PostJsonAsync(client, "/api/compare", new { clear = true }, token))
            Assert.Equal(0, (await JsonAsync(cleared)).GetProperty("count").GetInt32());
        using (var list = await client.GetAsync("/api/compare"))
            Assert.Equal(0, (await JsonAsync(list)).GetProperty("items").GetArrayLength());
    }

    [SqlFact]
    public async Task Compare_rejects_oversized_body()
    {
        using var client = app.CreateClient();
        var token = await client.AntiforgeryTokenAsync();
        using var response = await PostJsonAsync(client, "/api/compare", new { productId = Guid.NewGuid(), junk = new string('x', 5000) }, token);
        Assert.True(response.StatusCode is HttpStatusCode.RequestEntityTooLarge or HttpStatusCode.BadRequest, response.StatusCode.ToString());
    }

    // ── تضمین قیمت ──────────────────────────────────────────────

    [SqlFact]
    public async Task PriceMatch_end_to_end_customer_submit_admin_approve()
    {
        var (productId, slug) = await app.CreateProductAsync(price: 1_000_000, stock: 3);
        var customer = await app.CreateCustomerAsync();
        try
        {
            // مهمان: صفحهٔ کالا دکمه را دارد ولی ثبت ← 401.
            using var guest = app.CreateClient();
            var productPage = await guest.GetPageAsync("/product/" + slug);
            Assert.NotNull(productPage.One($"[data-price-match='{productId}']"));
            Assert.NotNull(productPage.One($"[data-social-proof='{productId}']"));
            var guestToken = await guest.AntiforgeryTokenAsync();
            using (var anonymous = await PostJsonAsync(guest, "/api/price-match", new { productId, url = "https://a.ir/p", price = 950_000 }, guestToken))
                Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);

            using var client = app.CreateClient();
            await client.LoginAsync(customer.Phone, customer.Password);
            using (var noToken = await PostJsonAsync(client, "/api/price-match", new { productId, url = "https://a.ir/p", price = 950_000 }, null))
                Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);

            var token = await client.AntiforgeryTokenAsync();
            using (var evil = await PostJsonAsync(client, "/api/price-match", new { productId, url = "javascript:alert(document.cookie)", price = 950_000 }, token))
                Assert.False((await JsonAsync(evil)).GetProperty("ok").GetBoolean());
            using (var tooCheap = await PostJsonAsync(client, "/api/price-match", new { productId, url = "https://a.ir/p", price = 10_000 }, token))
                Assert.False((await JsonAsync(tooCheap)).GetProperty("ok").GetBoolean());
            using (var ok = await PostJsonAsync(client, "/api/price-match", new { productId, url = "https://a.ir/p", price = 950_000 }, token))
            {
                Assert.Equal(HttpStatusCode.OK, ok.StatusCode);
                var json = await JsonAsync(ok);
                Assert.True(json.GetProperty("ok").GetBoolean(), json.GetProperty("message").GetString());
            }

            var requestId = await app.WithDbAsync(db => db.PriceMatchRequests.Where(r => r.UserId == customer.Id).Select(r => r.Id).SingleAsync());

            // مشتری به پنل دسترسی ندارد.
            using (var denied = await client.GetAsync("/admin/marketing/price-match"))
                Assert.NotEqual(HttpStatusCode.OK, denied.StatusCode);

            // مدیر: فهرست را می‌بیند و تأیید می‌کند.
            using var admin = app.CreateClient();
            await admin.LoginAsync(ShopApp.OwnerPhone);
            var panel = await admin.GetPageAsync("/admin/marketing/price-match");
            Assert.Contains("a.ir", panel.Html);
            var link = panel.One("a[href='https://a.ir/p']");
            Assert.NotNull(link);
            Assert.Contains("nofollow", link!.GetAttribute("rel"));
            Assert.Equal("_blank", link.GetAttribute("target"));

            using (var approve = await admin.PostFormAsync($"/admin/marketing/price-match?handler=Approve&id={requestId}", ("note", "تأیید آزمون")))
                Assert.Equal(HttpStatusCode.Redirect, approve.StatusCode);

            var (status, code) = await app.WithDbAsync(async db =>
            {
                var row = await db.PriceMatchRequests.SingleAsync(r => r.Id == requestId);
                return (row.Status, row.CouponCode);
            });
            Assert.Equal(PriceMatchStatus.Approved, status);
            Assert.StartsWith("PM-", code);
            var coupon = await app.WithDbAsync(db => db.Coupons.Include(c => c.Targets).SingleAsync(c => c.Code == code));
            Assert.Equal(customer.Id, coupon.UserId);
            Assert.Equal(50_000m, coupon.Value);
            Assert.Equal(productId, Assert.Single(coupon.Targets).TargetId);
        }
        finally
        {
            await app.WithDbAsync(async db =>
            {
                var codes = await db.PriceMatchRequests.IgnoreQueryFilters().Where(r => r.UserId == customer.Id && r.CouponCode != null).Select(r => r.CouponCode!).ToListAsync();
                var couponIds = await db.Coupons.IgnoreQueryFilters().Where(c => codes.Contains(c.Code)).Select(c => c.Id).ToListAsync();
                await db.CouponTargets.IgnoreQueryFilters().Where(t => couponIds.Contains(t.CouponId)).ExecuteDeleteAsync();
                await db.Coupons.IgnoreQueryFilters().Where(c => couponIds.Contains(c.Id)).ExecuteDeleteAsync();
                await db.PriceMatchRequests.IgnoreQueryFilters().Where(r => r.UserId == customer.Id).ExecuteDeleteAsync();
                await db.StaffNotifications.IgnoreQueryFilters().Where(n => n.Url == "/admin/marketing/price-match").ExecuteDeleteAsync();
            });
        }
    }

    // ── سفارش دوباره ────────────────────────────────────────────

    [SqlFact]
    public async Task Reorder_from_order_page_fills_cart_and_redirects()
    {
        var (productId, _) = await app.CreateProductAsync(stock: 5);
        var customer = await app.CreateCustomerAsync();
        var number = "RO" + Random.Shared.NextInt64(100_000_000, 999_999_999);
        await app.WithDbAsync(async db =>
        {
            var order = new Order
            {
                OrderNumber = number,
                UserId = customer.Id,
                Status = OrderStatus.Delivered,
                PaymentMethod = PaymentMethod.OnlineGateway,
                PaymentStatus = PaymentStatus.Paid,
                SubtotalAmount = 500_000,
                PayableAmount = 500_000,
                CustomerName = "مشتری آزمون",
                CustomerPhone = customer.Phone,
                ShippingProvince = "تهران",
                ShippingCity = "تهران",
                ShippingAddress = "خیابان آزمون",
                ShippingPostalCode = "1234567890",
                ShippingPhone = customer.Phone,
            };
            order.Items.Add(new OrderItem { ProductId = productId, ProductName = "کالای سفارش قبلی", SKU = "RO-1", UnitPrice = 250_000, Quantity = 2, TotalAmount = 500_000 });
            db.Orders.Add(order);
            await db.SaveChangesAsync();
        });

        using var client = app.CreateClient();
        await client.LoginAsync(customer.Phone, customer.Password);
        var page = await client.GetPageAsync($"/profile/orders/{number}");
        Assert.NotNull(page.One("form[action*='handler=Reorder']"));

        using var response = await client.PostFormAsync($"/profile/orders/{number}?handler=Reorder");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Equal("/cart", response.Headers.Location?.OriginalString);

        var quantity = await app.WithDbAsync(db => db.CartItems.Where(i => i.Cart.UserId == customer.Id && i.ProductId == productId).SumAsync(i => i.Quantity));
        Assert.Equal(2, quantity);

        // سفارش دیگران ← 404 (کاربر دیگر).
        var stranger = await app.CreateCustomerAsync();
        using var other = app.CreateClient();
        await other.LoginAsync(stranger.Phone, stranger.Password);
        using var foreign = await other.PostFormAsync($"/profile/orders/{number}?handler=Reorder");
        Assert.Equal(HttpStatusCode.NotFound, foreign.StatusCode);

        // بدون توکن ضدجعل ← رد.
        using var csrf = await client.PostRawFormAsync($"/profile/orders/{number}?handler=Reorder");
        Assert.Equal(HttpStatusCode.BadRequest, csrf.StatusCode);
    }

    // ── جست‌وجو با عکس ──────────────────────────────────────────

    [SqlFact]
    public async Task ImageSearch_rejects_invalid_files_and_requests()
    {
        using var client = app.CreateClient();
        var token = await client.AntiforgeryTokenAsync();

        using (var text = await PostImageAsync(client, Encoding.UTF8.GetBytes("<?php echo 'x'; ?>"), "shell.png", "image/png", token))
        {
            Assert.Equal(HttpStatusCode.BadRequest, text.StatusCode);
            Assert.Contains("معتبر", (await JsonAsync(text)).GetProperty("error").GetString());
        }
        using (var noToken = await PostImageAsync(client, Png(SKColors.Red), "a.png", "image/png", null))
            Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        using (var noFile = await PostJsonAsync(client, "/api/search/image", new { image = "x" }, token))
            Assert.True(noFile.StatusCode is HttpStatusCode.BadRequest or HttpStatusCode.RequestEntityTooLarge, noFile.StatusCode.ToString());
    }

    [SqlFact]
    public async Task ImageSearch_finds_product_with_same_picture()
    {
        var (productId, _) = await app.CreateProductAsync();
        var bytes = Png(new SKColor(30, 140, 200));
        var fileName = "r24-imgsig-" + Guid.NewGuid().ToString("N")[..10] + ".png";
        // همان پوشه‌ای که UploadStore برای عکس کالاها استفاده می‌کند (wwwroot/uploads/products).
        var webRoot = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.IWebHostEnvironment>().WebRootPath;
        var folder = Path.Combine(webRoot, "uploads", UploadArea.Products.Name);
        Directory.CreateDirectory(folder);
        var path = Path.Combine(folder, fileName);
        await File.WriteAllBytesAsync(path, bytes);
        await using (var check = app.Scope())
            Assert.Equal(path, check.ServiceProvider.GetRequiredService<IUploadStore>().GetPath(UploadArea.Products, fileName));
        try
        {
            await app.WithDbAsync(async db =>
            {
                db.ProductImages.Add(new ProductImage { ProductId = productId, ImageName = fileName, IsPrimary = true, DisplayOrder = 0, AltText = "عکس آزمون" });
                await db.SaveChangesAsync();
            });

            // ساخت امضاها (کار پس‌زمینه) تا امضای این کالا ساخته شود.
            var built = false;
            for (var round = 0; round < 60 && !built; round++)
            {
                await using var scope = app.Scope();
                var service = scope.ServiceProvider.GetRequiredService<ImageSearchService>();
                try
                {
                    await service.BuildSignaturesAsync(CancellationToken.None);
                }
                catch (DbUpdateException)
                {
                    // کار پس‌زمینهٔ خود برنامه هم‌زمان همان ردیف را ساخته؛ دوباره نگاه می‌کنیم.
                }
                built = await app.WithDbAsync(db => db.ProductImageSignatures.AnyAsync(s => s.ProductId == productId && s.ImageName == fileName && s.Hash != 0));
            }
            Assert.True(built);

            using var client = app.CreateClient();
            var token = await client.AntiforgeryTokenAsync();
            using var response = await PostImageAsync(client, bytes, "photo.png", "image/png", token);
            Assert.Equal(HttpStatusCode.OK, response.StatusCode);
            var ids = (await JsonAsync(response)).GetProperty("products").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();
            Assert.Contains(productId, ids);

            // عکس کاملاً متفاوت (رنگ و جهت دیگر) این کالا را برنمی‌گرداند.
            using var different = await PostImageAsync(client, Png(new SKColor(230, 200, 20), leftToRight: false), "other.png", "image/png", token);
            var otherIds = (await JsonAsync(different)).GetProperty("products").EnumerateArray().Select(p => p.GetProperty("id").GetGuid()).ToList();
            Assert.DoesNotContain(productId, otherIds);
        }
        finally
        {
            File.Delete(path);
            await app.WithDbAsync(async db =>
            {
                await db.ProductImageSignatures.IgnoreQueryFilters().Where(s => s.ProductId == productId).ExecuteDeleteAsync();
                await db.ProductImages.IgnoreQueryFilters().Where(i => i.ProductId == productId).ExecuteDeleteAsync();
            });
        }
    }

    // ── جست‌وجو ────────────────────────────────────────────────

    [SqlFact]
    public async Task Search_defaults_to_relevance_and_records_clicks()
    {
        var (productId, _) = await app.CreateProductAsync();
        using var client = app.CreateClient();

        var page = await client.GetPageAsync("/search?q=" + Uri.EscapeDataString("کالای آزمون"));
        var relevance = page.One("select[name='sort'] option[value='Relevance']");
        Assert.NotNull(relevance);
        Assert.True(relevance!.HasAttribute("selected"));
        Assert.Equal("کالای آزمون", page.One("[data-search-q]")?.GetAttribute("data-search-q"));
        Assert.NotEmpty(page.All("[data-search-q] [data-product-id]"));

        // بدون عبارت، گزینهٔ «مرتبط‌ترین» نیست.
        var browse = await client.GetPageAsync("/search");
        Assert.Null(browse.One("select[name='sort'] option[value='Relevance']"));

        // مرتب‌سازی دستی کاربر حفظ می‌شود.
        var cheap = await client.GetPageAsync("/search?q=" + Uri.EscapeDataString("کالای آزمون") + "&sort=" + nameof(Fratak_Shop.Core.DTOs.Products.Query.ProductSortBy.Relevance));
        Assert.NotNull(cheap.One("select[name='sort'] option[value='Relevance'][selected]"));

        // ثبت کلیک: بدون توکن ← 400؛ با توکن ← 204.
        using (var noToken = await PostJsonAsync(client, "/api/search/click", new { q = "کالای آزمون", productId }, null))
            Assert.Equal(HttpStatusCode.BadRequest, noToken.StatusCode);
        var token = await client.AntiforgeryTokenAsync();
        using (var ok = await PostJsonAsync(client, "/api/search/click", new { q = "کالای آزمون", productId }, token))
            Assert.Equal(HttpStatusCode.NoContent, ok.StatusCode);
        using (var bad = await PostJsonAsync(client, "/api/search/click", new { q = "", productId }, token))
            Assert.Equal(HttpStatusCode.BadRequest, bad.StatusCode);

        var key = Fratak_Shop.Core.Services.Searches.SearchLearning.Key("کالای آزمون")!;
        Assert.True(await app.WithDbAsync(db => db.SearchClickStats.AnyAsync(s => s.NormalizedQuery == key && s.ProductId == productId)));
    }

    // ── نشانه‌های رابط کاربری ───────────────────────────────────

    [SqlFact]
    public async Task Storefront_renders_commerce_hooks_and_script()
    {
        await app.CreateProductAsync();
        using var client = app.CreateClient();
        var home = await client.GetPageAsync("/");
        Assert.NotNull(home.One("script[src*='/assets/js/commerce.js']"));
        Assert.NotNull(home.One("link[href*='/assets/css/commerce.css']"));
        Assert.NotEmpty(home.All("[data-voice-search]"));
        Assert.NotEmpty(home.All("[data-image-search]"));

        var search = await client.GetPageAsync("/search");
        Assert.NotEmpty(search.All("[data-quick-view]"));
        Assert.NotEmpty(search.All("[data-compare-add]"));
        // دکمه داخل پیوند نیست (HTML معتبر).
        Assert.Empty(search.All("a [data-quick-view], a [data-compare-add]"));

        using var css = await client.GetAsync("/assets/css/commerce.css");
        Assert.Equal(HttpStatusCode.OK, css.StatusCode);
        using var js = await client.GetAsync("/assets/js/commerce.js");
        Assert.Equal(HttpStatusCode.OK, js.StatusCode);
    }

    [SqlFact]
    public async Task SocialProof_endpoint_returns_null_for_new_product()
    {
        var (productId, _) = await app.CreateProductAsync();
        using var client = app.CreateClient();
        using var response = await client.GetAsync($"/api/products/{productId}/proof");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal(JsonValueKind.Null, (await JsonAsync(response)).GetProperty("text").ValueKind);
    }
}
