using Claims.Application.Abstractions;
using Claims.Application.Covers;
using Claims.Domain;
using Claims.Tests.Fakes;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Claims.Tests.Unit;

public class CoverHandlerTests
{
    private static readonly DateTime Today = new(2030, 1, 1);

    private readonly InMemoryRepository<Cover> _covers = new();
    private readonly RecordingAuditer _auditer = new();
    private readonly CreateCoverCommandHandler _create;
    private readonly DeleteCoverCommandHandler _delete;
    private readonly ComputePremiumQueryHandler _compute;

    public CoverHandlerTests()
    {
        var validator = new CreateCoverCommandValidator(new FixedTimeProvider(new DateTimeOffset(Today, TimeSpan.Zero)));
        _create = new CreateCoverCommandHandler(_covers, validator, _auditer, NullLogger<CreateCoverCommandHandler>.Instance);
        _delete = new DeleteCoverCommandHandler(_covers, _auditer, NullLogger<DeleteCoverCommandHandler>.Instance);
        _compute = new ComputePremiumQueryHandler(validator);
    }

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    [Fact]
    public async Task Create_calculates_the_premium_on_the_server_and_audits_it()
    {
        var request = new CreateCoverCommand(Today, Today.AddDays(30), CoverType.Yacht);

        var cover = await _create.HandleAsync(request, Token);

        Assert.Equal(41_250, cover.Premium);
        Assert.Same(cover, Assert.Single(_covers.Stored));
        Assert.Equal(("Cover", cover.Id, AuditAction.Created), Assert.Single(_auditer.Entries));
    }

    [Fact]
    public async Task Create_uses_the_injected_clock_for_the_start_date_rule()
    {
        var request = new CreateCoverCommand(Today.AddDays(-1), Today.AddDays(30), CoverType.Yacht);

        var exception = await Assert.ThrowsAsync<ValidationException>(() => _create.HandleAsync(request, Token));

        Assert.Equal(nameof(CreateCoverCommand.StartDate), Assert.Single(exception.Errors).PropertyName);
        Assert.Empty(_covers.Stored);
        Assert.Empty(_auditer.Entries);
    }

    [Fact]
    public async Task ComputePremium_applies_the_same_validation_as_create()
    {
        var request = new CreateCoverCommand(Today, Today.AddYears(2), CoverType.Tanker);

        await Assert.ThrowsAsync<ValidationException>(() => _compute.HandleAsync(new ComputePremiumQuery(request.StartDate, request.EndDate, request.Type), Token));
    }

    [Fact]
    public async Task ComputePremium_quotes_without_storing_or_auditing()
    {
        var premium = await _compute.HandleAsync(new ComputePremiumQuery(Today, Today.AddDays(30), CoverType.Yacht), Token);

        Assert.Equal(41_250, premium);
        Assert.Empty(_covers.Stored);
        Assert.Empty(_auditer.Entries);
    }

    [Fact]
    public async Task Delete_of_an_unknown_cover_returns_false()
    {
        Assert.False(await _delete.HandleAsync(new DeleteCoverCommand("does-not-exist"), Token));
    }

    [Fact]
    public async Task GetCoverById_returns_the_stored_cover_or_null()
    {
        var cover = await _create.HandleAsync(new CreateCoverCommand(Today, Today.AddDays(10), CoverType.Tanker), Token);
        var getById = new GetCoverByIdQueryHandler(_covers, NullLogger<GetCoverByIdQueryHandler>.Instance);

        Assert.Same(cover, await getById.HandleAsync(new GetCoverByIdQuery(cover.Id), Token));
        Assert.Null(await getById.HandleAsync(new GetCoverByIdQuery("does-not-exist"), Token));
    }

    [Fact]
    public async Task Delete_removes_the_cover_and_audits_it()
    {
        var cover = await _create.HandleAsync(new CreateCoverCommand(Today, Today.AddDays(10), CoverType.Tanker), Token);
        _auditer.Entries.Clear();

        Assert.True(await _delete.HandleAsync(new DeleteCoverCommand(cover.Id), Token));
        Assert.Empty(_covers.Stored);
        Assert.Equal(("Cover", cover.Id, AuditAction.Deleted), Assert.Single(_auditer.Entries));
    }
}
