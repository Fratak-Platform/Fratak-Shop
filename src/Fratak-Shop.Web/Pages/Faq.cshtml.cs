using Fratak_Shop.Core.DTOs.Faqs.Query;
using Fratak_Shop.Core.Services.Faqs;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;

namespace Fratak_Shop.Web.Pages;

public class FaqModel(IFaqItemService faqService, IPageMetaService pageMeta) : ShopPageModel
{
    public List<FaqItemDto> RightColumn { get; private set; } = [];
    public List<FaqItemDto> LeftColumn { get; private set; } = [];

    public async Task OnGetAsync()
    {
        var items = await faqService.GetActiveFaqItemsAsync();

        // Two balanced columns, keeping the display order reading right-to-left.
        RightColumn = items.Where((_, index) => index % 2 == 0).ToList();
        LeftColumn = items.Where((_, index) => index % 2 == 1).ToList();

        await pageMeta.ApplyAsync(ViewData, SitePage.Faq, "سوالات متداول", "پاسخ پرسش های رایج درباره ثبت سفارش، ارسال، پرداخت و مرجوعی کالا.");

        if (items.Count > 0)
        {
            var schema = new Dictionary<string, object>
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "FAQPage",
                ["mainEntity"] = items.Select(i => new Dictionary<string, object>
                {
                    ["@type"] = "Question",
                    ["name"] = i.Question,
                    ["acceptedAnswer"] = new Dictionary<string, object> { ["@type"] = "Answer", ["text"] = i.Answer },
                }).ToList(),
            };

            ViewData[PageMetaKeys.JsonLd] ??= System.Text.Json.JsonSerializer.Serialize(schema,
                new System.Text.Json.JsonSerializerOptions { Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping });
        }
    }
}
