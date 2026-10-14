using Fratak_Shop.Core.DTOs.SeoRedirects.Command;
using Fratak_Shop.Core.DTOs.SeoRedirects.Query;
using Fratak_Shop.Core.Mappers.SeoRedirects;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.SeoRedirects;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.SeoRedirects;

/// <summary>
/// مدیریت ریدایرکت‌های سئو (۳۰۱/۳۰۲/۳۰۷/۳۰۸) تا بعد از تغییر آدرس محصولات،
/// مقالات و صفحات، لینک‌های قدیمی رتبه خود را از دست ندهند و ۴۰۴ نشوند.
/// </summary>
public class SeoRedirectService(FratakShopDbContext context) : ISeoRedirectService
{
    public async Task<OperationResult> CreateRedirectAsync(CreateSeoRedirectCommand command)
    {
        var error = Validate(command.FromPath, command.ToUrl);
        if (error is not null)
            return OperationResult.Error(error);

        var fromPath = NormalizeFromPath(command.FromPath);

        var exists = await context.SeoRedirects.AnyAsync(x => x.FromPath == fromPath);
        if (exists)
            return OperationResult.Error("برای این مسیر قبلاً ریدایرکت ثبت شده است.");

        if (IsSameDestination(fromPath, command.ToUrl))
            return OperationResult.Error("مسیر مقصد نباید با مسیر مبدأ یکسان باشد.");

        var model = new SeoRedirect
        {
            FromPath = fromPath,
            ToUrl = command.ToUrl.Trim(),
            Type = command.Type,
            Notes = command.Notes?.Trim(),
            IsActive = command.IsActive,
        };

        context.SeoRedirects.Add(model);
        await context.SaveChangesAsync();

        return OperationResult.Success("ریدایرکت با موفقیت ثبت شد.");
    }

    public async Task<OperationResult> EditRedirectAsync(EditSeoRedirectCommand command)
    {
        var redirect = await context.SeoRedirects.AsTracking().FirstOrDefaultAsync(x => x.Id == command.Id);
        if (redirect is null)
            return OperationResult.NotFound("ریدایرکت مورد نظر یافت نشد.");

        var error = Validate(command.FromPath, command.ToUrl);
        if (error is not null)
            return OperationResult.Error(error);

        var fromPath = NormalizeFromPath(command.FromPath);

        var exists = await context.SeoRedirects
            .AnyAsync(x => x.Id != command.Id && x.FromPath == fromPath);

        if (exists)
            return OperationResult.Error("برای این مسیر قبلاً ریدایرکت ثبت شده است.");

        if (IsSameDestination(fromPath, command.ToUrl))
            return OperationResult.Error("مسیر مقصد نباید با مسیر مبدأ یکسان باشد.");

        redirect.FromPath = fromPath;
        redirect.ToUrl = command.ToUrl.Trim();
        redirect.Type = command.Type;
        redirect.Notes = command.Notes?.Trim();
        redirect.IsActive = command.IsActive;

        await context.SaveChangesAsync();

        return OperationResult.Success("ریدایرکت با موفقیت ویرایش شد.");
    }

    public async Task<OperationResult> DeleteRedirectAsync(Guid id)
    {
        var redirect = await context.SeoRedirects.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (redirect is null)
            return OperationResult.NotFound("ریدایرکت مورد نظر یافت نشد.");

        context.SeoRedirects.Remove(redirect);
        await context.SaveChangesAsync();

        return OperationResult.Success("ریدایرکت با موفقیت حذف شد.");
    }

    public async Task<OperationResult> ChangeRedirectActivationAsync(Guid id, bool isActive)
    {
        var redirect = await context.SeoRedirects.AsTracking().FirstOrDefaultAsync(x => x.Id == id);
        if (redirect is null)
            return OperationResult.NotFound("ریدایرکت مورد نظر یافت نشد.");

        redirect.IsActive = isActive;
        await context.SaveChangesAsync();

        return OperationResult.Success(isActive
            ? "ریدایرکت فعال شد."
            : "ریدایرکت غیرفعال شد.");
    }

    public async Task<SeoRedirectFilterResult> GetRedirectsByFilterAsync(SeoRedirectFilterParams filterParams)
    {
        var query = context.SeoRedirects
            .OrderByDescending(x => x.CreatedAt)
            .AsQueryable();

        if (!string.IsNullOrWhiteSpace(filterParams.Search))
        {
            var search = filterParams.Search.Trim();
            query = query.Where(x => x.FromPath.Contains(search)
                                     || x.ToUrl.Contains(search)
                                     || (x.Notes != null && x.Notes.Contains(search)));
        }

        if (filterParams.Type.HasValue)
            query = query.Where(x => x.Type == filterParams.Type.Value);

        if (filterParams.IsActive.HasValue)
            query = query.Where(x => x.IsActive == filterParams.IsActive.Value);

        var skip = (filterParams.PageId - 1) * filterParams.Take;

        var redirects = await query
            .Skip(skip)
            .Take(filterParams.Take)
            .ToListAsync();

        var result = new SeoRedirectFilterResult
        {
            Data = redirects.Map(),
            FilterParams = filterParams,
        };

        result.GeneratePaging(query, filterParams.Take, filterParams.PageId);

        return result;
    }

    public List<SeoRedirectDto> GetAllRedirectsAsync()
    {
        return context.SeoRedirects
            .OrderByDescending(x => x.CreatedAt)
            .ToList()
            .Map();
    }

    public async Task<SeoRedirectDto?> GetRedirectByIdAsync(Guid id)
    {
        var redirect = await context.SeoRedirects.FirstOrDefaultAsync(x => x.Id == id);
        return redirect?.Map();
    }

    public async Task<SeoRedirectDto?> GetRedirectByPathAsync(string fromPath)
    {
        if (string.IsNullOrWhiteSpace(fromPath))
            return null;

        var path = NormalizeFromPath(fromPath);

        var redirect = await context.SeoRedirects
            .FirstOrDefaultAsync(x => x.IsActive && x.FromPath == path);

        return redirect?.Map();
    }

    private static string? Validate(string? fromPath, string? toUrl)
    {
        if (string.IsNullOrWhiteSpace(fromPath))
            return "مسیر مبدأ را وارد کنید.";

        if (string.IsNullOrWhiteSpace(toUrl))
            return "مسیر مقصد را وارد کنید.";

        return null;
    }

    /// <summary>
    /// یکسان‌سازی مسیر مبدأ: حذف فاصله‌های اضافه، افزودن اسلش ابتدایی و
    /// حذف اسلش‌های انتهایی تا مسیرهای نگهداری‌شده همیشه یک شکل باشند.
    /// </summary>
    private static string NormalizeFromPath(string fromPath)
    {
        var path = fromPath.Trim();

        if (!path.StartsWith('/'))
            path = "/" + path;

        while (path.Length > 1 && path.EndsWith('/'))
            path = path[..^1];

        return path;
    }

    private static bool IsSameDestination(string fromPath, string toUrl)
    {
        var destination = toUrl.Trim();

        if (!destination.StartsWith('/'))
            return false;

        return string.Equals(NormalizeFromPath(destination), fromPath, StringComparison.OrdinalIgnoreCase);
    }
}
