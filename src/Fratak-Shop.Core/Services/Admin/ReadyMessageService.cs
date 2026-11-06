using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Contexts;
using Fratak_Shop.Data.Entities.Notifications;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Memory;

namespace Fratak_Shop.Core.Services.Admin;

/// <summary>یک پیام آماده برای فهرست انتخاب در فرم‌ها.</summary>
public sealed record ReadyMessageDto(Guid Id, string Name, string? Title, string Body, string? LinkUrl, ReadyMessageScope Scopes, int SortOrder, bool IsActive, int UsageCount);

/// <summary>فرم ذخیرهٔ پیام آماده (ایجاد اگر Id خالی باشد، وگرنه ویرایش).</summary>
public sealed class SaveReadyMessageCommand
{
    public Guid? Id { get; set; }
    public string? Name { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
    public string? LinkUrl { get; set; }
    public ReadyMessageScope Scopes { get; set; } = ReadyMessageScope.All;
    public int SortOrder { get; set; }
    public bool IsActive { get; set; } = true;
}

/// <summary>
/// مدیریت «پیام‌های آماده» (اعلان آماده و پاسخ آماده).
/// <para>
/// محل استفاده: صفحهٔ <c>/admin/ready-messages</c> برای تعریف؛ و انتخابگر پیام آماده در فرم‌های
/// ارسال اعلان، پاسخ تیکت، پاسخ دیدگاه، پیام تماس و پیام سفارش (با <see cref="ListAsync"/>).
/// </para>
/// </summary>
public interface IReadyMessageService
{
    /// <summary>پیام‌های فعال یک محل (پرکاربردها اول). برای فرم‌ها ۳۰ ثانیه کش می‌شود.</summary>
    Task<List<ReadyMessageDto>> ListAsync(ReadyMessageScope scope);

    /// <summary>همهٔ پیام‌ها (فعال و غیرفعال) برای صفحهٔ مدیریت.</summary>
    Task<List<ReadyMessageDto>> AllAsync();

    Task<OperationResult> SaveAsync(SaveReadyMessageCommand command);

    Task<OperationResult> DeleteAsync(Guid id);

    /// <summary>یک واحد به شمارندهٔ استفاده اضافه می‌کند (بی‌خطر؛ اگر نبود کاری نمی‌کند).</summary>
    Task MarkUsedAsync(Guid id);
}

public sealed class ReadyMessageService(FratakShopDbContext context, IMemoryCache cache) : IReadyMessageService
{
    private const string CacheKey = "admin:ready-messages";

    public async Task<List<ReadyMessageDto>> ListAsync(ReadyMessageScope scope)
    {
        var all = await cache.GetOrCreateAsync(CacheKey, entry =>
        {
            entry.AbsoluteExpirationRelativeToNow = TimeSpan.FromSeconds(30);
            return AllAsync();
        }) ?? [];

        return all.Where(m => m.IsActive && (m.Scopes & scope) != 0)
            .OrderByDescending(m => m.UsageCount).ThenBy(m => m.SortOrder).ThenBy(m => m.Name)
            .ToList();
    }

    public Task<List<ReadyMessageDto>> AllAsync() =>
        context.ReadyMessages.OrderBy(m => m.SortOrder).ThenBy(m => m.Name)
            .Select(m => new ReadyMessageDto(m.Id, m.Name, m.Title, m.Body, m.LinkUrl, m.Scopes, m.SortOrder, m.IsActive, m.UsageCount))
            .ToListAsync();

    public async Task<OperationResult> SaveAsync(SaveReadyMessageCommand command)
    {
        var name = command.Name.ToPlainText(100);
        var body = command.Body.ToPlainText(2000);
        if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(body))
            return OperationResult.Error("نام و متن پیام را وارد کنید.");

        var scopes = command.Scopes & ReadyMessageScope.All;
        if (scopes == ReadyMessageScope.None)
            return OperationResult.Error("حداقل یک محل نمایش را انتخاب کنید.");

        var link = string.IsNullOrWhiteSpace(command.LinkUrl) ? null : command.LinkUrl.Trim();
        if (link is not null && !(link.StartsWith('/') && !link.StartsWith("//") && !link.Contains('\\')) && !link.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
            return OperationResult.Error("لینک باید با / شروع شود یا آدرس کامل https باشد.");

        ReadyMessage? entity;
        if (command.Id is { } id)
        {
            entity = await context.ReadyMessages.AsTracking().FirstOrDefaultAsync(m => m.Id == id);
            if (entity is null)
                return OperationResult.NotFound("پیام آماده پیدا نشد.");
        }
        else
        {
            entity = new ReadyMessage();
            context.ReadyMessages.Add(entity);
        }

        entity.Name = name;
        entity.Title = command.Title.ToPlainText(200) is { Length: > 0 } title ? title : null;
        entity.Body = body;
        entity.LinkUrl = link;
        entity.Scopes = scopes;
        entity.SortOrder = Math.Clamp(command.SortOrder, -1000, 1000);
        entity.IsActive = command.IsActive;
        await context.SaveChangesAsync();
        cache.Remove(CacheKey);
        return OperationResult.Success("پیام آماده ذخیره شد.");
    }

    public async Task<OperationResult> DeleteAsync(Guid id)
    {
        var entity = await context.ReadyMessages.AsTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (entity is null)
            return OperationResult.NotFound("پیام آماده پیدا نشد.");

        context.ReadyMessages.Remove(entity);
        await context.SaveChangesAsync();
        cache.Remove(CacheKey);
        return OperationResult.Success("پیام آماده حذف شد.");
    }

    public async Task MarkUsedAsync(Guid id)
    {
        var entity = await context.ReadyMessages.AsTracking().FirstOrDefaultAsync(m => m.Id == id);
        if (entity is null)
            return;

        entity.UsageCount++;
        await context.SaveChangesAsync();
        cache.Remove(CacheKey);
    }
}
