using Claims.Domain;

namespace Claims.Application.Covers;

/// <summary>Calculates cover premiums.</summary>
/// <remarks>
/// Premiums use a vessel-specific day rate and tiered discounts:
/// <list type="table">
///   <listheader><term>Days</term><description>Discount (Yacht / other types)</description></listheader>
///   <item><term>1 – 30</term><description>none</description></item>
///   <item><term>31 – 180</term><description>5% / 2%</description></item>
///   <item><term>181 and later</term><description>8% / 3%</description></item>
/// </list>
/// </remarks>
public static class PremiumCalculator
{
    /// <summary>The price of one day of cover, before the vessel type surcharge.</summary>
    public const decimal BaseDayRate = 1250m;

    /// <summary>A block of consecutive days that share one discount.</summary>
    private sealed record DiscountTier(int Days, decimal Discount);

    private static readonly DiscountTier[] YachtTiers =
    [
        new(Days: 30, Discount: 0.00m),
        new(Days: 150, Discount: 0.05m),
        new(Days: int.MaxValue, Discount: 0.08m)
    ];

    private static readonly DiscountTier[] OtherTiers =
    [
        new(Days: 30, Discount: 0.00m),
        new(Days: 150, Discount: 0.02m),
        new(Days: int.MaxValue, Discount: 0.03m)
    ];

    /// <summary>Calculates the total premium for the insurance period.</summary>
    /// <param name="startDate">First day of the period.</param>
    /// <param name="endDate">End of the period. The number of days is <c>endDate − startDate</c>; time of day is ignored.</param>
    /// <param name="coverType">The kind of vessel to insure.</param>
    /// <returns>The total premium, or 0 if the period is empty.</returns>
    public static decimal Calculate(DateTime startDate, DateTime endDate, CoverType coverType)
    {
        var dayRate = BaseDayRate * SurchargeMultiplier(coverType);
        var daysLeft = Math.Max(0, (endDate.Date - startDate.Date).Days);
        var premium = 0m;

        foreach (var tier in coverType == CoverType.Yacht ? YachtTiers : OtherTiers)
        {
            var daysInTier = Math.Min(daysLeft, tier.Days);
            premium += daysInTier * dayRate * (1 - tier.Discount);
            daysLeft -= daysInTier;
        }

        return premium;
    }

    /// <summary>Returns the vessel type's multiplier for the base day rate.</summary>
    private static decimal SurchargeMultiplier(CoverType coverType) => coverType switch
    {
        CoverType.Yacht => 1.1m,
        CoverType.PassengerShip => 1.2m,
        CoverType.Tanker => 1.5m,
        _ => 1.3m
    };
}
