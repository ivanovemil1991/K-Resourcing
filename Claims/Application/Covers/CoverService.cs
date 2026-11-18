using Claims.Application.Abstractions;
using Claims.Domain;
using FluentValidation;

namespace Claims.Application.Covers;

/// <inheritdoc />
public sealed class CoverService(
    IRepository<Cover> covers,
    IValidator<CreateCoverRequest> validator,
    IAuditer auditer,
    ILogger<CoverService> logger) : ICoverService
{
    /// <inheritdoc />
    public Task<IReadOnlyList<Cover>> GetAllAsync(CancellationToken cancellationToken) =>
        covers.GetAllAsync(cancellationToken);

    /// <inheritdoc />
    public async Task<Cover?> GetByIdAsync(string id, CancellationToken cancellationToken)
    {
        var cover = await covers.GetByIdAsync(id, cancellationToken);
        if (cover is null)
            logger.LogInformation("Cover not found");
        return cover;
    }

    /// <inheritdoc />
    public decimal ComputePremium(CreateCoverRequest request)
    {
        validator.ValidateAndThrow(request);
        return PremiumCalculator.Calculate(request.StartDate, request.EndDate, request.Type);
    }

    /// <inheritdoc />
    public async Task<Cover> CreateAsync(CreateCoverRequest request, CancellationToken cancellationToken)
    {
        var cover = new Cover
        {
            Id = Guid.NewGuid().ToString(),
            StartDate = request.StartDate,
            EndDate = request.EndDate,
            Type = request.Type,
            Premium = ComputePremium(request)
        };

        auditer.AuditCover(cover.Id, AuditAction.Created);
        await covers.AddAsync(cover, cancellationToken);
        logger.LogInformation("Cover created {CoverId}", cover.Id);
        return cover;
    }

    /// <inheritdoc />
    public async Task<bool> DeleteAsync(string id, CancellationToken cancellationToken)
    {
        auditer.AuditCover(id, AuditAction.Deleted);
        var deleted = await covers.DeleteAsync(id, cancellationToken);
        if (deleted && Guid.TryParse(id, out var coverId))
            logger.LogInformation("Cover deleted {CoverId}", coverId);
        else if (deleted)
            logger.LogInformation("Cover deleted");
        else
            logger.LogInformation("Cover not found for deletion");
        return deleted;
    }
}
