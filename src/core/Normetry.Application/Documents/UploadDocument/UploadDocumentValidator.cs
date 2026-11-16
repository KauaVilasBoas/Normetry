using FluentValidation;
using Normetry.Domain.Documents;

namespace Normetry.Application.Documents.UploadDocument;

internal sealed class UploadDocumentValidator : AbstractValidator<UploadDocumentCommand>
{
    public UploadDocumentValidator()
    {
        RuleFor(command => command.Title).NotEmpty().MaximumLength(Document.TitleMaxLength);
        RuleFor(command => command.Type).IsInEnum();
        RuleFor(command => command.File).NotNull();
    }
}
