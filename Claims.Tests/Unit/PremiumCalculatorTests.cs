using Claims.Application.Covers;
using Claims.Domain;
using Xunit;

namespace Claims.Tests.Unit;

public class PremiumCalculatorTests
{
    private static readonly DateTime Start = new(2030, 1, 1);

    [Theory]
    [InlineData(CoverType.Yacht, 1375)]          // 1250 × 1.1
    [InlineData(CoverType.PassengerShip, 1500)]  // 1250 × 1.2
    [InlineData(CoverType.Tanker, 1875)]         // 1250 × 1.5
    [InlineData(CoverType.ContainerShip, 1625)]  // 1250 × 1.3
    [InlineData(CoverType.BulkCarrier, 1625)]    // 1250 × 1.3
    public void A_single_day_costs_the_base_rate_plus_the_type_surcharge(CoverType type, decimal expected)
    {
        Assert.Equal(expected, PremiumCalculator.Calculate(Start, Start.AddDays(1), type));
    }

    [Theory]
    [InlineData(CoverType.Yacht, 30, 41_250)]                // 30 × 1375
    [InlineData(CoverType.Yacht, 31, 42_556.25)]             // + 1 × 1375 × 0.95
    [InlineData(CoverType.Yacht, 180, 237_187.5)]            // 41 250 + 150 × 1375 × 0.95
    [InlineData(CoverType.Yacht, 181, 238_452.5)]            // + 1 × 1375 × 0.92
    [InlineData(CoverType.Yacht, 365, 471_212.5)]            // 237 187.5 + 185 × 1375 × 0.92
    [InlineData(CoverType.Tanker, 30, 56_250)]               // 30 × 1875
    [InlineData(CoverType.Tanker, 31, 58_087.5)]             // + 1 × 1875 × 0.98
    [InlineData(CoverType.Tanker, 180, 331_875)]             // 56 250 + 150 × 1875 × 0.98
    [InlineData(CoverType.Tanker, 181, 333_693.75)]          // + 1 × 1875 × 0.97
    [InlineData(CoverType.Tanker, 365, 668_343.75)]          // 331 875 + 185 × 1875 × 0.97
    public void Discounts_are_applied_progressively_per_tier(CoverType type, int days, decimal expected)
    {
        Assert.Equal(expected, PremiumCalculator.Calculate(Start, Start.AddDays(days), type));
    }

    [Fact]
    public void Time_of_day_is_ignored_when_counting_days()
    {
        var premium = PremiumCalculator.Calculate(Start.AddHours(18), Start.AddDays(1).AddHours(6), CoverType.Yacht);

        Assert.Equal(1375, premium);
    }

    [Fact]
    public void An_empty_or_reversed_period_costs_nothing()
    {
        Assert.Equal(0, PremiumCalculator.Calculate(Start, Start, CoverType.Tanker));
        Assert.Equal(0, PremiumCalculator.Calculate(Start, Start.AddDays(-5), CoverType.Tanker));
    }
}
