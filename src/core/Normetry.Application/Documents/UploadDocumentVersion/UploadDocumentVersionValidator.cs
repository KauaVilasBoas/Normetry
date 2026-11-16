using FluentValidation;

namespace Normetry.Application.Documents.UploadDocumentVersion;

internal sealed class UploadDocumentVersionValidator
    : AbstractValidator<UploadDocumentVersionCommand>
{
    public UploadDocumentVersionValidator() => RuleFor(command => command.File).NotNull();
}
