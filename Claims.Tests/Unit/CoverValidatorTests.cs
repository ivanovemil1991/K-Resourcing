using Claims.Application.Covers;
using Claims.Domain;
using Xunit;

namespace Claims.Tests.Unit;

public class CoverValidatorTests
{
    private static readonly DateOnly Today = new(2030, 6, 15);
    private static readonly DateTime TodayAtMidnight = Today.ToDateTime(TimeOnly.MinValue);

    private static CreateCoverRequest Cover(DateTime start, DateTime end) => new(start, end, CoverType.Yacht);

    [Fact]
    public void A_cover_starting_today_and_lasting_exactly_one_year_is_valid()
    {
        var errors = CoverValidator.Validate(Cover(TodayAtMidnight, TodayAtMidnight.AddYears(1)), Today);

        Assert.Empty(errors);
    }

    [Fact]
    public void A_start_date_later_today_is_not_in_the_past()
    {
        var errors = CoverValidator.Validate(Cover(TodayAtMidnight.AddHours(23), TodayAtMidnight.AddDays(10)), Today);

        Assert.Empty(errors);
    }

    [Fact]
    public void StartDate_cannot_be_in_the_past()
    {
        var errors = CoverValidator.Validate(Cover(TodayAtMidnight.AddDays(-1), TodayAtMidnight.AddDays(10)), Today);

        var error = Assert.Single(errors);
        Assert.Equal(nameof(CreateCoverRequest.StartDate), error.Field);
    }

    [Fact]
    public void Insurance_period_cannot_exceed_one_year()
    {
        var errors = CoverValidator.Validate(Cover(TodayAtMidnight, TodayAtMidnight.AddYears(1).AddDays(1)), Today);

        var error = Assert.Single(errors);
        Assert.Equal(nameof(CreateCoverRequest.EndDate), error.Field);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void EndDate_must_be_after_StartDate(int lengthInDays)
    {
        var errors = CoverValidator.Validate(Cover(TodayAtMidnight, TodayAtMidnight.AddDays(lengthInDays)), Today);

        var error = Assert.Single(errors);
        Assert.Equal(nameof(CreateCoverRequest.EndDate), error.Field);
    }

    [Fact]
    public void All_broken_rules_are_reported_together()
    {
        var errors = CoverValidator.Validate(Cover(TodayAtMidnight.AddDays(-1), TodayAtMidnight.AddYears(2)), Today);

        Assert.Equal(2, errors.Count);
    }
}
