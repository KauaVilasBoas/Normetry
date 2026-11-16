using Normetry.Application.Documents;

namespace Normetry.Api.Documents;

internal static class DocumentsSetup
{
    /// <summary>
    /// The <c>Documents</c> settings (the per-establishment limit and the processing timeout),
    /// validated now: the API refuses to start on a limit below 1 or a timeout that is not positive.
    /// </summary>
    public static WebApplicationBuilder AddDocuments(this WebApplicationBuilder builder)
    {
        var options = builder
            .Configuration.GetSection(DocumentsOptions.Section)
            .Get<DocumentsOptions>()
            ?? new DocumentsOptions();
        options.Validate();
        builder.Services.AddSingleton(options);
        return builder;
    }
}
