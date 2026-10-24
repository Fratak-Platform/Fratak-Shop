using Fratak_Shop.Core.DTOs.Marketing;

namespace Fratak_Shop.Web.Models;

/// <summary>Discount code box of the cart / checkout summary.</summary>
/// <param name="Code">Code stored on the cart.</param>
/// <param name="Evaluation">Result of the code for the current cart (null = invalid or none).</param>
/// <param name="Error">Why the stored code does not apply (any more).</param>
/// <param name="ReturnUrl">Page to come back to after applying / removing.</param>
public record CouponBoxModel(string? Code, CouponEvaluation? Evaluation, string? Error, string ReturnUrl);
