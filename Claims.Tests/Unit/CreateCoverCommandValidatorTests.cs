using Claims.Application.Covers;
using Claims.Domain;
using Claims.Tests.Fakes;
using Xunit;

namespace Claims.Tests.Unit;

public class CreateCoverCommandValidatorTests
{
    private static readonly DateTime TodayAtMidnight = new(2030, 6, 15);

    private readonly CreateCoverCommandValidator _validator =
        new(new FixedTimeProvider(new DateTimeOffset(TodayAtMidnight, TimeSpan.Zero)));

    private static CreateCoverCommand Cover(DateTime start, DateTime end) => new(start, end, CoverType.Yacht);

    private string[] InvalidFields(CreateCoverCommand request) =>
        _validator.Validate(request).Errors.Select(error => error.PropertyName).ToArray();

    [Fact]
    public void A_cover_starting_today_and_lasting_exactly_one_year_is_valid()
    {
        Assert.Empty(InvalidFields(Cover(TodayAtMidnight, TodayAtMidnight.AddYears(1))));
    }

    [Fact]
    public void A_start_date_later_today_is_not_in_the_past()
    {
        Assert.Empty(InvalidFields(Cover(TodayAtMidnight.AddHours(23), TodayAtMidnight.AddDays(10))));
    }

    [Fact]
    public void StartDate_cannot_be_in_the_past()
    {
        var fields = InvalidFields(Cover(TodayAtMidnight.AddDays(-1), TodayAtMidnight.AddDays(10)));

        Assert.Equal(nameof(CreateCoverCommand.StartDate), Assert.Single(fields));
    }

    [Fact]
    public void Insurance_period_cannot_exceed_one_year()
    {
        var fields = InvalidFields(Cover(TodayAtMidnight, TodayAtMidnight.AddYears(1).AddDays(1)));

        Assert.Equal(nameof(CreateCoverCommand.EndDate), Assert.Single(fields));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void EndDate_must_be_after_StartDate(int lengthInDays)
    {
        var fields = InvalidFields(Cover(TodayAtMidnight, TodayAtMidnight.AddDays(lengthInDays)));

        Assert.Equal(nameof(CreateCoverCommand.EndDate), Assert.Single(fields));
    }

    [Fact]
    public void All_broken_rules_are_reported_together()
    {
        var fields = InvalidFields(Cover(TodayAtMidnight.AddDays(-1), TodayAtMidnight.AddYears(2)));

        Assert.Equal(2, fields.Length);
    }

    [Fact]
    public void An_undefined_cover_type_is_rejected()
    {
        var fields = InvalidFields(Cover(TodayAtMidnight, TodayAtMidnight.AddDays(10)) with { Type = (CoverType)99 });

        Assert.Equal(nameof(CreateCoverCommand.Type), Assert.Single(fields));
    }
}
