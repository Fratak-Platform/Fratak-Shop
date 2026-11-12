using System.Runtime.InteropServices;
using System.Text.Json;
using Microsoft.Data.SqlClient;

namespace Fratak_Shop.Tests;

/// <summary>
/// پیدا کردن خودکار SQL Server برای آزمون‌های Integration و E2E، تا روی سیستم برنامه‌نویس هم بدون هیچ تنظیمی
/// (مثلاً با Test Explorer ویژوال استودیو) اجرا شوند.
/// <para>ترتیب جست‌وجو:</para>
/// <list type="number">
/// <item>متغیر محیطی <c>FRATAK_TEST_SQL</c> (در CI). اگر تنظیم شده باشد فقط همین استفاده می‌شود؛ خرابی‌اش باید دیده شود.</item>
/// <item>رشتهٔ اتصال خود برنامه: <c>ConnectionDefault</c> در <c>appsettings.Development.json</c> و <c>appsettings.json</c>
/// پروژهٔ وب (همان سروری که برنامه با آن اجرا می‌شود، مثلاً <c>Server=.;User Id=sa;...</c>) – <b>فقط اگر سرور روی همین
/// دستگاه باشد</b>؛ سرور راه‌دور (مثلاً پایگاه دادهٔ واقعی سایت) هرگز خودکار استفاده نمی‌شود.</item>
/// <item>روی ویندوز: <c>(localdb)\MSSQLLocalDB</c> و نمونهٔ پیش‌فرض محلی با احراز هویت ویندوز.</item>
/// </list>
/// نام پایگاه داده همیشه حذف می‌شود: هر کلاس آزمون پایگاه دادهٔ موقت خودش را (FratakTests_… / FratakE2E_…) می‌سازد و
/// در پایان پاک می‌کند؛ به پایگاه دادهٔ اصلی برنامه هرگز دست زده نمی‌شود.
/// اگر هیچ سروری در دسترس نباشد، این آزمون‌ها با پیام راهنما «Skipped» می‌شوند، نه «Failed».
/// </summary>
public static class TestSql
{
    public const string EnvironmentVariable = "FRATAK_TEST_SQL";

    private static readonly Lazy<(string? Server, string? Problem)> Resolved = new(Resolve, LazyThreadSafetyMode.ExecutionAndPublication);

    /// <summary>رشتهٔ اتصال سرور (بدون نام پایگاه داده) یا null.</summary>
    public static string? Server => Resolved.Value.Server;

    /// <summary>دلیل رد شدن آزمون‌ها وقتی سروری پیدا نشد؛ null یعنی سرور آماده است.</summary>
    public static string? SkipReason => Resolved.Value.Server is null ? Resolved.Value.Problem : null;

    /// <summary>سرور را برمی‌گرداند یا با پیام راهنما خطا می‌دهد (برای fixtureها).</summary>
    public static string RequireServer() =>
        Server ?? throw new InvalidOperationException(SkipReason);

    private static (string?, string?) Resolve()
    {
        var fromEnvironment = Environment.GetEnvironmentVariable(EnvironmentVariable);
        if (!string.IsNullOrWhiteSpace(fromEnvironment))
            return (WithoutDatabase(fromEnvironment), null);

        var tried = new List<string>();
        foreach (var candidate in Candidates())
        {
            string server;
            try
            {
                server = WithoutDatabase(candidate);
            }
            catch (ArgumentException)
            {
                continue; // رشتهٔ اتصال خراب در appsettings
            }

            if (!IsLocalServer(server))
            {
                // مرحلهٔ هفدهم: رشتهٔ اتصال appsettings ممکن است به سرور واقعی (Production) اشاره کند؛ آزمون‌ها پایگاه دادهٔ
                // موقت می‌سازند و پاک می‌کنند، پس هرگز به سرور راه‌دور وصل نمی‌شوند (فقط با FRATAK_TEST_SQL صریح).
                tried.Add($"{Describe(server)} → سرور راه‌دور است؛ برای امنیت دادهٔ واقعی استفاده نشد");
                continue;
            }

            var problem = Probe(server);
            if (problem is null)
                return (server, null);
            tried.Add($"{Describe(server)} → {problem}");
        }

        var message = "آزمون‌های Integration/E2E به SQL Server نیاز دارند و سروری در دسترس نبود. "
                      + $"متغیر محیطی {EnvironmentVariable} را با یک رشتهٔ اتصال بدون Database تنظیم کنید "
                      + "(مثلاً Server=.;User Id=sa;Password=...;TrustServerCertificate=True) یا SQL Server / LocalDB را روشن کنید. "
                      + (tried.Count > 0 ? "امتحان‌شده‌ها: " + string.Join(" | ", tried) : "");
        return (null, message);
    }

