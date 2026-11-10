using System.Linq.Expressions;
using Fratak_Shop.Data.Entities.Products;

namespace Fratak_Shop.Core.Utilities;

/// <summary>
/// The one definition of "a product matches a search word" (listing, suggestions, did-you-mean),
/// with synonym support: every term group must match, a group matches when any of its
/// alternatives (the word itself or one of its synonyms) matches.
/// </summary>
public static class ProductSearch
{
    public static Expression<Func<Product, bool>> Matches(string term) =>
        x => x.Name.Contains(term)
             || (x.EnglishName != null && x.EnglishName.Contains(term))
             || x.Slug.Contains(term)
             || x.SKU.Contains(term)
             || (x.Brand != null && (x.Brand.Name.Contains(term) || (x.Brand.EnglishName != null && x.Brand.EnglishName.Contains(term))))
             || x.Category.Name.Contains(term);

    /// <summary>Matches when any alternative of the group matches (OR, translated to SQL).</summary>
    public static Expression<Func<Product, bool>> MatchesAny(IReadOnlyCollection<string> alternatives)
    {
        Expression<Func<Product, bool>>? result = null;
        foreach (var alternative in WithHalfSpace(alternatives.Where(a => !string.IsNullOrWhiteSpace(a)).Distinct(StringComparer.OrdinalIgnoreCase).Take(6)).Take(8))
            result = result is null ? Matches(alternative) : Or(result, Matches(alternative));

        return result ?? (_ => true);
    }

    /// <summary>
    /// دور ۲۳: عبارت چندواژه‌ای هم با فاصله و هم با نیم‌فاصله جست‌وجو می‌شود، چون نام کالاها نیم‌فاصله دارند ولی متن جست‌وجو یکسان‌سازی شده است.
    /// مثال: «لپ تاپ» ← «لپ تاپ» و «لپ‌تاپ»؛ «ماوس» ← فقط «ماوس».
    /// </summary>
    public static IEnumerable<string> WithHalfSpace(IEnumerable<string> alternatives)
    {
        foreach (var alternative in alternatives)
        {
            yield return alternative;
            if (alternative.Contains(' ', StringComparison.Ordinal))
                yield return alternative.Replace(' ', '\u200C');
        }
    }

    public static IQueryable<Product> Apply(IQueryable<Product> query, IReadOnlyList<IReadOnlyList<string>> termGroups)
    {
        foreach (var group in termGroups)
            query = query.Where(group.Count == 1 ? Matches(group[0]) : MatchesAny(group.ToList()));
        return query;
    }

    /// <summary>Term groups without synonyms (each word alone).</summary>
    public static IReadOnlyList<IReadOnlyList<string>> PlainGroups(string? search) =>
        SearchText.Terms(search).Select(term => (IReadOnlyList<string>)[term]).ToList();

    private static Expression<Func<T, bool>> Or<T>(Expression<Func<T, bool>> left, Expression<Func<T, bool>> right)
    {
        var parameter = left.Parameters[0];
        var body = new ReplaceParameter(right.Parameters[0], parameter).Visit(right.Body)!;
        return Expression.Lambda<Func<T, bool>>(Expression.OrElse(left.Body, body), parameter);
    }

    private sealed class ReplaceParameter(ParameterExpression from, ParameterExpression to) : ExpressionVisitor
    {
        protected override Expression VisitParameter(ParameterExpression node) => node == from ? to : base.VisitParameter(node);
    }
}
