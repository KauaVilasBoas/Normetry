using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Normetry.Application.Abstractions;
using Normetry.Application.Documents;

namespace Normetry.Api.Documents;

internal static class DocumentsSetup
{
    /// <summary>
    /// The <c>Documents</c> settings (the per-establishment limit and the processing timeout),
    /// validated now: the API refuses to start on a limit below 1 or a timeout that is not positive.
    /// Also the upload handlers' <see cref="DocumentUploadService"/>, and SignalR for
    /// <see cref="DocumentsHub"/>, with enums by name as in the HTTP API.
    /// </summary>
    public static WebApplicationBuilder AddDocuments(this WebApplicationBuilder builder)
    {
        var options = builder
            .Configuration.GetSection(DocumentsOptions.Section)
            .Get<DocumentsOptions>()
            ?? new DocumentsOptions();
        options.Validate();
        builder.Services.AddSingleton(options);
        builder.Services.AddScoped<DocumentUploadService>();
        builder
            .Services.AddSignalR()
            .AddJsonProtocol(protocol =>
                protocol.PayloadSerializerOptions.Converters.Add(
                    new JsonStringEnumConverter(allowIntegerValues: false)
                )
            );
        builder.Services.AddSingleton<
            IChangeNotifier<DocumentStatusChange>,
            SignalRDocumentStatusNotifier
        >();
        return builder;
    }

    /// <summary>
    /// WebSockets only: the connection keeps the request that authenticated it for its lifetime,
    /// and it closes when the access token expires.
    /// </summary>
    public static WebApplication MapDocumentsHub(this WebApplication app)
    {
        app.MapHub<DocumentsHub>(DocumentsHub.Path, options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.CloseOnAuthenticationExpiration = true;
        });
        return app;
    }
}
