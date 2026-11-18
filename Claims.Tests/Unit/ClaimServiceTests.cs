using Claims.Application.Abstractions;
using Claims.Application.Claims;
using Claims.Domain;
using Claims.Tests.Fakes;
using FluentValidation;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace Claims.Tests.Unit;

public class ClaimServiceTests
{
    private static readonly Cover ExistingCover = new()
    {
        Id = "cover-1",
        StartDate = new DateTime(2030, 1, 1),
        EndDate = new DateTime(2030, 12, 31),
        Type = CoverType.Tanker,
        Premium = 1
    };

    private readonly InMemoryRepository<Claim> _claims = new();
    private readonly RecordingAuditer _auditer = new();
    private readonly ClaimService _service;

    public ClaimServiceTests() =>
        _service = new ClaimService(_claims, new CreateClaimRequestValidator(new InMemoryRepository<Cover>(ExistingCover)), _auditer, NullLogger<ClaimService>.Instance);

    private static CancellationToken Token => TestContext.Current.CancellationToken;

    private static CreateClaimRequest ValidRequest() =>
        new(ExistingCover.Id, new DateTime(2030, 3, 1), "Engine fire", ClaimType.Fire, 50_000);

    [Fact]
    public async Task Create_stores_the_claim_with_a_new_id_and_audits_it()
    {
        var claim = await _service.CreateAsync(ValidRequest(), Token);

        Assert.False(string.IsNullOrWhiteSpace(claim.Id));
        Assert.Equal("Engine fire", claim.Name);
        Assert.Same(claim, Assert.Single(_claims.Stored));
        Assert.Equal(("Claim", claim.Id, AuditAction.Created), Assert.Single(_auditer.Entries));
    }

    [Fact]
    public async Task Create_rejects_an_invalid_claim_and_stores_nothing()
    {
        var request = ValidRequest() with { DamageCost = 150_000 };

        var exception = await Assert.ThrowsAsync<ValidationException>(() => _service.CreateAsync(request, Token));

        Assert.Equal(nameof(CreateClaimRequest.DamageCost), Assert.Single(exception.Errors).PropertyName);
        Assert.Empty(_claims.Stored);
        Assert.Empty(_auditer.Entries);
    }

    [Fact]
    public async Task Create_rejects_a_claim_for_an_unknown_cover()
    {
        var request = ValidRequest() with { CoverId = "does-not-exist" };

        var exception = await Assert.ThrowsAsync<ValidationException>(() => _service.CreateAsync(request, Token));

        Assert.Equal(nameof(CreateClaimRequest.CoverId), Assert.Single(exception.Errors).PropertyName);
    }

    [Fact]
    public async Task Delete_removes_the_claim_and_audits_it()
    {
        var claim = await _service.CreateAsync(ValidRequest(), Token);
        _auditer.Entries.Clear();

        var deleted = await _service.DeleteAsync(claim.Id, Token);

        Assert.True(deleted);
        Assert.Empty(_claims.Stored);
        Assert.Equal(("Claim", claim.Id, AuditAction.Deleted), Assert.Single(_auditer.Entries));
    }

    [Fact]
    public async Task Delete_of_an_unknown_claim_returns_false()
    {
        Assert.False(await _service.DeleteAsync("does-not-exist", Token));
    }

    [Fact]
    public async Task GetById_returns_null_for_an_unknown_claim()
    {
        Assert.Null(await _service.GetByIdAsync("does-not-exist", Token));
    }
}
