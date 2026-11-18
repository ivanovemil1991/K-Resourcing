using Claims.Application.Claims;
using Claims.Domain;
using Claims.Tests.Fakes;
using Xunit;

namespace Claims.Tests.Unit;

public class CreateClaimCommandValidatorTests
{
    private static readonly Cover January = new()
    {
        Id = TestData.Id(1),
        StartDate = new DateTime(2030, 1, 1),
        EndDate = new DateTime(2030, 1, 31),
        Type = CoverType.Yacht,
        Premium = 1
    };

    private readonly CreateClaimCommandValidator _validator = new(new InMemoryRepository<Cover>(January));

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CreateClaimCommand Claim(DateTime created, decimal damageCost = 1_000) =>
        new(January.Id, created, "Scraped hull", ClaimType.Collision, damageCost);

    private async Task<string[]> InvalidFields(CreateClaimCommand request)
    {
        var result = await _validator.ValidateAsync(request, Token);
        return result.Errors.Select(error => error.PropertyName).ToArray();
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100_000)]
    public async Task DamageCost_up_to_and_including_100_000_is_valid(decimal damageCost)
    {
        Assert.Empty(await InvalidFields(Claim(January.StartDate, damageCost)));
    }

    [Theory]
    [InlineData(100_000.01)]
    [InlineData(-1)]
    public async Task DamageCost_must_be_between_0_and_100_000(decimal damageCost)
    {
        var fields = await InvalidFields(Claim(January.StartDate, damageCost));

        Assert.Equal(nameof(CreateClaimCommand.DamageCost), Assert.Single(fields));
    }

    [Theory]
    [InlineData("2030-01-01T00:00:00")] // first day of the cover
    [InlineData("2030-01-15T12:00:00")]
    [InlineData("2030-01-31T23:59:59")] // last day of the cover, late in the day
    public async Task Created_within_the_cover_period_is_valid(string created)
    {
        Assert.Empty(await InvalidFields(Claim(DateTime.Parse(created))));
    }

    [Theory]
    [InlineData("2029-12-31T23:59:59")]
    [InlineData("2030-02-01T00:00:00")]
    public async Task Created_outside_the_cover_period_is_rejected(string created)
    {
        var fields = await InvalidFields(Claim(DateTime.Parse(created)));

        Assert.Equal(nameof(CreateClaimCommand.Created), Assert.Single(fields));
    }

    [Fact]
    public async Task Claim_for_an_unknown_cover_is_rejected()
    {
        var fields = await InvalidFields(Claim(January.StartDate) with { CoverId = Guid.NewGuid() });

        Assert.Equal(nameof(CreateClaimCommand.CoverId), Assert.Single(fields));
    }

    [Fact]
    public async Task A_name_over_the_length_limit_is_rejected()
    {
        var fields = await InvalidFields(Claim(January.StartDate) with { Name = new string('x', CreateClaimCommandValidator.MaxNameLength + 1) });

        Assert.Equal([nameof(CreateClaimCommand.Name)], fields);
    }

    [Fact]
    public async Task Name_and_cover_id_are_required()
    {
        var fields = await InvalidFields(Claim(January.StartDate) with { Name = "", CoverId = Guid.Empty });

        Assert.Equal([nameof(CreateClaimCommand.CoverId), nameof(CreateClaimCommand.Name)], fields.Order());
    }

    [Fact]
    public async Task An_undefined_claim_type_is_rejected()
    {
        var fields = await InvalidFields(Claim(January.StartDate) with { Type = (ClaimType)99 });

        Assert.Equal(nameof(CreateClaimCommand.Type), Assert.Single(fields));
    }
}
