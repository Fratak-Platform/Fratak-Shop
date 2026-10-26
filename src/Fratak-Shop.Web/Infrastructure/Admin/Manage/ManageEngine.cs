using System.Globalization;
using System.Linq.Expressions;
using System.Reflection;
using Fratak_Shop.Core.Services.Admin;
using Fratak_Shop.Core.Utilities;
using Fratak_Shop.Data.Utilities;
using Fratak_Shop.Data.Utilities.SeoManagement;
using Fratak_Shop.Web.Helpers;
using Fratak_Shop.Web.Services;
using Microsoft.AspNetCore.Html;
using Microsoft.EntityFrameworkCore;

namespace Fratak_Shop.Web.Infrastructure.Admin.Manage;

/// <summary>
/// List / create / edit / delete of the tables in <see cref="ManageRegistry"/> through
/// <see cref="IAdminDataService"/> (model validation, friendly unique-index messages, soft delete, audit
/// and cache invalidation all come from there). Values are bound by reflection from the posted form,
/// strictly limited to the fields of the definition – nothing else of the entity can be changed.
/// </summary>
public sealed class ManageEngine(IAdminDataService data, IUploadStore uploads)
{
    public const int PageSize = 30;
    private static readonly CultureInfo Inv = CultureInfo.InvariantCulture;

    private sealed class Box<T>(T value)
    {
        public T Value { get; } = value;
    }

    // ── Generic dispatch ───────────────────────────────────────────────

