using System.Text.Json;
using System.Text.Encodings.Web;
using Fratak_Shop.Core.DTOs.Seo.Command;
using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.SeoEngine;
using Fratak_Shop.Core.Services.Settings;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Blogs;
using Fratak_Shop.Data.Entities.Categories;
using Fratak_Shop.Data.Entities.Comments;
using Fratak_Shop.Data.Entities.Content;
using Fratak_Shop.Data.Entities.Pages;
using Fratak_Shop.Data.Entities.Products;
using Fratak_Shop.Data.Entities.Seo;
using Fratak_Shop.Data.Utilities.SeoManagement;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Seo;

public class SeoAuditService(
    FratakShopDbContext context,
    ISiteSettingService settingService,
    ISeoStatisticsService statisticsService) : ISeoAuditService
{
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNameCaseInsensitive = true,
    };

    // ==================================================================== generate

    public async Task<OperationResult<SeoAuditDto>> GenerateAsync(SeoEntityType entityType, Guid entityId, Guid? userId,
        CancellationToken cancellationToken = default)
    {
        if (entityType == SeoEntityType.None || entityId == Guid.Empty)
            return OperationResult<SeoAuditDto>.Error("محتوای انتخاب‌شده معتبر نیست.");

        var input = await BuildInputAsync(entityType, entityId, cancellationToken);
        if (input is null)
            return OperationResult<SeoAuditDto>.NotFound("محتوای مورد نظر برای سئو یافت نشد.");

        var audit = SmartSeoEngine.Analyze(input);

        // Previous pending proposals of the same content become stale, so the review
        // screen never shows two competing versions of the same page.
        var previous = await context.SeoAuditRecords
            .Where(record => record.EntityType == entityType &&
                             record.EntityId == entityId &&
                             record.Status == SeoAuditStatus.PendingReview)
            .ToListAsync(cancellationToken);

        foreach (var item in previous)
            item.Status = SeoAuditStatus.Stale;

        var record = new SeoAuditRecord
        {
            EntityType = entityType,
            EntityId = entityId,
            Score = audit.Score,
            PayloadJson = JsonSerializer.Serialize(audit, JsonOptions),
            Summary = audit.Summary,
            Provider = "FratakSeoEngine",
            Status = SeoAuditStatus.PendingReview,
            CreatedByUserId = userId,
        };

        context.SeoAuditRecords.Add(record);
        await context.SaveChangesAsync(cancellationToken);

        return OperationResult<SeoAuditDto>.Success(
            await MapAsync(record, cancellationToken),
            "پیشنهاد سئوی هوشمند آماده بررسی است؛ پس از تأیید ذخیره می‌شود.");
    }

    public async Task<OperationResult<int>> GenerateForMissingAsync(SeoEntityType entityType, int take, Guid? userId,
        CancellationToken cancellationToken = default)
    {
        take = take is < 1 or > 50 ? 10 : take;
        var counter = 0;

        switch (entityType)
        {
            case SeoEntityType.Product:
            {
                var ids = await context.Products
                    .Where(product => product.Seo == null || product.Seo.Title == null || product.Seo.SeoScore == null)
                    .OrderByDescending(product => product.CreatedAt)
                    .Take(take)
                    .Select(product => product.Id)
                    .ToListAsync(cancellationToken);

                foreach (var id in ids)
                {
                    var result = await GenerateAsync(entityType, id, userId, cancellationToken);
                    if (result.Status == EnOperationResultStatus.Success)
                        counter++;
                }

                break;
            }
            case SeoEntityType.BlogPost:
            {
                var ids = await context.BlogPosts
                    .Where(post => post.Seo == null || post.Seo.Title == null || post.Seo.SeoScore == null)
                    .OrderByDescending(post => post.CreatedAt)
                    .Take(take)
                    .Select(post => post.Id)
                    .ToListAsync(cancellationToken);

                foreach (var id in ids)
                {
                    var result = await GenerateAsync(entityType, id, userId, cancellationToken);
                    if (result.Status == EnOperationResultStatus.Success)
                        counter++;
                }

                break;
            }
            case SeoEntityType.Category:
            {
                var ids = await context.Categories
                    .Where(category => category.Seo == null || category.Seo.Title == null || category.Seo.SeoScore == null)
                    .OrderBy(category => category.DisplayOrder)
                    .Take(take)
                    .Select(category => category.Id)
                    .ToListAsync(cancellationToken);

                foreach (var id in ids)
                {
                    var result = await GenerateAsync(entityType, id, userId, cancellationToken);
                    if (result.Status == EnOperationResultStatus.Success)
                        counter++;
                }

                break;
            }
            default:
                return OperationResult<int>.Error("برای این نوع محتوا اجرای دسته‌جمعی پشتیبانی نمی‌شود.");
        }

        return counter == 0
            ? OperationResult<int>.Success(0, "همه محتواهای این بخش قبلاً سئو شده‌اند.")
            : OperationResult<int>.Success(counter, $"برای {PriceHelper.ToPersianDigits(counter.ToString())} محتوا پیشنهاد سئو ساخته شد.");
    }

    public async Task<OperationResult<ContentEnhancementDto>> EnhanceAsync(SeoEntityType entityType, Guid entityId,
        CancellationToken cancellationToken = default)
    {
        var input = await BuildInputAsync(entityType, entityId, cancellationToken);
        if (input is null)
            return OperationResult<ContentEnhancementDto>.NotFound("محتوای مورد نظر یافت نشد.");

        var audit = SmartSeoEngine.Analyze(input);
        var keyword = string.IsNullOrWhiteSpace(input.FocusKeyword) ? audit.Proposal.FocusKeyword ?? input.Title : input.FocusKeyword!;

        var dto = new ContentEnhancementDto
        {
            EntityType = entityType,
            EntityId = entityId,
            EntityTitle = input.Title,
            FocusKeyword = keyword,
            OriginalShortDescription = input.ShortDescription,
            OriginalContent = input.Content,
            FaqSuggestions = audit.Proposal.FaqSuggestions,
            InternalLinks = audit.Proposal.InternalLinks,
            EnhancedShortDescription = ContentEnhancer.EnhanceShortDescription(input, keyword),
            EnhancedContent = entityType switch
            {
                SeoEntityType.Product => ContentEnhancer.EnhanceProductDescription(input, keyword, audit.Proposal.FaqSuggestions),
                SeoEntityType.BlogPost => ContentEnhancer.EnhanceBlogContent(input, keyword),
                _ => ContentEnhancer.RestructureExistingContent(input.Content, keyword),
            },
            Summary = "متن پیشنهادی توسط موتور هوشمند تولید شده است؛ پس از بازبینی و ویرایش، ذخیره را تأیید کنید.",
        };

        return OperationResult<ContentEnhancementDto>.Success(dto, "پیش‌نویس متن آماده بازبینی است.");
    }

    // ======================================================================= apply

    public async Task<OperationResult> ApplyAsync(ApplySeoProposalCommand command, CancellationToken cancellationToken = default)
    {
        var record = await context.SeoAuditRecords.AsTracking()
            .FirstOrDefaultAsync(item => item.Id == command.RecordId, cancellationToken);

        if (record is null)
            return OperationResult.NotFound("پیشنهاد سئوی مورد نظر یافت نشد.");

        if (record.Status == SeoAuditStatus.Applied)
            return OperationResult.Error("این پیشنهاد قبلاً اعمال شده است.");

        var proposal = command.Proposal;
        var now = DateTimeOffset.UtcNow;

        switch (record.EntityType)
        {
            case SeoEntityType.Product:
            {
                var product = await context.Products.AsTracking()
                    .Include(item => item.Images)
                    .FirstOrDefaultAsync(item => item.Id == record.EntityId, cancellationToken);

                if (product is null)
                    return OperationResult.NotFound("محصول مورد نظر یافت نشد.");

                product.Seo = BuildSeo(product.Seo, proposal) ?? product.Seo;
                product.Seo!.SeoScore = record.Score;
                product.Seo.LastAuditedAt = now;
                product.Seo.IsReviewed = command.MarkAsReviewed;
                product.Seo.ReviewedAt = command.MarkAsReviewed ? now : product.Seo.ReviewedAt;
                product.Seo.ReviewedByUserId = command.MarkAsReviewed ? command.UserId : product.Seo.ReviewedByUserId;

                if (command.ApplySlug && !string.IsNullOrWhiteSpace(proposal.Slug))
                    product.Slug = await MakeSlugUniqueAsync(proposal.Slug!, product.Id, cancellationToken);

                if (command.ApplyImageAlts && !string.IsNullOrWhiteSpace(proposal.ImageAlt))
                {
                    foreach (var image in product.Images)
                    {
                        if (string.IsNullOrWhiteSpace(image.AltText))
                            image.AltText = proposal.ImageAlt;
                    }
                }

                if (command.ApplyContent)
                {
                    if (!string.IsNullOrWhiteSpace(proposal.EnhancedShortDescription))
                        product.ShortDescription = proposal.EnhancedShortDescription;
                    if (!string.IsNullOrWhiteSpace(proposal.EnhancedContent))
                        product.Description = proposal.EnhancedContent;
                }

                break;
            }
            case SeoEntityType.Category:
            {
                var category = await context.Categories.AsTracking()
                    .FirstOrDefaultAsync(item => item.Id == record.EntityId, cancellationToken);

                if (category is null)
                    return OperationResult.NotFound("دسته‌بندی مورد نظر یافت نشد.");

                category.Seo = BuildSeo(category.Seo, proposal) ?? category.Seo;
                category.Seo!.SeoScore = record.Score;
                category.Seo.LastAuditedAt = now;
                category.Seo.IsReviewed = command.MarkAsReviewed;
                category.Seo.ReviewedAt = command.MarkAsReviewed ? now : category.Seo.ReviewedAt;
                category.Seo.ReviewedByUserId = command.MarkAsReviewed ? command.UserId : category.Seo.ReviewedByUserId;

                if (command.ApplySlug && !string.IsNullOrWhiteSpace(proposal.Slug))
                    category.Slug = await MakeSlugUniqueAsync(proposal.Slug!, category.Id, cancellationToken);

                if (command.ApplyContent && !string.IsNullOrWhiteSpace(proposal.EnhancedShortDescription))
                    category.Description = proposal.EnhancedShortDescription;

                break;
            }
            case SeoEntityType.BlogPost:
            {
                var post = await context.BlogPosts.AsTracking()
                    .FirstOrDefaultAsync(item => item.Id == record.EntityId, cancellationToken);

                if (post is null)
                    return OperationResult.NotFound("مقاله مورد نظر یافت نشد.");

                post.Seo = BuildSeo(post.Seo, proposal) ?? post.Seo;
                post.Seo!.SeoScore = record.Score;
                post.Seo.LastAuditedAt = now;
                post.Seo.IsReviewed = command.MarkAsReviewed;
                post.Seo.ReviewedAt = command.MarkAsReviewed ? now : post.Seo.ReviewedAt;
                post.Seo.ReviewedByUserId = command.MarkAsReviewed ? command.UserId : post.Seo.ReviewedByUserId;

                if (command.ApplySlug && !string.IsNullOrWhiteSpace(proposal.Slug))
                    post.Slug = await MakeSlugUniqueAsync(proposal.Slug!, post.Id, cancellationToken);

                if (command.ApplyContent)
                {
                    if (!string.IsNullOrWhiteSpace(proposal.EnhancedShortDescription))
                        post.Excerpt = proposal.EnhancedShortDescription;
                    if (!string.IsNullOrWhiteSpace(proposal.EnhancedContent))
                        post.Content = proposal.EnhancedContent;
                    if (!string.IsNullOrWhiteSpace(proposal.ImageAlt))
                        post.ImageAlt = proposal.ImageAlt;
                }

                break;
            }
            case SeoEntityType.BlogCategory:
            {
                var blogCategory = await context.BlogCategories.AsTracking()
                    .FirstOrDefaultAsync(item => item.Id == record.EntityId, cancellationToken);

                if (blogCategory is null)
                    return OperationResult.NotFound("دسته‌بندی وبلاگ مورد نظر یافت نشد.");

                blogCategory.Seo = BuildSeo(blogCategory.Seo, proposal) ?? blogCategory.Seo;
                blogCategory.Seo!.SeoScore = record.Score;
                blogCategory.Seo.LastAuditedAt = now;
                blogCategory.Seo.IsReviewed = command.MarkAsReviewed;
                blogCategory.Seo.ReviewedAt = command.MarkAsReviewed ? now : blogCategory.Seo.ReviewedAt;
                blogCategory.Seo.ReviewedByUserId = command.MarkAsReviewed ? command.UserId : blogCategory.Seo.ReviewedByUserId;

                if (command.ApplySlug && !string.IsNullOrWhiteSpace(proposal.Slug))
                    blogCategory.Slug = await MakeSlugUniqueAsync(proposal.Slug!, blogCategory.Id, cancellationToken);

                if (command.ApplyContent && !string.IsNullOrWhiteSpace(proposal.EnhancedShortDescription))
                    blogCategory.Description = proposal.EnhancedShortDescription;

                break;
            }
            case SeoEntityType.Page:
            {
                var contentPage = await context.ContentPages.AsTracking()
                    .FirstOrDefaultAsync(item => item.Id == record.EntityId, cancellationToken);

                if (contentPage is null)
                    return OperationResult.NotFound("صفحه مورد نظر یافت نشد.");

                var pageSeo = await context.PageSeos.AsTracking()
                    .FirstOrDefaultAsync(item => item.Page == contentPage.Page, cancellationToken);

                if (pageSeo is null)
                {
                    pageSeo = new PageSeo { Page = contentPage.Page };
                    context.PageSeos.Add(pageSeo);
                }

                pageSeo.Seo = BuildSeo(pageSeo.Seo, proposal) ?? new SeoMetaData();
                pageSeo.Seo.SeoScore = record.Score;
                pageSeo.Seo.LastAuditedAt = now;
                pageSeo.Seo.IsReviewed = command.MarkAsReviewed;
                pageSeo.Seo.ReviewedAt = command.MarkAsReviewed ? now : pageSeo.Seo.ReviewedAt;
                pageSeo.Seo.ReviewedByUserId = command.MarkAsReviewed ? command.UserId : pageSeo.Seo.ReviewedByUserId;

                if (command.ApplyContent)
                {
                    if (!string.IsNullOrWhiteSpace(proposal.EnhancedShortDescription))
                        contentPage.Summary = proposal.EnhancedShortDescription;
                    if (!string.IsNullOrWhiteSpace(proposal.EnhancedContent))
                        contentPage.Content = proposal.EnhancedContent;
                }

                break;
            }
            default:
                return OperationResult.Error("نوع محتوای انتخاب‌شده پشتیبانی نمی‌شود.");
        }

        record.Status = SeoAuditStatus.Applied;
        record.AppliedAt = now;
        record.AppliedByUserId = command.UserId;
        record.Score = record.Score == 0 ? 100 : record.Score;
        record.PayloadJson = JsonSerializer.Serialize(
            new SeoAuditResult { Score = record.Score, Proposal = proposal, Summary = record.Summary ?? string.Empty },
            JsonOptions);

        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success("سئوی تأییدشده روی محتوا ذخیره شد.");
    }

    public async Task<OperationResult> ApplyContentAsync(ApplyContentEnhancementCommand command,
        CancellationToken cancellationToken = default)
    {
        switch (command.EntityType)
        {
            case SeoEntityType.Product:
            {
                var product = await context.Products.AsTracking()
                    .FirstOrDefaultAsync(item => item.Id == command.EntityId, cancellationToken);

                if (product is null)
                    return OperationResult.NotFound("محصول مورد نظر یافت نشد.");

                if (!string.IsNullOrWhiteSpace(command.ShortDescription))
                    product.ShortDescription = command.ShortDescription;
                if (!string.IsNullOrWhiteSpace(command.Content))
                    product.Description = command.Content;

                break;
            }
            case SeoEntityType.BlogPost:
            {
                var post = await context.BlogPosts.AsTracking()
                    .FirstOrDefaultAsync(item => item.Id == command.EntityId, cancellationToken);

                if (post is null)
                    return OperationResult.NotFound("مقاله مورد نظر یافت نشد.");

                if (!string.IsNullOrWhiteSpace(command.ShortDescription))
                    post.Excerpt = command.ShortDescription;
                if (!string.IsNullOrWhiteSpace(command.Content))
                    post.Content = command.Content;

                break;
            }
            case SeoEntityType.Page:
            {
                var contentPage = await context.ContentPages.AsTracking()
                    .FirstOrDefaultAsync(item => item.Id == command.EntityId, cancellationToken);

                if (contentPage is null)
                    return OperationResult.NotFound("صفحه مورد نظر یافت نشد.");

                if (!string.IsNullOrWhiteSpace(command.ShortDescription))
                    contentPage.Summary = command.ShortDescription;
                if (!string.IsNullOrWhiteSpace(command.Content))
                    contentPage.Content = command.Content;

                break;
            }
            case SeoEntityType.Category:
            {
                var category = await context.Categories.AsTracking()
                    .FirstOrDefaultAsync(item => item.Id == command.EntityId, cancellationToken);

                if (category is null)
                    return OperationResult.NotFound("دسته‌بندی مورد نظر یافت نشد.");

                if (!string.IsNullOrWhiteSpace(command.ShortDescription))
                    category.Description = command.ShortDescription;

                break;
            }
            default:
                return OperationResult.Error("نوع محتوای انتخاب‌شده پشتیبانی نمی‌شود.");
        }

        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success("متن تأییدشده ذخیره شد.");
    }

    public async Task<OperationResult> RejectAsync(RejectSeoProposalCommand command, CancellationToken cancellationToken = default)
    {
        var record = await context.SeoAuditRecords.AsTracking()
            .FirstOrDefaultAsync(item => item.Id == command.RecordId, cancellationToken);

        if (record is null)
            return OperationResult.NotFound("پیشنهاد سئوی مورد نظر یافت نشد.");

        record.Status = SeoAuditStatus.Rejected;
        if (!string.IsNullOrWhiteSpace(command.Reason))
            record.Summary = $"{record.Summary} | دلیل رد: {command.Reason}";

        await context.SaveChangesAsync(cancellationToken);
        return OperationResult.Success("پیشنهاد سئو رد شد.");
    }

    // ===================================================================== queries

    public async Task<SeoAuditDto?> GetAsync(Guid id)
    {
        var record = await context.SeoAuditRecords.FirstOrDefaultAsync(item => item.Id == id);
        return record is null ? null : await MapAsync(record, CancellationToken.None);
    }

    public async Task<SeoAuditFilterResult> GetFilterAsync(SeoAuditFilterParams filterParams)
    {
        var query = context.SeoAuditRecords.AsQueryable();

        if (filterParams.EntityType.HasValue)
            query = query.Where(record => record.EntityType == filterParams.EntityType.Value);

        if (filterParams.Status.HasValue)
            query = query.Where(record => record.Status == filterParams.Status.Value);

        if (filterParams.MaxScore.HasValue)
            query = query.Where(record => record.Score <= filterParams.MaxScore.Value);

        if (!string.IsNullOrWhiteSpace(filterParams.Search))
        {
            var term = filterParams.Search.Trim();
            query = query.Where(record => record.Summary != null && record.Summary.Contains(term));
        }

        var total = await query.CountAsync();
        var records = await query
            .OrderByDescending(record => record.CreatedAt)
            .Skip((filterParams.PageId - 1) * filterParams.Take)
            .Take(filterParams.Take)
            .ToListAsync();

        var result = new SeoAuditFilterResult
        {
            Data = new List<SeoAuditDto>(),
            FilterParams = filterParams,
        };

        foreach (var record in records)
            result.Data.Add(await MapAsync(record, CancellationToken.None));

        result.GeneratePaging(total, filterParams.Take, filterParams.PageId);
        return result;
    }

    public async Task<SeoDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default)
    {
        var productStats = await context.Products
            .Select(product => new
            {
                product.Id,
                product.Name,
                product.Slug,
                Title = product.Seo != null ? product.Seo.Title : null,
                Description = product.Seo != null ? product.Seo.MetaDescription : null,
                Score = product.Seo != null ? product.Seo.SeoScore : null,
                Reviewed = product.Seo != null && product.Seo.IsReviewed == true,
                Keyword = product.Seo != null ? product.Seo.FocusKeyword : null,
            })
            .ToListAsync(cancellationToken);

        var postStats = await context.BlogPosts
            .Select(post => new
            {
                post.Id,
                post.Title,
                post.Slug,
                Title2 = post.Seo != null ? post.Seo.Title : null,
                Description = post.Seo != null ? post.Seo.MetaDescription : null,
                Score = post.Seo != null ? post.Seo.SeoScore : null,
                Reviewed = post.Seo != null && post.Seo.IsReviewed == true,
                Keyword = post.Seo != null ? post.Seo.FocusKeyword : null,
            })
            .ToListAsync(cancellationToken);

        var categoryStats = await context.Categories
            .Select(category => new
            {
                category.Id,
                category.Name,
                category.Slug,
                Title = category.Seo != null ? category.Seo.Title : null,
                Description = category.Seo != null ? category.Seo.MetaDescription : null,
                Score = category.Seo != null ? category.Seo.SeoScore : null,
                Reviewed = category.Seo != null && category.Seo.IsReviewed == true,
                Keyword = category.Seo != null ? category.Seo.FocusKeyword : null,
            })
            .ToListAsync(cancellationToken);

        var contents = new List<SeoContentScoreDto>();

        contents.AddRange(productStats.Select(item => new SeoContentScoreDto
        {
            EntityType = SeoEntityType.Product,
            EntityId = item.Id,
            Title = item.Name,
            Url = $"/product/{item.Slug}",
            EditUrl = $"/admin/products/edit/{item.Id}",
            Score = item.Score,
            HasTitle = !string.IsNullOrWhiteSpace(item.Title),
            HasDescription = !string.IsNullOrWhiteSpace(item.Description),
            IsReviewed = item.Reviewed,
        }));

        contents.AddRange(postStats.Select(item => new SeoContentScoreDto
        {
            EntityType = SeoEntityType.BlogPost,
            EntityId = item.Id,
            Title = item.Title,
            Url = $"/blog/{item.Slug}",
            EditUrl = $"/admin/blog/posts/edit/{item.Id}",
            Score = item.Score,
            HasTitle = !string.IsNullOrWhiteSpace(item.Title2),
            HasDescription = !string.IsNullOrWhiteSpace(item.Description),
            IsReviewed = item.Reviewed,
        }));

        contents.AddRange(categoryStats.Select(item => new SeoContentScoreDto
        {
            EntityType = SeoEntityType.Category,
            EntityId = item.Id,
            Title = item.Name,
            Url = $"/category/{item.Slug}",
            EditUrl = $"/admin/categories/edit/{item.Id}",
            Score = item.Score,
            HasTitle = !string.IsNullOrWhiteSpace(item.Title),
            HasDescription = !string.IsNullOrWhiteSpace(item.Description),
            IsReviewed = item.Reviewed,
        }));

        var scores = contents.Where(item => item.Score.HasValue).Select(item => item.Score!.Value).ToList();

        var keywords = productStats.Select(item => item.Keyword)
            .Concat(postStats.Select(item => item.Keyword))
            .Concat(categoryStats.Select(item => item.Keyword))
            .Where(keyword => !string.IsNullOrWhiteSpace(keyword))
            .Select(keyword => keyword!.Trim())
            .GroupBy(keyword => keyword)
            .OrderByDescending(group => group.Count())
            .Take(12)
            .Select(group => new KeywordIdeaDto { Keyword = group.Key, Count = group.Count() })
            .ToList();

        var dashboard = new SeoDashboardDto
        {
            ContentCount = contents.Count,
            OptimizedCount = contents.Count(item => item.HasTitle && item.HasDescription),
            MissingTitleCount = contents.Count(item => !item.HasTitle),
            MissingDescriptionCount = contents.Count(item => !item.HasDescription),
            LowScoreCount = contents.Count(item => item.Score is < 60),
            NotReviewedCount = contents.Count(item => !item.IsReviewed),
            AverageScore = scores.Count == 0 ? 0 : Math.Round(scores.Average(), 1),
            PendingAudits = await context.SeoAuditRecords.CountAsync(record => record.Status == SeoAuditStatus.PendingReview, cancellationToken),
            RedirectCount = await context.SeoRedirects.CountAsync(cancellationToken),
            NotFoundCount = await context.NotFoundLogs.CountAsync(log => !log.IsResolved, cancellationToken),
            VisitsLastThirtyDays = await statisticsService.GetTotalVisitsAsync(30, cancellationToken),
            WorstContent = contents.Where(item => item.Score.HasValue).OrderBy(item => item.Score).Take(8).ToList(),
            MissingSeoContent = contents.Where(item => !item.HasTitle || !item.HasDescription).Take(8).ToList(),
            KeywordIdeas = keywords,
        };

        return dashboard;
    }

    public Task<SeoAuditResult?> PreviewAsync(SeoAuditInput input, CancellationToken cancellationToken = default) =>
        Task.FromResult<SeoAuditResult?>(SmartSeoEngine.Analyze(input));

    // ==================================================================== internals

    /// <summary>ساخت ورودی موتور سئو از محتوای واقعی سایت.</summary>
    private async Task<SeoAuditInput?> BuildInputAsync(SeoEntityType entityType, Guid entityId, CancellationToken cancellationToken)
    {
        var settings = await settingService.GetStorefrontSettingsAsync();

        switch (entityType)
        {
            case SeoEntityType.Product:
            {
                var product = await context.Products
                    .Include(item => item.Images)
                    .Include(item => item.Features)
                    .Include(item => item.Category)
                    .FirstOrDefaultAsync(item => item.Id == entityId, cancellationToken);

                if (product is null)
                    return null;

                var ratings = await context.Comments
                    .Where(comment => comment.ProductId == entityId &&
                                      comment.Status == CommentStatus.Approved &&
                                      comment.Rating != null)
                    .Select(comment => comment.Rating!.Value)
                    .ToListAsync(cancellationToken);

                var brand = product.Features.FirstOrDefault(feature =>
                    feature.Key.Contains("برند") || feature.Key.ToLower().Contains("brand"));

                return new SeoAuditInput
                {
                    EntityType = SeoEntityType.Product,
                    EntityId = product.Id,
                    Title = product.Name,
                    Slug = product.Slug,
                    ShortDescription = product.ShortDescription,
                    Content = product.Description,
                    ImageAlt = product.Images.FirstOrDefault(image => image.IsPrimary)?.AltText
                               ?? product.Images.FirstOrDefault()?.AltText,
                    ImageCount = product.Images.Count,
                    FocusKeyword = product.Seo?.FocusKeyword,
                    CategoryName = product.Category?.Name,
                    BrandName = brand?.Value,
                    Price = PriceHelper.FinalPrice(product.Price, product.DiscountPrice, product.DiscountStartAt, product.DiscountEndAt),
                    RatingValue = ratings.Count == 0 ? null : Math.Round(ratings.Select(rating => (double)rating).Average(), 1),
                    RatingCount = ratings.Count,
                    StockStatus = product.StockQuantity > 0 ? "InStock" : "OutOfStock",
                    Features = BuildFeatureDictionary(product.Features),
                    Existing = product.Seo,
                    LinkCandidates = await GetLinkCandidatesAsync(),
                    SiteUrl = settings.SiteUrl,
                    SiteName = settings.SiteName,
                    Language = product.Seo?.Language ?? "fa-IR",
                };
            }
            case SeoEntityType.Category:
            {
                var category = await context.Categories
                    .Include(item => item.ParentCategory)
                    .FirstOrDefaultAsync(item => item.Id == entityId, cancellationToken);

                if (category is null)
                    return null;

                return new SeoAuditInput
                {
                    EntityType = SeoEntityType.Category,
                    EntityId = category.Id,
                    Title = category.Name,
                    Slug = category.Slug,
                    ShortDescription = category.Description,
                    Content = category.Description,
                    FocusKeyword = category.Seo?.FocusKeyword,
                    CategoryName = category.ParentCategory?.Name,
                    Existing = category.Seo,
                    LinkCandidates = await GetLinkCandidatesAsync(),
                    SiteUrl = settings.SiteUrl,
                    SiteName = settings.SiteName,
                    Language = category.Seo?.Language ?? "fa-IR",
                };
            }
            case SeoEntityType.BlogPost:
            {
                var post = await context.BlogPosts
                    .Include(item => item.BlogCategory)
                    .Include(item => item.Author)
                    .FirstOrDefaultAsync(item => item.Id == entityId, cancellationToken);

                if (post is null)
                    return null;

                return new SeoAuditInput
                {
                    EntityType = SeoEntityType.BlogPost,
                    EntityId = post.Id,
                    Title = post.Title,
                    Slug = post.Slug,
                    ShortDescription = post.Excerpt,
                    Content = post.Content,
                    ImageAlt = post.ImageAlt,
                    ImageCount = string.IsNullOrWhiteSpace(post.ImageName) ? 0 : 1,
                    FocusKeyword = post.Seo?.FocusKeyword,
                    CategoryName = post.BlogCategory?.Name,
                    BrandName = post.Author is null
                        ? null
                        : $"{post.Author.FirstName} {post.Author.LastName}".Trim(),
                    Existing = post.Seo,
                    LinkCandidates = await GetLinkCandidatesAsync(),
                    SiteUrl = settings.SiteUrl,
                    SiteName = settings.SiteName,
                    Language = post.Seo?.Language ?? "fa-IR",
                };
            }
            case SeoEntityType.BlogCategory:
            {
                var blogCategory = await context.BlogCategories
                    .FirstOrDefaultAsync(item => item.Id == entityId, cancellationToken);

                if (blogCategory is null)
                    return null;

                return new SeoAuditInput
                {
                    EntityType = SeoEntityType.BlogCategory,
                    EntityId = blogCategory.Id,
                    Title = blogCategory.Name,
                    Slug = blogCategory.Slug,
                    ShortDescription = blogCategory.Description,
                    Content = blogCategory.Description,
                    FocusKeyword = blogCategory.Seo?.FocusKeyword,
                    Existing = blogCategory.Seo,
                    LinkCandidates = await GetLinkCandidatesAsync(),
                    SiteUrl = settings.SiteUrl,
                    SiteName = settings.SiteName,
                    Language = blogCategory.Seo?.Language ?? "fa-IR",
                };
            }
            case SeoEntityType.Page:
            {
                var contentPage = await context.ContentPages
                    .FirstOrDefaultAsync(item => item.Id == entityId, cancellationToken);

                if (contentPage is null)
                    return null;

                var pageSeo = await context.PageSeos
                    .FirstOrDefaultAsync(item => item.Page == contentPage.Page, cancellationToken);

                return new SeoAuditInput
                {
                    EntityType = SeoEntityType.Page,
                    EntityId = contentPage.Id,
                    Title = contentPage.Title,
                    Slug = contentPage.Page.ToString(),
                    ShortDescription = contentPage.Summary,
                    Content = contentPage.Content,
                    FocusKeyword = pageSeo?.Seo?.FocusKeyword,
                    Existing = pageSeo?.Seo,
                    LinkCandidates = await GetLinkCandidatesAsync(),
                    SiteUrl = settings.SiteUrl,
                    SiteName = settings.SiteName,
                    Language = pageSeo?.Seo?.Language ?? "fa-IR",
                };
            }
            default:
                return null;
        }
    }

    /// <summary>ویژگی‌های محصول به‌صورت دیکشنری؛ کلیدهای تکراری نادیده گرفته می‌شوند.</summary>
    private static Dictionary<string, string> BuildFeatureDictionary(IEnumerable<ProductFeature> features)
    {
        var dictionary = new Dictionary<string, string>();
        foreach (var feature in features.OrderBy(item => item.DisplayOrder))
        {
            if (!string.IsNullOrWhiteSpace(feature.Key) && !dictionary.ContainsKey(feature.Key))
                dictionary[feature.Key] = feature.Value;
        }

        return dictionary;
    }

    private async Task<List<SeoLinkSuggestion>> GetLinkCandidatesAsync()
    {
        var candidates = new List<SeoLinkSuggestion>();

        candidates.AddRange(await context.Products
            .Where(product => product.IsActive)
            .OrderByDescending(product => product.ViewCount)
            .Take(20)
            .Select(product => new SeoLinkSuggestion
            {
                Title = product.Name,
                Url = $"/product/{product.Slug}",
                EntityType = SeoEntityType.Product,
            })
            .ToListAsync());

        candidates.AddRange(await context.Categories
            .Where(category => category.IsActive)
            .OrderBy(category => category.DisplayOrder)
            .Take(10)
            .Select(category => new SeoLinkSuggestion
            {
                Title = category.Name,
                Url = $"/category/{category.Slug}",
                EntityType = SeoEntityType.Category,
            })
            .ToListAsync());

        candidates.AddRange(await context.BlogPosts
            .Where(post => post.IsPublished)
            .OrderByDescending(post => post.PublishedAt)
            .Take(10)
            .Select(post => new SeoLinkSuggestion
            {
                Title = post.Title,
                Url = $"/blog/{post.Slug}",
                EntityType = SeoEntityType.BlogPost,
            })
            .ToListAsync());

        return candidates;
    }

    private async Task<SeoAuditDto> MapAsync(SeoAuditRecord record, CancellationToken cancellationToken)
    {
        var result = Deserialize(record.PayloadJson);
        var dto = new SeoAuditDto
        {
            Id = record.Id,
            CreatedAt = record.CreatedAt,
            UpdatedAt = record.UpdatedAt,
            EntityType = record.EntityType,
            EntityId = record.EntityId,
            Score = record.Score,
            Summary = record.Summary ?? result.Summary,
            Provider = record.Provider,
            Status = record.Status,
            AppliedAt = record.AppliedAt,
            Result = result,
        };

        switch (record.EntityType)
        {
            case SeoEntityType.Product:
            {
                var product = await context.Products
                    .Where(item => item.Id == record.EntityId)
                    .Select(item => new { item.Name, item.Slug })
                    .FirstOrDefaultAsync(cancellationToken);
                dto.EntityTitle = product?.Name ?? "محصول حذف‌شده";
                dto.EntityUrl = product is null ? null : $"/product/{product.Slug}";
                dto.EditUrl = $"/admin/products/edit/{record.EntityId}";
                break;
            }
            case SeoEntityType.Category:
            {
                var category = await context.Categories
                    .Where(item => item.Id == record.EntityId)
                    .Select(item => new { item.Name, item.Slug })
                    .FirstOrDefaultAsync(cancellationToken);
                dto.EntityTitle = category?.Name ?? "دسته‌بندی حذف‌شده";
                dto.EntityUrl = category is null ? null : $"/category/{category.Slug}";
                dto.EditUrl = $"/admin/categories/edit/{record.EntityId}";
                break;
            }
            case SeoEntityType.BlogPost:
            {
                var post = await context.BlogPosts
                    .Where(item => item.Id == record.EntityId)
                    .Select(item => new { item.Title, item.Slug })
                    .FirstOrDefaultAsync(cancellationToken);
                dto.EntityTitle = post?.Title ?? "مقاله حذف‌شده";
                dto.EntityUrl = post is null ? null : $"/blog/{post.Slug}";
                dto.EditUrl = $"/admin/blog/posts/edit/{record.EntityId}";
                break;
            }
            case SeoEntityType.BlogCategory:
            {
                var blogCategory = await context.BlogCategories
                    .Where(item => item.Id == record.EntityId)
                    .Select(item => new { item.Name, item.Slug })
                    .FirstOrDefaultAsync(cancellationToken);
                dto.EntityTitle = blogCategory?.Name ?? "دسته وبلاگ حذف‌شده";
                dto.EntityUrl = blogCategory is null ? null : $"/blog/category/{blogCategory.Slug}";
                dto.EditUrl = $"/admin/blog/categories/edit/{record.EntityId}";
                break;
            }
            default:
            {
                var page = await context.ContentPages
                    .Where(item => item.Id == record.EntityId)
                    .Select(item => new { item.Title })
                    .FirstOrDefaultAsync(cancellationToken);
                dto.EntityTitle = page?.Title ?? "صفحه حذف‌شده";
                dto.EditUrl = $"/admin/pages/edit/{record.EntityId}";
                break;
            }
        }

        return dto;
    }

    private static SeoAuditResult Deserialize(string payload)
    {
        try
        {
            return JsonSerializer.Deserialize<SeoAuditResult>(payload, JsonOptions) ?? new SeoAuditResult();
        }
        catch (JsonException)
        {
            return new SeoAuditResult { Summary = "پیشنهاد سئو قابل خواندن نبود؛ لطفاً دوباره اجرا کنید." };
        }
    }

    /// <summary>نوشتن فیلدهای پیشنهادی روی رکورد سئوی موجود (بدون تغییر مقادیر خالی).</summary>
    private static SeoMetaData? BuildSeo(SeoMetaData? current, SeoProposal proposal)
    {
        var seo = current ?? new SeoMetaData();

        seo.Title = Value(proposal.Title, seo.Title);
        seo.MetaDescription = Value(proposal.MetaDescription, seo.MetaDescription);
        seo.MetaKeywords = Value(proposal.MetaKeywords, seo.MetaKeywords);
        seo.SecondaryKeywordsJson = Value(proposal.SecondaryKeywordsJson, seo.SecondaryKeywordsJson);
        seo.FocusKeyword = Value(proposal.FocusKeyword, seo.FocusKeyword);
        seo.CanonicalUrl = Value(proposal.CanonicalUrl, seo.CanonicalUrl);
        seo.BreadcrumbTitle = Value(proposal.BreadcrumbTitle, seo.BreadcrumbTitle);
        seo.OgTitle = Value(proposal.OgTitle, seo.OgTitle);
        seo.OgDescription = Value(proposal.OgDescription, seo.OgDescription);
        seo.OgType = Value(proposal.OgType, seo.OgType);
        seo.XCard = Value(proposal.XCard, seo.XCard);
        seo.XTitle = Value(proposal.XTitle, seo.XTitle);
        seo.XDescription = Value(proposal.XDescription, seo.XDescription);
        seo.SchemaType = Value(proposal.SchemaType, seo.SchemaType);
        seo.StructuredDataJsonLd = Value(proposal.StructuredDataJsonLd, seo.StructuredDataJsonLd);
        seo.SitemapChangeFrequency = Value(proposal.SitemapChangeFrequency, seo.SitemapChangeFrequency);
        seo.OgImageAlt = Value(proposal.ImageAlt, seo.OgImageAlt);
        seo.IncludeInSitemap = proposal.IncludeInSitemap;
        seo.SitemapPriority = proposal.SitemapPriority ?? seo.SitemapPriority;

        return seo;
    }

    private static string? Value(string? proposed, string? current) =>
        string.IsNullOrWhiteSpace(proposed) ? current : proposed;

    private async Task<string> MakeSlugUniqueAsync(string slug, Guid entityId, CancellationToken cancellationToken)
    {
        var candidate = SlugHelper.Generate(slug);
        if (string.IsNullOrWhiteSpace(candidate))
            return slug;

        var index = 1;
        while (await context.Products.AnyAsync(product => product.Slug == candidate && product.Id != entityId, cancellationToken) ||
               await context.Categories.AnyAsync(category => category.Slug == candidate && category.Id != entityId, cancellationToken) ||
               await context.BlogPosts.AnyAsync(post => post.Slug == candidate && post.Id != entityId, cancellationToken))
        {
            candidate = $"{slug}-{index++}";
        }

        return candidate;
    }
}
