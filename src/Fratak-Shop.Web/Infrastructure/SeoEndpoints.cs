using System.Globalization;
using System.Text;
using System.Xml;
using Fratak_Shop.Core.Services.Seo;
using Fratak_Shop.Core.Services.Settings;

namespace Fratak_Shop.Web.Infrastructure;

/// <summary>/sitemap.xml and /robots.txt generated from the database.</summary>
public static class SeoEndpoints
{
    public static IEndpointRouteBuilder MapSeoEndpoints(this IEndpointRouteBuilder app)
    {
        app.MapGet("/sitemap.xml", async (HttpContext http, ISitemapService sitemap, CancellationToken ct) =>
        {
            var entries = await sitemap.GetEntriesAsync(ct);
            var baseUrl = BaseUrl(http.Request);

            var settings = new XmlWriterSettings { Async = true, Encoding = new UTF8Encoding(false), Indent = false };
            using var buffer = new MemoryStream();
            await using (var writer = XmlWriter.Create(buffer, settings))
            {
                await writer.WriteStartDocumentAsync();
                writer.WriteStartElement("urlset", "http://www.sitemaps.org/schemas/sitemap/0.9");
                foreach (var entry in entries)
                {
                    writer.WriteStartElement("url");
                    writer.WriteElementString("loc", baseUrl + EncodePath(entry.Path));
                    if (entry.LastModified is { } modified && modified > DateTimeOffset.MinValue)
                        writer.WriteElementString("lastmod", modified.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
                    writer.WriteElementString("changefreq", entry.ChangeFrequency);
                    writer.WriteElementString("priority", entry.Priority.ToString("0.0", CultureInfo.InvariantCulture));
                    writer.WriteEndElement();
                }
                writer.WriteEndElement();
                await writer.WriteEndDocumentAsync();
            }

            http.Response.Headers.CacheControl = "public, max-age=1800";
            return Results.Bytes(buffer.ToArray(), "application/xml; charset=utf-8");
        });

        app.MapGet("/robots.txt", async (HttpContext http, IStoreSettingService settingService, CancellationToken ct) =>
        {
            var settings = await settingService.GetAsync(ct);
            var builder = new StringBuilder().AppendLine("User-agent: *");
            foreach (var prefix in Fratak_Shop.Web.Services.PrivatePaths.Prefixes)
                builder.Append("Disallow: ").AppendLine(prefix);
            builder.AppendLine("Allow: /");

            if (!string.IsNullOrWhiteSpace(settings.RobotsTxtExtra))
            {
                builder.AppendLine();
                // Plain text only; strip anything that could break the file format.
                foreach (var line in settings.RobotsTxtExtra.Split('\n'))
                    builder.AppendLine(line.Replace("\r", string.Empty).Trim());
            }

            builder.AppendLine().Append("Sitemap: ").Append(BaseUrl(http.Request)).AppendLine("/sitemap.xml");

            http.Response.Headers.CacheControl = "public, max-age=3600";
            return Results.Text(builder.ToString(), "text/plain; charset=utf-8");
        });

        return app;
    }

    private static string BaseUrl(HttpRequest request) => $"{request.Scheme}://{request.Host}{request.PathBase}";

    private static string EncodePath(string path) =>
        string.Join('/', path.Split('/').Select(Uri.EscapeDataString));
}
