using Claims.Application.Abstractions;
using Claims.Application.Covers;
using Claims.Domain;
using Claims.Tests.Fakes;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Claims.Tests.Unit;

public class CoverServiceTests
{
    private static readonly DateTime Today = new(2030, 1, 1);

    private readonly InMemoryRepository<Cover> _covers = new();
    private readonly RecordingAuditer _auditer = new();
    private readonly CoverService _service;

    public CoverServiceTests() =>
        _service = new CoverService(_covers, new CreateCoverRequestValidator(new FixedTimeProvider(new DateTimeOffset(Today, TimeSpan.Zero))), _auditer, NullLogger<CoverService>.Instance);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_calculates_the_premium_on_the_server_and_audits_it()
    {
        var request = new CreateCoverRequest(Today, Today.AddDays(30), CoverType.Yacht);

        var cover = await _service.CreateAsync(request, Token);

        Assert.Equal(41_250, cover.Premium);
        Assert.Same(cover, Assert.Single(_covers.Stored));
        Assert.Equal(("Cover", cover.Id, AuditAction.Created), Assert.Single(_auditer.Entries));
    }

    [Fact]
    public async Task Create_uses_the_injected_clock_for_the_start_date_rule()
    {
        var request = new CreateCoverRequest(Today.AddDays(-1), Today.AddDays(30), CoverType.Yacht);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => _service.CreateAsync(request, Token));

        Assert.Equal(nameof(CreateCoverRequest.StartDate), Assert.Single(exception.Errors).PropertyName);
        Assert.Empty(_covers.Stored);
        Assert.Empty(_auditer.Entries);
    }

    [Fact]
    public void ComputePremium_applies_the_same_validation_as_create()
    {
        var request = new CreateCoverRequest(Today, Today.AddYears(2), CoverType.Tanker);

        Assert.Throws<ValidationException>(() => _service.ComputePremium(request));
    }

    [Fact]
    public async Task Delete_removes_the_cover_and_audits_it()
    {
        var cover = await _service.CreateAsync(new CreateCoverRequest(Today, Today.AddDays(10), CoverType.Tanker), Token);
        _auditer.Entries.Clear();

        Assert.True(await _service.DeleteAsync(cover.Id, Token));
        Assert.Empty(_covers.Stored);
        Assert.Equal(("Cover", cover.Id, AuditAction.Deleted), Assert.Single(_auditer.Entries));
    }
}
