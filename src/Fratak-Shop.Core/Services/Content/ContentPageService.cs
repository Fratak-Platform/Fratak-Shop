using Fratak_Shop.Core.DTOs.Content.Command;
using Fratak_Shop.Core.DTOs.Content.Query;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Content;
using Fratak_Shop.Data.Entities.Pages;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Content;

public class ContentPageService(FratakShopDbContext context) : IContentPageService
{
    public async Task<List<ContentPageDto>> GetAllAsync()
    {
        var pages = await context.ContentPages
            .OrderBy(page => page.DisplayOrder)
            .ToListAsync();

        return pages.Select(Map).ToList();
    }

    public async Task<ContentPageDto?> GetAsync(SitePage page)
    {
        var contentPage = await context.ContentPages.FirstOrDefaultAsync(item => item.Page == page);
        return contentPage is null ? null : Map(contentPage);
    }

    public async Task<ContentPageDto?> GetByIdAsync(Guid id)
    {
        var contentPage = await context.ContentPages.FirstOrDefaultAsync(item => item.Id == id);
        return contentPage is null ? null : Map(contentPage);
    }

    public async Task<OperationResult> SaveAsync(SaveContentPageCommand command)
    {
        if (command.Page == SitePage.None)
            return OperationResult.Error("صفحه مورد نظر را انتخاب کنید.");

        if (string.IsNullOrWhiteSpace(command.Title))
            return OperationResult.Error("عنوان صفحه را وارد کنید.");

        // The body is authored in a rich editor, so it is sanitized before storage
        // to keep stored HTML safe for visitors.
        var content = string.IsNullOrWhiteSpace(command.Content) ? null : command.Content.SanitizeText();

        if (command.Id.HasValue)
        {
            var contentPage = await context.ContentPages.AsTracking()
                .FirstOrDefaultAsync(item => item.Id == command.Id.Value);

            if (contentPage is null)
                return OperationResult.NotFound("صفحه مورد نظر یافت نشد.");

            var duplicate = await context.ContentPages
                .AnyAsync(item => item.Id != command.Id.Value && item.Page == command.Page);

            if (duplicate)
                return OperationResult.Error("برای این صفحه قبلاً محتوایی ثبت شده است.");

            contentPage.Page = command.Page;
            contentPage.Title = command.Title.Trim();
            contentPage.Summary = command.Summary;
            contentPage.Content = content;
            contentPage.IsPublished = command.IsPublished;
            contentPage.DisplayOrder = command.DisplayOrder;

            await context.SaveChangesAsync();
            return OperationResult.Success("محتوای صفحه ذخیره شد.");
        }

        var exists = await context.ContentPages.AnyAsync(item => item.Page == command.Page);
        if (exists)
            return OperationResult.Error("برای این صفحه قبلاً محتوایی ثبت شده است.");

        context.ContentPages.Add(new ContentPage
        {
            Page = command.Page,
            Title = command.Title.Trim(),
            Summary = command.Summary,
            Content = content,
            IsPublished = command.IsPublished,
            DisplayOrder = command.DisplayOrder,
        });

        await context.SaveChangesAsync();
        return OperationResult.Success("صفحه جدید ایجاد شد.");
    }

    public async Task<OperationResult> TogglePublishedAsync(Guid id)
    {
        var contentPage = await context.ContentPages.AsTracking().FirstOrDefaultAsync(item => item.Id == id);
        if (contentPage is null)
            return OperationResult.NotFound("صفحه مورد نظر یافت نشد.");

        contentPage.IsPublished = !contentPage.IsPublished;
        await context.SaveChangesAsync();

        return OperationResult.Success(contentPage.IsPublished ? "صفحه منتشر شد." : "صفحه از دسترس خارج شد.");
    }

