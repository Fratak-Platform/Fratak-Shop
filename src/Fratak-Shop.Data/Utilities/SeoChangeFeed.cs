using Fratak_Shop.Data.Entities.SeoEngine;

namespace Fratak_Shop.Data.Utilities;

/// <summary>یک صفحهٔ سئوپذیر که همین الان ذخیره شد. مثال: <c>new SeoChange(SeoEntityKind.Product, id, Added: true)</c>.</summary>
public readonly record struct SeoChange(SeoEntityKind Kind, Guid Id, bool Added);

/// <summary>
/// خبررسان «صفحه‌های سئوپذیر ذخیره شدند» (دور سئوی خودکار).
/// <para>هر بار که <see cref="Contexts.FratakShopDbContext"/> محصول، دسته، برند، مقاله یا صفحهٔ محتوا را (از هر مسیری: پنل،
/// درون‌ریزی، همگام‌سازی تأمین‌کننده، کپی محصول) با موفقیت ذخیره کند، فهرست آن‌ها را اعلام می‌کند.
/// شنوندهٔ لایهٔ Web (صف «سئوی خودکار») چند ثانیه بعد فیلدهای خالی سئو را پر می‌کند، HTML متن را مرتب می‌کند و آدرس را به
/// IndexNow خبر می‌دهد.</para>
/// <para>جلوگیری از حلقه: ذخیره‌هایی که خودِ سئوی خودکار انجام می‌دهد داخل <see cref="Suppress"/> است و دوباره اعلام نمی‌شود.</para>
/// </summary>
/// <example>
/// <code>
/// SeoChangeFeed.Saved += changes => queue.Enqueue(changes);   // ثبت شنونده (یک بار، هنگام شروع برنامه)
/// using (SeoChangeFeed.Suppress()) await context.SaveChangesAsync();   // ذخیرهٔ خودِ سئوی خودکار
/// </code>
/// </example>
public static class SeoChangeFeed
{
    private static readonly AsyncLocal<int> SuppressDepth = new();

    /// <summary>بعد از ذخیرهٔ موفق صدا زده می‌شود؛ شنونده باید سریع باشد و خطا ندهد (کار سنگین در صف پس‌زمینه).</summary>
    public static event Action<IReadOnlyList<SeoChange>>? Saved;

    /// <summary>آیا کسی گوش می‌دهد؟ (اگر نه، فهرست اصلاً ساخته نمی‌شود)</summary>
    public static bool HasListeners => Saved is not null;

    /// <summary>آیا ذخیرهٔ فعلی از داخل خود سئوی خودکار است؟</summary>
    public static bool IsSuppressed => SuppressDepth.Value > 0;

    /// <summary>ذخیره‌های داخل این بازه اعلام نمی‌شوند (فقط همین جریان async).</summary>
    public static IDisposable Suppress()
    {
        SuppressDepth.Value++;
        return new Scope();
    }

    /// <summary>اعلام به شنونده‌ها؛ خطای شنونده عملیات اصلی را خراب نمی‌کند.</summary>
    public static void Publish(IReadOnlyList<SeoChange> changes)
    {
        if (changes.Count == 0 || IsSuppressed)
            return;
        var handlers = Saved;
        if (handlers is null)
            return;
        foreach (var handler in handlers.GetInvocationList().Cast<Action<IReadOnlyList<SeoChange>>>())
        {
            try
            {
                handler(changes);
            }
            catch
            {
                // ذخیره انجام شده است؛ شنوندهٔ خراب نباید آن را خراب کند.
            }
        }
    }

    private sealed class Scope : IDisposable
    {
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed)
                return;
            _disposed = true;
            SuppressDepth.Value = Math.Max(0, SuppressDepth.Value - 1);
        }
    }
}
