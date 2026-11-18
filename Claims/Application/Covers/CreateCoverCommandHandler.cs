using Claims.Application.Abstractions;
using Claims.Domain;
using FluentValidation;

namespace Claims.Application.Covers;

/// <summary>Command: validates the cover, calculates its premium and stores it.</summary>
/// <exception cref="ValidationException">The command breaks a business rule.</exception>
public sealed class CreateCoverCommandHandler(
    IRepository<Cover> covers,
    IValidator<CreateCoverCommand> validator,
    IAuditer auditer,
    ILogger<CreateCoverCommandHandler> logger) : ICommandHandler<CreateCoverCommand, Cover>
{
    /// <inheritdoc />
    public async Task<Cover> HandleAsync(CreateCoverCommand command, CancellationToken cancellationToken)
    {
        await validator.ValidateAndThrowAsync(command, cancellationToken);

        var cover = new Cover
        {
            Id = Guid.NewGuid().ToString(),
            StartDate = command.StartDate,
            EndDate = command.EndDate,
            Type = command.Type,
            Premium = PremiumCalculator.Calculate(command.StartDate, command.EndDate, command.Type)
        };

        auditer.AuditCover(cover.Id, AuditAction.Created);
        await covers.AddAsync(cover, cancellationToken);
        logger.LogInformation("Cover created {CoverId}", cover.Id);
        return cover;
    }
}
