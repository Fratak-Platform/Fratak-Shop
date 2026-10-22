using Fratak_Shop.Core.Utilities;

namespace Fratak_Shop.Web.Models;

/// <summary>
/// Data of the <c>_Pagination</c> partial. Page links keep the current query
/// string and only replace the page number.
/// </summary>
public class PagerModel
{
    public int CurrentPage { get; init; }
    public int PageCount { get; init; }
    public int StartPage { get; init; }
    public int EndPage { get; init; }
    public required Func<int, string> UrlFor { get; init; }

    public bool HasPages => PageCount > 1;

    public static PagerModel From(BasePaginate paginate, HttpRequest request, string pageKey = "page")
    {
        return new PagerModel
        {
            CurrentPage = Math.Max(1, paginate.CurrentPage),
            PageCount = paginate.PageCount,
            StartPage = Math.Max(1, paginate.StartPage),
            EndPage = Math.Min(paginate.PageCount, Math.Max(paginate.EndPage, 1)),
            UrlFor = page => BuildUrl(request, pageKey, page),
        };
    }

    private static string BuildUrl(HttpRequest request, string pageKey, int page)
    {
        var query = request.Query
            .Where(pair => !string.Equals(pair.Key, pageKey, StringComparison.OrdinalIgnoreCase))
            .SelectMany(pair => pair.Value.Select(value => new KeyValuePair<string, string?>(pair.Key, value)))
            .ToList();

        if (page > 1)
            query.Add(new KeyValuePair<string, string?>(pageKey, page.ToString()));

        return request.PathBase + request.Path + Microsoft.AspNetCore.Http.QueryString.Create(query);
    }
}
