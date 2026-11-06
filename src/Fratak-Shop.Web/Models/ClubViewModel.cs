using Fratak_Shop.Core.DTOs.Loyalty;

namespace Fratak_Shop.Web.Models;

/// <summary>Data of the club partials (public /club page and /profile/club).</summary>
public record ClubViewModel(LoyaltyProgramDto Program, LoyaltySummaryDto? Summary);
