using Claims.Application.Claims;
using Claims.Domain;
using Xunit;

namespace Claims.Tests.Unit;

public class ClaimValidatorTests
{
    private static readonly Cover January = new()
    {
        Id = "cover-1",
        StartDate = new DateTime(2030, 1, 1),
        EndDate = new DateTime(2030, 1, 31),
        Type = CoverType.Yacht,
        Premium = 1
    };

    private static CreateClaimRequest Claim(DateTime created, decimal damageCost = 1_000) =>
        new(January.Id, created, "Scraped hull", ClaimType.Collision, damageCost);

    [Theory]
    [InlineData(0)]
    [InlineData(100_000)]
    public void DamageCost_up_to_and_including_100_000_is_valid(decimal damageCost)
    {
        Assert.Empty(ClaimValidator.Validate(Claim(January.StartDate, damageCost), January));
    }

    [Fact]
    public void DamageCost_cannot_exceed_100_000()
    {
        var errors = ClaimValidator.Validate(Claim(January.StartDate, 100_000.01m), January);

        var error = Assert.Single(errors);
        Assert.Equal(nameof(CreateClaimRequest.DamageCost), error.Field);
    }

    [Fact]
    public void DamageCost_cannot_be_negative()
    {
        var errors = ClaimValidator.Validate(Claim(January.StartDate, -1), January);

        Assert.Equal(nameof(CreateClaimRequest.DamageCost), Assert.Single(errors).Field);
    }

    [Theory]
    [InlineData("2030-01-01T00:00:00")] // first day of the cover
    [InlineData("2030-01-15T12:00:00")]
    [InlineData("2030-01-31T23:59:59")] // last day of the cover, late in the day
    public void Created_within_the_cover_period_is_valid(string created)
    {
        Assert.Empty(ClaimValidator.Validate(Claim(DateTime.Parse(created)), January));
    }

    [Theory]
    [InlineData("2029-12-31T23:59:59")]
    [InlineData("2030-02-01T00:00:00")]
    public void Created_outside_the_cover_period_is_rejected(string created)
    {
        var errors = ClaimValidator.Validate(Claim(DateTime.Parse(created)), January);

        Assert.Equal(nameof(CreateClaimRequest.Created), Assert.Single(errors).Field);
    }

    [Fact]
    public void Claim_for_an_unknown_cover_is_rejected()
    {
        var errors = ClaimValidator.Validate(Claim(January.StartDate), cover: null);

        Assert.Equal(nameof(CreateClaimRequest.CoverId), Assert.Single(errors).Field);
    }
}
