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
                    .Must(command =>
                        command
                            is not {
                                Verdict: null,
                                Severity: null,
                                Rationale: null,
                                ActionText: null,
                                DueOn: null
                            }
                    )
                    .WithMessage(
                        "Change at least one of verdict, severity, rationale, actionText or dueOn."
                    )
                    .OverridePropertyName("Edit");
                RuleFor(command => command.Verdict)
                    .Must(verdict => Finding.Verdicts.Contains(verdict!))
                    .WithMessage("Use compliant, non_compliant or insufficient_evidence.")
                    .When(command => command.Verdict is not null);
                RuleFor(command => command.Severity)
                    .Must(severity => Finding.Severities.Contains(severity!))
                    .WithMessage("Use critical, major or minor.")
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
