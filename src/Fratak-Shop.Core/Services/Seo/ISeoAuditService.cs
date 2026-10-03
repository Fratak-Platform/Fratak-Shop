using Fratak_Shop.Core.DTOs.Seo.Command;
using Fratak_Shop.Core.DTOs.Seo.Query;
using Fratak_Shop.Core.SeoEngine;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Entities.Seo;

namespace Fratak_Shop.Core.Services.Seo;

/// <summary>
/// «سئوی هوشمند» and «هوشمندسازی نوشته‌ها» of the admin panel.
/// The engine never writes to the content directly: every run stores a proposal in
/// <see cref="SeoAuditRecord"/>, the specialist reviews (and may edit) it, and only
/// the confirmed fields are applied to the product/post/page.
/// </summary>
public interface ISeoAuditService
{
    /// <summary>اجرای سئوی هوشمند روی یک محتوا و ساخت پیشنهاد قابل بررسی.</summary>
    Task<OperationResult<SeoAuditDto>> GenerateAsync(SeoEntityType entityType, Guid entityId, Guid? userId,
        CancellationToken cancellationToken = default);

    /// <summary>اجرای دسته‌جمعی سئوی هوشمند روی محتوایی که هنوز سئو نشده است.</summary>
    Task<OperationResult<int>> GenerateForMissingAsync(SeoEntityType entityType, int take, Guid? userId,
        CancellationToken cancellationToken = default);

    /// <summary>تولید پیش‌نویس بهبود متن (هوشمندسازی نوشته‌ها) برای بازبینی.</summary>
    Task<OperationResult<ContentEnhancementDto>> EnhanceAsync(SeoEntityType entityType, Guid entityId,
        CancellationToken cancellationToken = default);

    /// <summary>اعمال پیشنهاد تأییدشده روی محتوا (بدون ذخیره خودکار).</summary>
    Task<OperationResult> ApplyAsync(ApplySeoProposalCommand command, CancellationToken cancellationToken = default);

    /// <summary>اعمال خروجی هوشمندسازی نوشته‌ها که کارشناس تأیید کرده است.</summary>
    Task<OperationResult> ApplyContentAsync(ApplyContentEnhancementCommand command,
        CancellationToken cancellationToken = default);

    Task<OperationResult> RejectAsync(RejectSeoProposalCommand command, CancellationToken cancellationToken = default);

    Task<SeoAuditDto?> GetAsync(Guid id);

    Task<SeoAuditFilterResult> GetFilterAsync(SeoAuditFilterParams filterParams);

    /// <summary>خلاصه وضعیت سئوی سایت برای داشبورد مرکز سئو.</summary>
    Task<SeoDashboardDto> GetDashboardAsync(CancellationToken cancellationToken = default);

    /// <summary>تحلیل سریع بدون ذخیره (برای پیش‌نمایش زنده در فرم ویرایش).</summary>
    Task<SeoAuditResult?> PreviewAsync(SeoAuditInput input, CancellationToken cancellationToken = default);
}
