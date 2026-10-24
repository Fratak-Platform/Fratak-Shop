using Fratak_Shop.Core.DTOs.Faqs.Query;
using Fratak_Shop.Core.Services.Faqs;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Web.Infrastructure;
using Fratak_Shop.Web.Services;

namespace Fratak_Shop.Web.Pages;

/// <summary>FAQ grouped by the categories managed in the database (FAQPage rich result).</summary>
public class FaqModel(IFaqItemService faqService, IPageMetaService pageMeta) : ShopPageModel
{
    public List<FaqGroupDto> Groups { get; private set; } = [];

    public int TotalCount => Groups.Sum(group => group.Items.Count);

    public async Task OnGetAsync()
    {
        Groups = (await faqService.GetFaqGroupsAsync(HttpContext.RequestAborted))
            .Where(group => group.Items.Count > 0)
            .ToList();

        await pageMeta.ApplyAsync(ViewData, SitePage.Faq, "سوالات متداول",
            "پاسخ پرسش های رایج درباره ثبت سفارش، ارسال، پرداخت، کیف پول و مرجوعی کالا.");
        pageMeta.AddBreadcrumbs(ViewData, ("سوالات متداول", null));

        if (TotalCount > 0 && ViewData[PageMetaKeys.JsonLd] is null)
        {
            pageMeta.AddJsonLd(ViewData, new Dictionary<string, object>
            {
                ["@context"] = "https://schema.org",
                ["@type"] = "FAQPage",
                ["mainEntity"] = Groups.SelectMany(group => group.Items).Select(item => new Dictionary<string, object>
                {
                    ["@type"] = "Question",
                    ["name"] = item.Question,
                    ["acceptedAnswer"] = new Dictionary<string, object>
                    {
                        ["@type"] = "Answer",
                        ["text"] = item.Answer.ToPlainText(),
                    },
                }).ToList(),
            });
        }
    }
}