    public async Task EnsureDefaultsAsync(CancellationToken cancellationToken = default)
    {
        var existing = await context.ContentPages
            .Select(page => page.Page)
            .ToListAsync(cancellationToken);

        var defaults = new List<ContentPage>
        {
            new()
            {
                Page = SitePage.AboutUs,
                Title = "درباره ما",
                Summary = "با فروشگاه فراتک و تیمی که پشت آن است آشنا شوید.",
                DisplayOrder = 1,
                Content = "<p>فروشگاه فراتک با هدف ارائه کالای اصل، قیمت منصفانه و پشتیبانی واقعی راه‌اندازی شده است. " +
                          "ما روی سه اصل ساده تمرکز کرده‌ایم: انتخاب دقیق محصولات، شفافیت قیمت و همراهی پس از فروش.</p>" +
                          "<h2>چرا فراتک؟</h2><ul><li>تضمین اصالت کالا و گارانتی معتبر</li>" +
                          "<li>ارسال سریع به سراسر کشور</li><li>پشتیبانی پاسخگو در ساعات کاری</li></ul>",
            },
            new()
            {
                Page = SitePage.ContactUs,
                Title = "تماس با ما",
                Summary = "راه‌های ارتباطی با فروشگاه؛ تلفن، ایمیل و فرم پیام.",
                DisplayOrder = 2,
                Content = "<p>کارشناسان ما در ساعات کاری آماده پاسخ‌گویی به سوالات شما هستند. " +
                          "همچنین می‌توانید از فرم پیام همین صفحه استفاده کنید تا در سریع‌ترین زمان پاسخ بگیرید.</p>",
            },
            new()
            {
                Page = SitePage.Faq,
                Title = "پرسش‌های متداول",
                Summary = "پاسخ پرسش‌های رایج درباره خرید، ارسال و گارانتی.",
                DisplayOrder = 3,
                Content = "<p>پیش از تماس با پشتیبانی، نگاهی به پرسش‌های متداول بیندازید؛ احتمالاً پاسخ سوال شما همین‌جاست.</p>",
            },
            new()
            {
                Page = SitePage.Warranty,
                Title = "گارانتی و خدمات پس از فروش",
                Summary = "شرایط گارانتی کالاها و روند خدمات پس از فروش.",
                DisplayOrder = 4,
                Content = "<p>همه کالاهای فروشگاه دارای گارانتی معتبر هستند. در صورت بروز مشکل، " +
                          "کافی است از بخش سفارش‌های من درخواست خدمات ثبت کنید تا کارشناسان ما پیگیری کنند.</p>",
            },
            new()
            {
                Page = SitePage.Terms,
                Title = "قوانین و مقررات",
                Summary = "شرایط خرید، ارسال و بازگشت کالا در فروشگاه فراتک.",
                DisplayOrder = 5,
                Content = "<p>ثبت سفارش در فروشگاه به معنی پذیرش قوانین زیر است:</p>" +
                          "<ul><li>قیمت‌ها به تومان و شامل مالیات است.</li><li>مهلت مرجوعی کالا ۷ روز پس از تحویل است.</li>" +
                          "<li>در پرداخت کارت به کارت، سفارش پس از تأیید فیش پردازش می‌شود.</li></ul>",
            },
            new()
            {
                Page = SitePage.Privacy,
                Title = "حریم خصوصی",
                Summary = "نحوه نگهداری و استفاده از اطلاعات کاربران.",
                DisplayOrder = 6,
                Content = "<p>اطلاعات شما فقط برای پردازش سفارش و بهبود تجربه خرید استفاده می‌شود و " +
                          "در اختیار هیچ شخص ثالثی قرار نمی‌گیرد.</p>",
            },
        };

        var missing = defaults.Where(item => !existing.Contains(item.Page)).ToList();
        if (missing.Count == 0)
            return;

        context.ContentPages.AddRange(missing);
        await context.SaveChangesAsync(cancellationToken);
    }

    private static ContentPageDto Map(ContentPage page) => new()
    {
        Id = page.Id,
        CreatedAt = page.CreatedAt,
        UpdatedAt = page.UpdatedAt,
        Page = page.Page,
        Title = page.Title,
        Summary = page.Summary,
        Content = page.Content,
        IsPublished = page.IsPublished,
        DisplayOrder = page.DisplayOrder,
    };
}