    private static IEnumerable<string> Candidates()
    {
        var web = FindWebProject();
        if (web is not null)
        {
            foreach (var file in new[] { "appsettings.Development.json", "appsettings.json" })
            {
                var value = ReadConnection(Path.Combine(web, file));
                if (!string.IsNullOrWhiteSpace(value))
                    yield return value;
            }
        }

        if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
        {
            yield return @"Server=(localdb)\MSSQLLocalDB;Integrated Security=True;TrustServerCertificate=True";
            yield return "Server=.;Integrated Security=True;TrustServerCertificate=True";
            yield return @"Server=.\SQLEXPRESS;Integrated Security=True;TrustServerCertificate=True";
        }
    }

    /// <summary>پوشهٔ src/Fratak-Shop.Web با بالا رفتن از پوشهٔ خروجی آزمون (bin/Debug/net10.0).</summary>
    private static string? FindWebProject()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            var candidate = Path.Combine(directory.FullName, "src", "Fratak-Shop.Web");
            if (File.Exists(Path.Combine(candidate, "appsettings.json")))
                return candidate;
        }
        return null;
    }

    private static string? ReadConnection(string path)
    {
        try
        {
            if (!File.Exists(path))
                return null;
            using var document = JsonDocument.Parse(File.ReadAllText(path),
                new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            var root = document.RootElement;
            if (root.TryGetProperty("ConnectionDefault", out var direct) && direct.ValueKind == JsonValueKind.String)
                return direct.GetString();
            if (root.TryGetProperty("ConnectionStrings", out var strings) && strings.ValueKind == JsonValueKind.Object
                && strings.TryGetProperty("DefaultConnection", out var named) && named.ValueKind == JsonValueKind.String)
                return named.GetString();
        }
        catch (Exception exception) when (exception is IOException or JsonException or UnauthorizedAccessException)
        {
            // فایل تنظیمات خوانا نیست؛ نامزد بعدی.
        }
        return null;
    }

    /// <summary>
    /// سرور روی همین دستگاه است؟ مثال: «.»، «(local)»، «localhost,1433»، «.\SQLEXPRESS»، «(localdb)\MSSQLLocalDB»، «127.0.0.1» ← true؛
    /// «185.191.78.226,2019» یا «db.example.com» ← false.
    /// </summary>
    public static bool IsLocalServer(string connection)
    {
        string source;
        try
        {
            source = new SqlConnectionStringBuilder(connection).DataSource.Trim();
        }
        catch (ArgumentException)
        {
            return false;
        }

        if (source.StartsWith("(localdb)", StringComparison.OrdinalIgnoreCase))
            return true;
        foreach (var prefix in new[] { "tcp:", "np:", "lpc:" })
            if (source.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
                source = source[prefix.Length..];
        var host = source.Split(',', '\\')[0].Trim();
        return host is "." or "(local)" or "127.0.0.1" or "::1" or "[::1]"
               || host.Equals("localhost", StringComparison.OrdinalIgnoreCase)
               || host.Equals(Environment.MachineName, StringComparison.OrdinalIgnoreCase);
    }

    private static string WithoutDatabase(string connection)
    {
        var builder = new SqlConnectionStringBuilder(connection);
        builder.Remove("Initial Catalog");
        builder.Remove("AttachDBFilename");
        return builder.ConnectionString;
    }

    /// <summary>اتصال کوتاه به master؛ null یعنی موفق، وگرنه پیام خطا.</summary>
    private static string? Probe(string server)
    {
        try
        {
            var builder = new SqlConnectionStringBuilder(server) { InitialCatalog = "master", Pooling = false };
            // LocalDB بار اول خودش را روشن می‌کند و ممکن است چند ثانیه طول بکشد.
            builder.ConnectTimeout = builder.DataSource.Contains("(localdb)", StringComparison.OrdinalIgnoreCase) ? 30 : 8;
            using var connection = new SqlConnection(builder.ConnectionString);
            connection.Open();
            using var command = connection.CreateCommand();
            command.CommandText = "SELECT 1";
            command.ExecuteScalar();
            return null;
        }
        catch (Exception exception)
        {
            var message = exception.Message.ReplaceLineEndings(" ");
            return message.Length > 160 ? message[..160] : message;
        }
    }

    private static string Describe(string server)
    {
        var builder = new SqlConnectionStringBuilder(server);
        return string.IsNullOrEmpty(builder.UserID) ? builder.DataSource : $"{builder.DataSource} ({builder.UserID})";
    }
}

/// <summary>
/// مثل <see cref="FactAttribute"/>، ولی اگر SQL Server پیدا نشود آزمون با پیام راهنما «Skipped» می‌شود.
/// برای همهٔ آزمون‌های پوشه‌های Integration و E2E.
/// </summary>
public sealed class SqlFactAttribute : FactAttribute
{
    public SqlFactAttribute()
    {
        if (TestSql.SkipReason is { } reason && string.IsNullOrEmpty(Skip))
            Skip = reason;
    }
}

/// <summary>نسخهٔ <see cref="TheoryAttribute"/> از <see cref="SqlFactAttribute"/>.</summary>
public sealed class SqlTheoryAttribute : TheoryAttribute
{
    public SqlTheoryAttribute()
    {
        if (TestSql.SkipReason is { } reason && string.IsNullOrEmpty(Skip))
            Skip = reason;
    }
}
