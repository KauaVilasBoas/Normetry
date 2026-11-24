using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Http.Connections;
using Microsoft.AspNetCore.SignalR;

namespace Normetry.Api;

/// <summary>SignalR for the hubs that push one establishment's changes (<see cref="EstablishmentGroups"/>).</summary>
internal static class RealtimeSetup
{
    /// <summary>SignalR, with enums by name as in the HTTP API.</summary>
    public static WebApplicationBuilder AddRealtime(this WebApplicationBuilder builder)
    {
        builder
            .Services.AddSignalR()
            .AddJsonProtocol(protocol =>
                protocol.PayloadSerializerOptions.Converters.Add(
                    new JsonStringEnumConverter(allowIntegerValues: false)
                )
            );
        return builder;
    }

    /// <summary>
    /// WebSockets only: the connection keeps the request that authenticated it for its lifetime,
    /// and it closes when the access token expires.
    /// </summary>
    public static WebApplication MapEstablishmentHub<THub>(this WebApplication app, string path)
        where THub : Hub
    {
        app.MapHub<THub>(path, options =>
        {
            options.Transports = HttpTransportType.WebSockets;
            options.CloseOnAuthenticationExpiration = true;
        });
        return app;
    }
}
