using System.Security.Claims;
using Normetry.Api.Auth;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Transforms;

namespace Normetry.Api.Gateway;

/// <summary>
/// Header names shared with the AI service. The contract and the rules for each side live in
/// <c>src/shared/contracts/internal-http.md</c>.
/// </summary>
public static class InternalHeaders
{
    /// <summary>Every internal header starts with it; the gateway drops any a client sends.</summary>
    public const string Prefix = "X-Normetry-";

    public const string Token = "X-Normetry-Internal-Token";
    public const string TenantId = "X-Normetry-Tenant-Id";
    public const string UserId = "X-Normetry-User-Id";

    /// <summary>Only on establishment routes, after the core loaded the establishment under RLS.</summary>
    public const string EstablishmentId = "X-Normetry-Establishment-Id";

    /// <summary><c>US</c>, <c>EU</c> or <c>BR</c>: the jurisdiction of that establishment.</summary>
    public const string Jurisdiction = "X-Normetry-Jurisdiction";
}

/// <summary>
/// YARP routes <c>/ai/{**catch-all}</c> and <c>/ai/establishments/{**rest}</c> to the AI service
/// (ADR 004). Only tenant-scoped callers get through. The AI service trusts these headers, so the
/// gateway removes every internal header and credential the client sent and sets the internal
/// token, tenant and user from the validated token, plus the establishment and its jurisdiction on
/// establishment routes (<see cref="EstablishmentScope"/>). Responses are not buffered, so
/// server-sent events stream through.
/// </summary>
internal static class AiGateway
{
    /// <summary>Aspire service discovery resolves it from <c>Services:ai:http</c>.</summary>
    private const string Destination = "http://ai";

    private const string DestinationSetting = "Services:ai:http";

    private const string TokenSetting = "Ai:InternalToken";

    public static WebApplicationBuilder AddAiGateway(this WebApplicationBuilder builder)
    {
        // Same rule as the AI service, which refuses to start with a shorter token.
        var token = builder.Configuration[TokenSetting];
        if (token is not { Length: >= 32 })
        {
            throw new InvalidOperationException(
                $"Set {TokenSetting} to the AI service's internal token (32 characters or more)."
            );
        }

        // Without it, service discovery falls back to a DNS lookup of "ai", which could send the
        // token and the caller's tenant to whatever host answers to that name.
        if (
            !builder
                .Configuration.GetSection(DestinationSetting)
                .GetChildren()
                .Any(endpoint => !string.IsNullOrEmpty(endpoint.Value))
        )
        {
            throw new InvalidOperationException(
                $"Set {DestinationSetting}:0 to the AI service's address (the AppHost does)."
            );
        }

        // The literal "establishments" segment wins over the catch-all, in any case. The id is
        // not constrained in the pattern: a malformed one must get 404 from EstablishmentScope,
        // not fall through to the catch-all route without establishment headers.
        RouteConfig[] routes =
        [
            Route("ai", "/ai/{**catch-all}"),
            Route(
                EstablishmentScope.RouteId,
                $"/ai/establishments/{{**{EstablishmentScope.RouteValue}}}"
            ),
        ];
        ClusterConfig[] clusters =
        [
            new ClusterConfig
            {
                ClusterId = "ai",
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    ["ai"] = new() { Address = Destination },
                },
            },
        ];

        builder
            .Services.AddReverseProxy()
            .LoadFromMemory(routes, clusters)
            .AddServiceDiscoveryDestinationResolver()
            .AddTransforms(context =>
            {
                context.RequestTransforms.Add(new InternalHeadersTransform(token));
                // Nothing internal goes back to the client either, the token above all.
                context.AddResponseTransform(response =>
                {
                    var headers = response.HttpContext.Response.Headers;
                    foreach (var name in headers.Keys.Where(IsInternal).ToArray())
                    {
                        headers.Remove(name);
                    }
                    return ValueTask.CompletedTask;
                });
            });
        return builder;
    }

    private static RouteConfig Route(string id, string path) =>
        new RouteConfig
        {
            RouteId = id,
            ClusterId = "ai",
            AuthorizationPolicy = Policies.Tenant,
            Match = new RouteMatch { Path = path },
        }
            .WithTransformPathRemovePrefix("/ai")
            // A query-string token (SignalR style) must not reach the AI service or its logs.
            .WithTransformQueryRemoveKey("access_token");

    // Also catches X_Normetry_*: some servers and adapters read underscores as dashes.
    private static bool IsInternal(string name) =>
        name.Replace('_', '-')
            .StartsWith(InternalHeaders.Prefix, StringComparison.OrdinalIgnoreCase);

    public static WebApplication MapAiGateway(this WebApplication app)
    {
        app.MapReverseProxy(proxy =>
        {
            // Loads the establishment and counts chat messages before anything is forwarded.
            proxy.Use(EstablishmentScope.InvokeAsync);
            // The steps of the default pipeline, which a custom one replaces.
            proxy.UseSessionAffinity();
            proxy.UseLoadBalancing();
            proxy.UsePassiveHealthChecks();
        });
        return app;
    }

    private sealed class InternalHeadersTransform(string token) : RequestTransform
    {
        // Credentials meant for the core never reach the AI service.
        private static readonly string[] Credentials = ["Authorization", "Cookie"];

        public override ValueTask ApplyAsync(RequestTransformContext context)
        {
            var inbound = context
                .HttpContext.Request.Headers.Keys.Where(IsInternal)
                .Concat(Credentials)
                .ToArray();
            foreach (var name in inbound)
            {
                RemoveHeader(context, name);
            }

            // The route policy guarantees both; failing here means the policy was removed.
            var user = context.HttpContext.User;
            if (
                !HttpCurrentUser.TryGetTenantId(user, out var tenantId)
                || user.FindFirstValue("sub") is not { Length: > 0 } userId
            )
            {
                throw new InvalidOperationException(
                    $"The /ai route must require the {Policies.Tenant} policy."
                );
            }

            AddHeader(context, InternalHeaders.Token, token);
            AddHeader(context, InternalHeaders.TenantId, tenantId.ToString());
            AddHeader(context, InternalHeaders.UserId, userId);

            if (context.HttpContext.Features.Get<EstablishmentScope>() is { } establishment)
            {
                AddHeader(
                    context,
                    InternalHeaders.EstablishmentId,
                    establishment.EstablishmentId.ToString()
                );
                AddHeader(
                    context,
                    InternalHeaders.Jurisdiction,
                    establishment.Jurisdiction.ToString()
                );
            }
            else if (
                context.HttpContext.GetReverseProxyFeature().Route.Config.RouteId
                == EstablishmentScope.RouteId
            )
            {
                throw new InvalidOperationException(
                    $"The proxy pipeline must run {nameof(EstablishmentScope)} on establishment routes."
                );
            }
            return ValueTask.CompletedTask;
        }
    }
}