    private Task<TResult> Call<TResult>(string method, Type entity, params object?[] args) =>
        (Task<TResult>)typeof(ManageEngine).GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance)!
            .MakeGenericMethod(entity).Invoke(this, args)!;

    public Task<ManageList> ListAsync(MDef def, string? search, Guid? parent, int page) =>
        ListAsync(def, new ManageQuery { Search = search, Parent = parent, Page = page });

    /// <summary>
    /// فهرست یک جدول با جست‌وجو، فیلتر والد، مرتب‌سازی دلخواه، اندازهٔ صفحه و نمای «سطل بازیافت».
    /// مقدارهای نامعتبر (ستون مرتب‌سازی ناشناخته، اندازهٔ صفحهٔ عجیب) بی‌صدا به پیش‌فرض برمی‌گردند.
    /// </summary>
    public Task<ManageList> ListAsync(MDef def, ManageQuery options) =>
        Call<ManageList>(nameof(ListCore), def.Entity, def, options);

    /// <summary>تعداد ردیف‌های حذف‌شدهٔ یک جدول (برای دکمهٔ «سطل بازیافت»).</summary>
    public Task<int> CountDeletedAsync(Type entity) => Call<int>(nameof(CountDeletedCore), entity);

    /// <summary>ردیف‌های حذف‌شده با برچسب و زمان حذف، جدیدترین اول (صفحهٔ /admin/trash).</summary>
    public Task<List<TrashRow>> TrashAsync(Type entity, string labelProperty, int page, int take) =>
        Call<List<TrashRow>>(nameof(TrashCore), entity, labelProperty, page, take);

    /// <summary>بازگرداندن یک ردیف از سطل بازیافت.</summary>
    public Task<OperationResult> RestoreAsync(Type entity, Guid id) => Call<OperationResult>(nameof(RestoreCore), entity, id);

    /// <summary>
    /// ترتیب دستی ردیف‌ها با کشیدن و رها کردن. <paramref name="ids"/> ردیف‌های همین صفحه به ترتیب جدید است.
    /// همان عددهای «ترتیب نمایش» فعلی این ردیف‌ها بین خودشان جابه‌جا می‌شود، پس جای ردیف‌های صفحه‌های دیگر به هم نمی‌خورد.
    /// </summary>
    public Task<OperationResult> ReorderAsync(MDef def, IReadOnlyList<Guid> ids) => Call<OperationResult>(nameof(ReorderCore), def.Entity, ids);

    /// <summary>آیا ترتیب دستی (کشیدن و رها کردن) برای این جدول ممکن است؟ (مرتب‌سازی پیش‌فرض با DisplayOrder عددی)</summary>
    public static bool CanReorder(MDef def) =>
        def.Sort == "DisplayOrder" && !def.SortDescending && def.Entity.GetProperty("DisplayOrder")?.PropertyType == typeof(int);

    public Task<object?> FindAsync(MDef def, Guid id) => Call<object?>(nameof(FindCore), def.Entity, id);

    public object Create(MDef def) => Activator.CreateInstance(def.Entity)!;

    public Task<OperationResult<Guid>> SaveAsync(MDef def, object entity, bool isNew) =>
        Call<OperationResult<Guid>>(nameof(SaveCore), def.Entity, entity, isNew);

    public Task<OperationResult> DeleteAsync(MDef def, Guid id) => Call<OperationResult>(nameof(DeleteCore), def.Entity, id);

    public Task<Dictionary<Guid, string>> OptionsAsync(Type refType, string label) =>
        Call<Dictionary<Guid, string>>(nameof(OptionsCore), refType, label);

    public async Task<Dictionary<string, Dictionary<Guid, string>>> AllOptionsAsync(MDef def)
    {
        var result = new Dictionary<string, Dictionary<Guid, string>>();
        foreach (var field in def.Fields.Where(f => f.Kind == MKind.Ref && f.RefType is not null))
            result[field.Name] = await OptionsAsync(field.RefType!, field.RefLabel);
        return result;
    }

    public Task<int> CountAsync(MDef def) => Call<int>(nameof(CountCore), def.Entity);

    // ── Core (typed) ───────────────────────────────────────────────────

    private async Task<int> CountCore<T>() where T : BaseEntity => await data.Query<T>().CountAsync();

    private async Task<ManageList> ListCore<T>(MDef def, ManageQuery options) where T : BaseEntity
    {
        var search = options.Search;
        var parent = options.Parent;
        var page = options.Page;
        var take = ManageQuery.AllowedPageSizes.Contains(options.Take) ? options.Take : PageSize;
        var query = options.Deleted ? data.QueryDeleted<T>() : data.Query<T>();
        var x = Expression.Parameter(typeof(T), "x");

        if (!string.IsNullOrWhiteSpace(search) && def.Search.Length > 0)
        {
            var term = Expression.Property(Expression.Constant(new Box<string>(SearchText.NormalizeDigits(search.Trim()))), nameof(Box<string>.Value));
            var contains = typeof(string).GetMethod(nameof(string.Contains), [typeof(string)])!;
            Expression? body = null;
            foreach (var name in def.Search)
            {
                var property = Expression.Property(x, name);
                var part = Expression.AndAlso(Expression.NotEqual(property, Expression.Constant(null, typeof(string))), Expression.Call(property, contains, term));
                body = body is null ? part : Expression.OrElse(body, part);
            }
            if (body is not null)
                query = query.Where(Expression.Lambda<Func<T, bool>>(body, x));
        }

        if (parent is { } parentId && def.ParentField is not null)
        {
            var property = Expression.Property(x, def.ParentField);
            var value = Expression.Convert(Expression.Property(Expression.Constant(new Box<Guid>(parentId)), nameof(Box<Guid>.Value)), property.Type);
            query = query.Where(Expression.Lambda<Func<T, bool>>(Expression.Equal(property, value), x));
        }

        var total = await query.CountAsync();

        // مرتب‌سازی: سطل بازیافت ← تازه‌حذف‌شده‌ها اول؛ ستون انتخابی کاربر (فقط ستون‌های مجاز تعریف)؛ یا پیش‌فرض تعریف جدول.
        IOrderedQueryable<T> sorted;
        if (options.Deleted)
        {
            sorted = query.OrderByDescending(e => e.DeletedAt);
        }
        else if (SortableField(def, options.Sort) is { } custom)
        {
            sorted = OrderBy(query, custom, options.Descending);
            sorted = sorted.ThenByDescending(e => e.CreatedAt);
        }
        else
        {
            var sortProperty = typeof(T).GetProperty(def.Sort) is not null ? def.Sort : nameof(BaseEntity.CreatedAt);
            sorted = OrderBy(query, sortProperty, def.SortDescending || sortProperty == nameof(BaseEntity.CreatedAt));
            if (sortProperty != nameof(BaseEntity.CreatedAt))
                sorted = sorted.ThenByDescending(e => e.CreatedAt);
        }

        // صفحهٔ بیش از آخرین صفحه (مثلاً بعد از حذف) به آخرین صفحه می‌رود.
        var pageCount = Math.Max(1, (int)Math.Ceiling(total / (double)take));
        page = Math.Clamp(page, 1, pageCount);
        var rows = await sorted.Skip((page - 1) * take).Take(take).ToListAsync();

        var refLabels = new Dictionary<string, Dictionary<Guid, string>>();
        foreach (var field in def.Fields.Where(f => f.InList && f.Kind == MKind.Ref && f.RefType is not null))
            refLabels[field.Name] = await OptionsAsync(field.RefType!, field.RefLabel);

        return new ManageList(rows.Cast<object>().ToList(), total, page, take, refLabels);
    }

    /// <summary>
    /// ستون مرتب‌سازی درخواستی اگر مجاز باشد: یکی از فیلدهای تعریف (غیر از متن HTML و تصویر) یا زمان ثبت/ویرایش؛ در غیر این صورت null.
    /// (نام ستون از آدرس می‌آید؛ هرگز مستقیم به Expression داده نمی‌شود مگر اینکه در فهرست مجاز باشد.)
    /// </summary>
    public static string? SortableField(MDef def, string? sort)
    {
        if (string.IsNullOrEmpty(sort))
            return null;
        if (sort is nameof(BaseEntity.CreatedAt) or nameof(BaseEntity.UpdatedAt))
            return sort;
        var field = def.Field(sort);
        if (field is null || field.Kind is MKind.Html or MKind.Image || def.Entity.GetProperty(field.Name) is null)
            return null;
        return field.Name;
    }

    private async Task<int> CountDeletedCore<T>() where T : BaseEntity => await data.QueryDeleted<T>().CountAsync();

    private async Task<List<TrashRow>> TrashCore<T>(string labelProperty, int page, int take) where T : BaseEntity
    {
        var query = data.QueryDeleted<T>()
            .OrderByDescending(e => e.DeletedAt)
            .Skip((Math.Max(1, page) - 1) * take)
            .Take(take);
        // جدولی که فیلد متنی مناسب ندارد فقط با شناسه نشان داده می‌شود.
        var rows = labelProperty.Length == 0
            ? await query.Select(e => new { e.Id, Label = (string?)null, e.DeletedAt }).ToListAsync()
            : await query.Select(e => new { e.Id, Label = (string?)EF.Property<string>(e, labelProperty), e.DeletedAt }).ToListAsync();
        return rows.Select(r => new TrashRow(r.Id, string.IsNullOrWhiteSpace(r.Label) ? r.Id.ToString()[..8] : r.Label, r.DeletedAt)).ToList();
    }

    private async Task<OperationResult> RestoreCore<T>(Guid id) where T : BaseEntity => await data.RestoreAsync<T>(id);

    private async Task<OperationResult> ReorderCore<T>(IReadOnlyList<Guid> ids) where T : BaseEntity
    {
        var distinct = ids.Distinct().ToList();
        if (distinct.Count is 0 or > 200)
            return OperationResult.Error("ترتیب ارسالی نامعتبر است.");

        var property = typeof(T).GetProperty("DisplayOrder");
        if (property?.PropertyType != typeof(int))
            return OperationResult.Error("این جدول ترتیب دستی ندارد.");

        var entities = new List<T>();
        foreach (var id in distinct)
        {
            var entity = await data.FindAsync<T>(id);
            if (entity is null)
                return OperationResult.NotFound("یکی از ردیف‌ها پیدا نشد؛ صفحه را تازه کنید.");
            entities.Add(entity);
        }

        // همان عددهای فعلی (مرتب‌شده) به ترتیب جدید داده می‌شود؛ اگر تکراری باشند (مثلاً همه صفر) از کمترین عدد پشت سر هم شماره می‌خورند.
        var current = entities.Select(e => (int)property.GetValue(e)!).OrderBy(v => v).ToList();
        if (current.Distinct().Count() != current.Count)
        {
            var start = current[0];
            current = Enumerable.Range(0, current.Count).Select(i => start + i).ToList();
        }
        for (var i = 0; i < entities.Count; i++)
            property.SetValue(entities[i], current[i]);

        var result = await data.UpdateAsync(entities[0]);
        return result.IsSuccess() ? OperationResult.Success("ترتیب جدید ذخیره شد.") : OperationResult.Error(result.Message ?? "ذخیره نشد.");
    }

    private static IOrderedQueryable<T> OrderBy<T>(IQueryable<T> query, string property, bool descending)
    {
        var x = Expression.Parameter(typeof(T), "x");
        var member = Expression.Property(x, property);
        var lambda = Expression.Lambda(member, x);
        var method = typeof(Queryable).GetMethods()
            .First(m => m.Name == (descending ? nameof(Queryable.OrderByDescending) : nameof(Queryable.OrderBy)) && m.GetParameters().Length == 2)
            .MakeGenericMethod(typeof(T), member.Type);
        return (IOrderedQueryable<T>)method.Invoke(null, [query, lambda])!;
    }

    private async Task<object?> FindCore<T>(Guid id) where T : BaseEntity => await data.FindAsync<T>(id);

    private async Task<OperationResult<Guid>> SaveCore<T>(object entity, bool isNew) where T : BaseEntity =>
        isNew ? await data.AddAsync((T)entity) : await data.UpdateAsync((T)entity);

    private async Task<OperationResult> DeleteCore<T>(Guid id) where T : BaseEntity => await data.DeleteAsync<T>(id);

    private async Task<Dictionary<Guid, string>> OptionsCore<T>(string label) where T : BaseEntity
    {
        var rows = await data.Query<T>()
            .Select(e => new { e.Id, Label = EF.Property<string>(e, label) })
            .OrderBy(e => e.Label)
            .Take(3000)
            .ToListAsync();
        return rows.ToDictionary(r => r.Id, r => r.Label ?? r.Id.ToString());
    }

    // ── Values ─────────────────────────────────────────────────────────

    public static object? Get(object entity, MField field) => entity.GetType().GetProperty(field.Name)?.GetValue(entity);

    public static Guid IdOf(object entity) => ((BaseEntity)entity).Id;

    /// <summary>Text for an input's value attribute.</summary>
    public static string InputValue(object entity, MField field)
    {
        var value = Get(entity, field);
        return value switch
        {
            null => "",
            decimal d => d.ToString("0.##", Inv),
            int i => i.ToString(Inv),
            DateTimeOffset date => DateInput(date),
            TimeOnly time => time.ToString("HH:mm", Inv),
            Guid g => g.ToString(),
            Enum e => e.ToString(),
            _ => value.ToString() ?? "",
        };
    }

    public static string DateInput(DateTimeOffset date)
    {
        var local = IranClock.ToLocal(date);
        var text = PersianDateInput.Format(DateOnly.FromDateTime(local.DateTime));
        return local.TimeOfDay == TimeSpan.Zero ? text : $"{text} {local:HH:mm}";
    }

    public static IEnumerable<(string Value, string Label)> EnumOptions(MDef def, MField field)
    {
        var property = def.Entity.GetProperty(field.Name)!;
        var type = Nullable.GetUnderlyingType(property.PropertyType) ?? property.PropertyType;
        foreach (var name in Enum.GetNames(type))
        {
            if (field.Labels is not null && field.Labels.TryGetValue(name, out var label))
                yield return (name, label);
            else if (name != "None" && field.Labels is null)
                yield return (name, name);
        }
    }

    public static string ImageUrl(MField field, string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return "";
        if (value.StartsWith('/') || value.StartsWith("http", StringComparison.OrdinalIgnoreCase))
            return value;
        return $"/uploads/{field.Area?.Name ?? "content"}/{Uri.EscapeDataString(value)}";
    }

    /// <summary>Cell of the list table.</summary>
    public static IHtmlContent Cell(MDef def, MField field, object entity, IReadOnlyDictionary<string, Dictionary<Guid, string>> refLabels)
    {
        var value = Get(entity, field);
        switch (field.Kind)
        {
            case MKind.Bool:
                return new HtmlString(value is true ? "<span class=\"a-badge a-badge-green\">بله</span>" : "<span class=\"a-badge a-badge-gray\">خیر</span>");
            case MKind.Image:
                var url = ImageUrl(field, value as string);
                return url.Length == 0
                    ? new HtmlString("<span class=\"a-muted\">—</span>")
                    : new HtmlString($"<img class=\"a-thumb\" src=\"{Enc(url)}\" alt=\"\" loading=\"lazy\">");
            case MKind.Enum:
                var name = value?.ToString() ?? "";
                return new HtmlString(Enc(field.Labels is not null && field.Labels.TryGetValue(name, out var l) ? l : name));
            case MKind.Ref:
                if (value is Guid id && refLabels.TryGetValue(field.Name, out var labels))
                    return new HtmlString(Enc(labels.GetValueOrDefault(id, "—")));
                return new HtmlString("<span class=\"a-muted\">—</span>");
            case MKind.Decimal:
                return new HtmlString(value is decimal d ? Enc(Format.Price(d)) : "—");
            case MKind.Date:
                return new HtmlString(value is DateTimeOffset date ? Enc(Format.DateTime(date)) : "<span class=\"a-muted\">—</span>");
            case MKind.Color:
                var color = value as string;
                return new HtmlString(string.IsNullOrEmpty(color) ? "—" : $"<span class=\"inline-block w-4 h-4 rounded align-middle border\" style=\"background:{Enc(color)}\"></span> <span class=\"a-ltr\">{Enc(color)}</span>");
            default:
                var text = value switch
                {
                    null => "",
                    int i => Format.Number(i),
                    TimeOnly t => t.ToString("HH:mm", Inv),
                    DateTimeOffset moment => Format.DateTime(moment),
                    Enum e when field.Labels is not null && field.Labels.TryGetValue(e.ToString(), out var enumLabel) => enumLabel,
                    _ => value.ToString() ?? "",
                };
                if (field.Kind == MKind.Html)
                    text = text.ToPlainText(80);
                else if (text.Length > 80)
                    text = text[..79] + "…";
                return new HtmlString(field.Ltr ? $"<span class=\"a-ltr\">{Enc(text)}</span>" : Enc(text));
        }
    }

    private static string Enc(string value) => System.Net.WebUtility.HtmlEncode(value);

    // ── Binding ────────────────────────────────────────────────────────

    /// <summary>
    /// Copies the posted values of the definition's fields onto <paramref name="entity"/>. Returns an error
    /// message or null; <paramref name="savedFiles"/> receives new uploads (to delete when saving fails).
    /// </summary>
    public async Task<string?> BindAsync(MDef def, object entity, IFormCollection form, List<(UploadArea Area, string Name)> savedFiles, CancellationToken cancellationToken)
    {
        foreach (var field in def.Fields)
        {
            var fieldError = await BindFieldAsync(def, field, entity, form, savedFiles, cancellationToken);
            if (fieldError is not null)
                return fieldError;
        }

        return BindSeo(def, entity, form);
    }

    /// <summary>
    /// ویرایش درجای یک خانهٔ جدول: فقط همان فیلد با همان قواعد فرم کامل (اعتبارسنجی، پاک‌سازی HTML، تبدیل تاریخ) مقدار می‌گیرد.
    /// فقط فیلدهایی که <see cref="MField.CanInline"/> دارند مجازند. خطا یا null برمی‌گرداند.
    /// </summary>
    /// <example>فیلد «ترتیب نمایش» با مقدار «۱۲» ← DisplayOrder = 12.</example>
    public async Task<string?> BindInlineAsync(MDef def, MField field, object entity, string? value, CancellationToken cancellationToken)
    {
        if (!field.CanInline)
            return "این فیلد در جدول قابل ویرایش نیست؛ از فرم ویرایش استفاده کنید.";
        var values = new Dictionary<string, Microsoft.Extensions.Primitives.StringValues>(StringComparer.Ordinal)
        {
            [field.Name] = field.Kind == MKind.Bool ? (value == "true" ? "true" : "false") : (value ?? ""),
        };
        return await BindFieldAsync(def, field, entity, new FormCollection(values), [], cancellationToken);
    }

    private async Task<string?> BindFieldAsync(MDef def, MField field, object entity, IFormCollection form, List<(UploadArea Area, string Name)> savedFiles, CancellationToken cancellationToken)
    {
        {
            if (field.Kind == MKind.ReadOnly)
                return null;

            var property = def.Entity.GetProperty(field.Name);
            if (property is null || !property.CanWrite)
                return null;

            var type = property.PropertyType;
            var underlying = Nullable.GetUnderlyingType(type) ?? type;
            var nullable = !type.IsValueType || Nullable.GetUnderlyingType(type) is not null;
            var raw = form[field.Name].FirstOrDefault()?.Trim();
            if (raw is { Length: 0 })
                raw = null;
            var required = $"«{field.Label}» را وارد کنید.";

            switch (field.Kind)
            {
                case MKind.Bool:
                    property.SetValue(entity, form[field.Name].Contains("true"));
                    break;

                case MKind.Text or MKind.TextArea or MKind.Html or MKind.Url or MKind.Slug or MKind.Color:
                    if (field.Kind == MKind.Slug && raw is null && field.SlugFrom is not null)
                    {
                        var source = form[field.SlugFrom].FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(source))
                            raw = Slugify(source);
                    }
                    else if (field.Kind == MKind.Slug && raw is not null)
                    {
                        raw = Slugify(raw);
                    }

                    if (field.Kind == MKind.Html)
                        raw = raw.SanitizeOrNull();
                    if (field.Kind == MKind.Url && raw is not null && !IsSafeUrl(raw))
                        return $"«{field.Label}» باید با / شروع شود یا آدرس کامل http(s) باشد.";
                    if (field.Kind == MKind.Color && raw is not null && !System.Text.RegularExpressions.Regex.IsMatch(raw, "^#[0-9a-fA-F]{3,8}$"))
                        return $"«{field.Label}» باید کد رنگ مانند #0069FD باشد.";
                    if (raw is null && field.Required)
                        return required;
                    property.SetValue(entity, raw);
                    break;

                case MKind.Int:
                    if (raw is null)
                    {
                        if (field.Required || !nullable)
                        {
                            if (field.Required)
                                return required;
                            property.SetValue(entity, 0);
                        }
                        else
                        {
                            property.SetValue(entity, null);
                        }
                        break;
                    }
                    if (!int.TryParse(SearchText.NormalizeDigits(raw).Replace(",", ""), NumberStyles.Integer, Inv, out var number))
                        return $"«{field.Label}» باید عدد صحیح باشد.";
                    property.SetValue(entity, number);
                    break;

                case MKind.Decimal:
                    if (raw is null)
                    {
                        if (field.Required)
                            return required;
                        property.SetValue(entity, nullable ? null : 0m);
                        break;
                    }
                    if (!decimal.TryParse(SearchText.NormalizeDigits(raw).Replace(",", "").Replace("٫", "."), NumberStyles.Number, Inv, out var amount) || amount < 0)
                        return $"«{field.Label}» باید عدد مثبت باشد.";
                    property.SetValue(entity, amount);
                    break;

                case MKind.Enum:
                    if (raw is null || !Enum.TryParse(underlying, raw, out var enumValue) || !Enum.IsDefined(underlying, enumValue!))
                    {
                        if (raw is null && nullable && !field.Required)
                        {
                            property.SetValue(entity, null);
                            break;
                        }
                        return $"«{field.Label}» را انتخاب کنید.";
                    }
                    property.SetValue(entity, enumValue);
                    break;

                case MKind.Ref:
                    if (raw is null)
                    {
                        if (field.Required || !nullable)
                            return $"«{field.Label}» را انتخاب کنید.";
                        property.SetValue(entity, null);
                        break;
                    }
                    if (!Guid.TryParse(raw, out var refId))
                        return $"«{field.Label}» نامعتبر است.";
                    if (field.Name == "ParentId" && entity is BaseEntity self && self.Id == refId)
                        return "یک مورد نمی‌تواند والد خودش باشد.";
                    property.SetValue(entity, refId);
                    break;

                case MKind.Date:
                    if (raw is null)
                    {
                        if (field.Required)
                            return required;
                        property.SetValue(entity, null);
                        break;
                    }
                    var parsed = ParseDate(raw);
                    if (parsed is null)
                        return $"«{field.Label}» را به‌صورت ۱۴۰۴/۰۸/۰۱ یا ۱۴۰۴/۰۸/۰۱ ۱۴:۳۰ وارد کنید.";
                    property.SetValue(entity, parsed);
                    break;

                case MKind.Time:
                    if (raw is null || !TimeOnly.TryParse(SearchText.NormalizeDigits(raw), Inv, out var time))
                        return $"«{field.Label}» را به‌صورت ۰۹:۳۰ وارد کنید.";
                    property.SetValue(entity, time);
                    break;

                case MKind.Image:
                    var file = form.Files.GetFile(field.Name);
                    if (file is { Length: > 0 } && field.Area is not null)
                    {
                        var (files, error) = await uploads.SaveAsync(field.Area, [file], 1, 4_096, cancellationToken);
                        if (error is not null)
                            return $"«{field.Label}»: {error}";
                        var name = files[0].FileName;
                        savedFiles.Add((field.Area, name));
                        property.SetValue(entity, field.StoreAsPath ? $"/uploads/{field.Area.Name}/{name}" : name);
                    }
                    else if (form[field.Name + "__remove"].Contains("true") && !field.Required)
                    {
                        property.SetValue(entity, null);
                    }
                    else if (field.Required && string.IsNullOrEmpty(property.GetValue(entity) as string))
                    {
                        return $"«{field.Label}» را بارگذاری کنید.";
                    }
                    break;
            }
        }

        return null;
    }

    private static string? BindSeo(MDef def, object entity, IFormCollection form)
    {
        if (def.HasSeo && def.Entity.GetProperty("Seo") is { } seoProperty)
        {
            var seo = new SeoInput
            {
                Title = form["Seo.Title"].FirstOrDefault(),
                MetaDescription = form["Seo.MetaDescription"].FirstOrDefault(),
                FocusKeyword = form["Seo.FocusKeyword"].FirstOrDefault(),
                MetaKeywords = form["Seo.MetaKeywords"].FirstOrDefault(),
                CanonicalUrl = form["Seo.CanonicalUrl"].FirstOrDefault(),
                BreadcrumbTitle = form["Seo.BreadcrumbTitle"].FirstOrDefault(),
                OgTitle = form["Seo.OgTitle"].FirstOrDefault(),
                OgDescription = form["Seo.OgDescription"].FirstOrDefault(),
                NoIndex = form["Seo.NoIndex"].FirstOrDefault() == "true",
                NoFollow = form["Seo.NoFollow"].FirstOrDefault() == "true",
            };
            var error = seo.Validate();
            if (error is not null)
                return error;
            seoProperty.SetValue(entity, seo.ApplyToCopy(seoProperty.GetValue(entity) as SeoMetaData));
        }

        return null;
    }

    public void DeleteFiles(List<(UploadArea Area, string Name)> files)
    {
        foreach (var group in files.GroupBy(f => f.Area))
            uploads.Delete(group.Key, group.Select(f => f.Name));
    }

    private static bool IsSafeUrl(string url) =>
        url.Length <= 2000
        && ((url.StartsWith('/') && !url.StartsWith("//"))
            || url.StartsWith('#')
            || (Uri.TryCreate(url, UriKind.Absolute, out var uri) && (uri.Scheme == Uri.UriSchemeHttps || uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == "tel" || uri.Scheme == "mailto")));

    private static DateTimeOffset? ParseDate(string raw)
    {
        var parts = SearchText.NormalizeDigits(raw).Split(' ', StringSplitOptions.RemoveEmptyEntries);
        var date = PersianDateInput.Parse(parts[0]);
        if (date is null)
            return null;
        var time = TimeOnly.MinValue;
        if (parts.Length > 1 && !TimeOnly.TryParse(parts[1], Inv, out time))
            return null;
        return IranClock.At(date.Value, time);
    }

    public static string Slugify(string value) => Areas.Admin.Pages.Products.EditModel.Slugify(value);
}
