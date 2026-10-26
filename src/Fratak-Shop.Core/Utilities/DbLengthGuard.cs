using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Core.Utilities;

/// <summary>
/// محافظ طول ستون‌ها پیش از SaveChanges (مرحلهٔ هفتم – اصلاحیه).
/// <para>
/// مشکل: اگر متنی بلندتر از سقف ستون (MaxLength در مدل EF) ذخیره شود، SQL Server خطای ۲۶۲۸
/// «String or binary data would be truncated» می‌دهد و کل درخواست با صفحهٔ خطا تمام می‌شود
/// (همین اتفاق هنگام «تأیید» پیشنهاد سئو با توضیح بلند برند افتاد).
/// </para>
/// راه‌حل: قبل از ذخیره، همهٔ متن‌های افزوده/تغییرکردهٔ ChangeTracker (از جمله نوع‌های Owned مثل Seo) با سقف ستونشان مقایسه
/// می‌شوند و به جای خطای SQL یک پیام فارسی روشن برمی‌گردد. هیچ متنی بی‌صدا کوتاه نمی‌شود.
/// <para>استفاده: SeoEngineService.ApproveAsync؛ هر سرویس دیگری هم می‌تواند قبل از SaveChanges صدا بزند.</para>
/// </summary>
public static class DbLengthGuard
{
    /// <summary>
    /// اولین متن بلندتر از سقف ستون را پیدا می‌کند و پیام فارسی برمی‌گرداند؛ null یعنی همه چیز جا می‌شود.
    /// <example>مثال خروجی: «متن «Description» در «Brand» ۱٬۲۰۰ نویسه است ولی سقف این ستون ۱٬۰۰۰ نویسه است».</example>
    /// </summary>
    public static string? FindOverflow(DbContext context)
    {
        foreach (var entry in context.ChangeTracker.Entries())
        {
            if (entry.State is not (EntityState.Added or EntityState.Modified))
                continue;
            foreach (var property in entry.Properties)
            {
                if (property.CurrentValue is not string text || (entry.State == EntityState.Modified && !property.IsModified))
                    continue;
                var max = property.Metadata.GetMaxLength();
                if (max is > 0 && text.Length > max)
                    return $"متن «{property.Metadata.Name}» در «{entry.Metadata.ClrType.Name}» {text.Length:N0} نویسه است ولی سقف این ستون {max:N0} نویسه است";
            }
        }
        return null;
    }
}
