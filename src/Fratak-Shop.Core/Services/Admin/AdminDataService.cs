using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Utilities;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Services.Admin;

/// <summary>
/// Generic create / edit / delete of simple reference data in the admin panel (suppliers, banners, menus,
/// FAQ, loyalty rules, ...). Required fields and maximum lengths are checked against the EF model so the
/// user gets a clear message instead of a database error; unique-index violations become a friendly message.
/// Deletes are soft (see the DbContext). Caches follow automatically through the change notifier.
/// </summary>
public interface IAdminDataService
{
    /// <summary>Read-only query (no tracking) of an entity set.</summary>
    IQueryable<T> Query<T>() where T : BaseEntity;

    /// <summary>Tracked entity to edit, or null.</summary>
    Task<T?> FindAsync<T>(Guid id) where T : BaseEntity;

    Task<OperationResult<Guid>> AddAsync<T>(T entity) where T : BaseEntity;

    /// <summary>Saves the changes of an entity returned by <see cref="FindAsync{T}"/>.</summary>
    Task<OperationResult<Guid>> UpdateAsync<T>(T entity) where T : BaseEntity;

    Task<OperationResult> DeleteAsync<T>(Guid id) where T : BaseEntity;

    /// <summary>Validation against the model (required / max length) – null when valid.</summary>
    string? Validate<T>(T entity) where T : class;

    /// <summary>
    /// ردیف‌های حذف‌شده (سطل بازیافت) – فقط خواندنی.
    /// حذف در این پروژه نرم است (IsDeleted)، پس ردیف حذف‌شده هنوز در دیتابیس هست و می‌شود برش گرداند.
    /// استفاده: صفحهٔ «سطل بازیافت» و نمای «حذف‌شده‌ها» در جدول‌های مدیریت داده.
    /// </summary>
    IQueryable<T> QueryDeleted<T>() where T : BaseEntity;

    /// <summary>
    /// بازگرداندن یک ردیف از سطل بازیافت (IsDeleted=false). اگر در این فاصله ردیف دیگری با همان نامک/کد ساخته شده باشد
    /// پیام خطای قابل فهم برمی‌گردد. در گزارش فعالیت با عملیات «بازیابی» ثبت می‌شود.
    /// </summary>
    Task<OperationResult> RestoreAsync<T>(Guid id) where T : BaseEntity;
}

public class AdminDataService(FratakShopDbContext context) : IAdminDataService
{
    public IQueryable<T> Query<T>() where T : BaseEntity => context.Set<T>().AsNoTracking();

    public Task<T?> FindAsync<T>(Guid id) where T : BaseEntity =>
        context.Set<T>().AsTracking().FirstOrDefaultAsync(e => e.Id == id);

    public async Task<OperationResult<Guid>> AddAsync<T>(T entity) where T : BaseEntity
    {
        var error = Validate(entity);
        if (error is not null)
            return OperationResult<Guid>.Error(error);

        context.Set<T>().Add(entity);
        return await SaveAsync(entity, "مورد جدید با موفقیت ثبت شد.");
    }

    public async Task<OperationResult<Guid>> UpdateAsync<T>(T entity) where T : BaseEntity
    {
        var error = Validate(entity);
        if (error is not null)
            return OperationResult<Guid>.Error(error);

        if (context.Entry(entity).State == EntityState.Detached)
            context.Set<T>().Update(entity);

        return await SaveAsync(entity, "تغییرات با موفقیت ذخیره شد.");
    }

    public async Task<OperationResult> DeleteAsync<T>(Guid id) where T : BaseEntity
    {
        var entity = await FindAsync<T>(id);
        if (entity is null)
            return OperationResult.NotFound("مورد مورد نظر یافت نشد.");

        context.Set<T>().Remove(entity);
        try
        {
            await context.SaveChangesAsync();
            return OperationResult.Success("مورد حذف شد.");
        }
        catch (DbUpdateException exception)
        {
            context.ChangeTracker.Clear();
            return OperationResult.Error(Describe(exception));
        }
    }

    public IQueryable<T> QueryDeleted<T>() where T : BaseEntity =>
        context.Set<T>().IgnoreQueryFilters().AsNoTracking().Where(e => e.IsDeleted);

    public async Task<OperationResult> RestoreAsync<T>(Guid id) where T : BaseEntity
    {
        var entity = await context.Set<T>().IgnoreQueryFilters().AsTracking().FirstOrDefaultAsync(e => e.Id == id && e.IsDeleted);
        if (entity is null)
            return OperationResult.NotFound("این مورد در سطل بازیافت نیست (شاید قبلاً بازیابی شده است).");

        entity.IsDeleted = false;
        entity.DeletedAt = null;
        try
        {
            await context.SaveChangesAsync();
            return OperationResult.Success("مورد از سطل بازیافت برگردانده شد.");
        }
        catch (DbUpdateException exception)
        {
            context.ChangeTracker.Clear();
            return OperationResult.Error(Describe(exception));
        }
    }

    private async Task<OperationResult<Guid>> SaveAsync<T>(T entity, string message) where T : BaseEntity
    {
        try
        {
            await context.SaveChangesAsync();
            return OperationResult<Guid>.Success(entity.Id, message);
        }
        catch (DbUpdateException exception)
        {
            context.ChangeTracker.Clear();
            return OperationResult<Guid>.Error(Describe(exception));
        }
    }

    public string? Validate<T>(T entity) where T : class
    {
        var entityType = context.Model.FindEntityType(typeof(T));
        if (entityType is null)
            return null;

        foreach (var property in entityType.GetProperties())
        {
            if (property.IsShadowProperty() || property.PropertyInfo is null || property.ClrType != typeof(string))
                continue;

            var value = property.PropertyInfo.GetValue(entity) as string;
            if (!property.IsNullable && string.IsNullOrWhiteSpace(value))
                return $"فیلد «{property.Name}» الزامی است.";

            var max = property.GetMaxLength();
            if (max is > 0 && value?.Length > max)
                return $"طول فیلد «{property.Name}» حداکثر {max} نویسه است.";
        }

        foreach (var navigation in entityType.GetNavigations().Where(n => n.TargetEntityType.IsOwned()))
        {
            if (navigation.PropertyInfo?.GetValue(entity) is not { } owned)
                continue;

            foreach (var property in navigation.TargetEntityType.GetProperties())
            {
                if (property.PropertyInfo is null || property.ClrType != typeof(string))
                    continue;
                var max = property.GetMaxLength();
                if (max is > 0 && property.PropertyInfo.GetValue(owned) is string text && text.Length > max)
                    return $"طول فیلد «{navigation.Name}.{property.Name}» حداکثر {max} نویسه است.";
            }
        }

        return null;
    }

    private static string Describe(DbUpdateException exception) => exception.InnerException switch
    {
        SqlException { Number: 2601 or 2627 } => "این مقدار (مثلاً نامک، کد یا عنوان) قبلاً ثبت شده است؛ مقدار دیگری وارد کنید.",
        SqlException { Number: 547 } => "این مورد به داده‌های دیگری وابسته است یا مقدار یکی از فیلدها مجاز نیست.",
        SqlException { Number: 8152 or 2628 } => "طول یکی از مقادیر بیش از حد مجاز است.",
        _ => "ذخیره اطلاعات انجام نشد؛ مقادیر را بررسی کنید.",
    };
}
