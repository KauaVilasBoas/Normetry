using FluentValidation;
using Normetry.Domain.Inspections;

namespace Normetry.Application.Inspections.ReviewFinding;

internal sealed class ReviewFindingValidator : AbstractValidator<ReviewFindingCommand>
{
    public ReviewFindingValidator()
    {
        When(
            command => command.Decision == ReviewDecision.Reject,
            () =>
                RuleFor(command => command.Note)
                    .NotEmpty()
                    .MaximumLength(Finding.ReviewNoteMaxLength)
        );
        When(
            command => command.Decision == ReviewDecision.Edit,
            () =>
            {
                RuleFor(command => command)
                    .Must(command => !command.ToEdit().IsEmpty)
                    .WithMessage(
                        "Change at least one of verdict, severity, rationale, actionText or dueOn."
                    )
                    .OverridePropertyName("Edit");
                RuleFor(command => command.Verdict)
                    .Must(verdict => Verdicts.All.Contains(verdict!))
                    .WithMessage(
                        $"Use {Verdicts.Compliant}, {Verdicts.NonCompliant} or {Verdicts.InsufficientEvidence}."
                    )
                    .When(command => command.Verdict is not null);
                RuleFor(command => command.Severity)
                    .Must(severity => Severities.All.Contains(severity!))
                    .WithMessage(
                        $"Use {Severities.Critical}, {Severities.Major} or {Severities.Minor}."
                    )
                    .When(command => command.Severity is not null);
                RuleFor(command => command.Rationale)
                    .NotEmpty()
                    .MaximumLength(Finding.EditedRationaleMaxLength)
                    .When(command => command.Rationale is not null);
                RuleFor(command => command.ActionText)
                    .NotEmpty()
                    .MaximumLength(ActionItem.EditedTextMaxLength)
                    .When(command => command.ActionText is not null);
            }
        );
    }
}
