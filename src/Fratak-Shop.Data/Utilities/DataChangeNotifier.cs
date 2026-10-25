namespace Fratak_Shop.Data.Utilities;

/// <summary>
/// Process-wide signal "these kinds of data changed". Raised by <see cref="Contexts.FratakShopDbContext"/>
/// after every successful SaveChanges (with the entity types that were added / modified / deleted)
/// and by services after bulk updates (ExecuteUpdate). The web layer listens to clear page caches,
/// so anything changed – today by the storefront, later by the admin panel – shows up immediately.
/// </summary>
public static class DataChangeNotifier
{
    /// <summary>Raised synchronously after the change was committed. Handlers must be fast and never throw.</summary>
    public static event Action<IReadOnlyCollection<Type>>? Changed;

    /// <summary>Monotonic version per entity type – usable as a cache key part.</summary>
    private static readonly System.Collections.Concurrent.ConcurrentDictionary<Type, long> Versions = new();

    private static long _globalVersion;

    /// <summary>
    /// رویداد «ردیف‌های تازه ثبت شدند»: بعد از ذخیرهٔ موفق، فهرست موجودیت‌هایی که اضافه شده‌اند را می‌دهد.
    /// <para>
    /// کاربرد: اعلان زندهٔ کارکنان بدون این‌که سرویس‌های سفارش/تیکت/دیدگاه تغییر کنند.
    /// مثلاً وقتی یک <c>Order</c> تازه ذخیره شد، شنوندهٔ لایهٔ Web برای کارکنان اعلان «سفارش جدید» می‌سازد.
    /// شنونده باید سریع باشد و هرگز خطا پرتاب نکند (کار سنگین را در صف پس‌زمینه انجام دهد).
    /// </para>
    /// اگر هیچ شنونده‌ای ثبت نشده باشد، فهرست ساخته نمی‌شود و هزینه‌ای ندارد.
    /// </summary>
    public static event Action<IReadOnlyList<object>>? EntitiesAdded;

    /// <summary>آیا کسی به <see cref="EntitiesAdded"/> گوش می‌دهد؟ (برای جلوگیری از کار اضافه)</summary>
    public static bool HasAddedListeners => EntitiesAdded is not null;

    /// <summary>اعلام ردیف‌های تازه به شنونده‌ها؛ خطای شنونده عملیات اصلی را خراب نمی‌کند.</summary>
    public static void PublishAdded(IReadOnlyList<object> entities)
    {
        if (entities.Count == 0)
            return;

        var handlers = EntitiesAdded;
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList().Cast<Action<IReadOnlyList<object>>>())
        {
            try
            {
                handler(entities);
            }
            catch
            {
                // عملیات اصلی قبلاً ثبت شده است؛ خطای شنونده نباید آن را خراب کند.
            }
        }
    }

    public static long GlobalVersion => Interlocked.Read(ref _globalVersion);

    public static long VersionOf(Type type) => Versions.TryGetValue(type, out var version) ? version : 0;

    /// <summary>Entity types that changed at least once since start-up.</summary>
    public static ICollection<Type> KnownTypes() => Versions.Keys;

    public static void Publish(params Type[] types) => Publish((IReadOnlyCollection<Type>)types);

    public static void Publish(IReadOnlyCollection<Type> types)
    {
        if (types.Count == 0)
            return;

        foreach (var type in types)
            Versions.AddOrUpdate(type, 1, (_, version) => version + 1);
        Interlocked.Increment(ref _globalVersion);

        var handlers = Changed;
        if (handlers is null)
            return;

        foreach (var handler in handlers.GetInvocationList().Cast<Action<IReadOnlyCollection<Type>>>())
        {
            try
            {
                handler(types);
            }
            catch
            {
                // A cache listener must never break the business operation that already committed.
            }
        }
    }
}
