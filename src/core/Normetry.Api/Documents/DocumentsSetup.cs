using Normetry.Application.Abstractions;
using Normetry.Application.Documents;

namespace Normetry.Api.Documents;

internal static class DocumentsSetup
{
    /// <summary>
    /// The <c>Documents</c> settings (the per-establishment limit and the processing timeout),
    /// validated now: the API refuses to start on a limit below 1 or a timeout that is not positive.
    /// Also the upload handlers' <see cref="DocumentUploadService"/> and the notifier of
    /// <see cref="DocumentsHub"/>.
    /// </summary>
    public static WebApplicationBuilder AddDocuments(this WebApplicationBuilder builder)
    {
        builder.BindValidated<DocumentsOptions>(DocumentsOptions.Section, options =>
            options.Validate()
        );
        builder.Services.AddScoped<DocumentUploadService>();
        builder.Services.AddSingleton<
            IChangeNotifier<DocumentStatusChange>,
            SignalRDocumentStatusNotifier
        >();
        return builder;
    }
}
