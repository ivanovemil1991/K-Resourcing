using FluentValidation;

namespace Claims.Application.Covers;

/// <summary>Validates a <see cref="CreateCoverRequest"/>. "Today" comes from the injected clock.</summary>
public sealed class CreateCoverRequestValidator : AbstractValidator<CreateCoverRequest>
{
    public CreateCoverRequestValidator(TimeProvider timeProvider)
    {
        RuleFor(cover => cover.Type).IsInEnum();

        RuleFor(cover => cover.StartDate)
            .Must(start => DateOnly.FromDateTime(start) >= DateOnly.FromDateTime(timeProvider.GetUtcNow().UtcDateTime))
            .WithMessage("StartDate cannot be in the past.");

        RuleFor(cover => cover.EndDate)
            .Cascade(CascadeMode.Stop)
            .Must((cover, end) => DateOnly.FromDateTime(end) > DateOnly.FromDateTime(cover.StartDate))
            .WithMessage("EndDate must be after StartDate.")
            .Must((cover, end) => DateOnly.FromDateTime(end) <= DateOnly.FromDateTime(cover.StartDate).AddYears(1))
            .WithMessage("The insurance period cannot exceed 1 year.");
    }
}
