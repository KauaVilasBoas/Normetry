using Microsoft.Net.Http.Headers;
using Normetry.Api.Auth;
using Normetry.Api.RateLimiting;
using Normetry.Domain.Identity;
using Normetry.Infrastructure;
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

    public const string Token = HostingKeys.InternalTokenHeader;
    public const string TenantId = "X-Normetry-Tenant-Id";
    public const string UserId = "X-Normetry-User-Id";

    /// <summary>Only on establishment routes, after the core loaded the establishment under RLS.</summary>
    public const string EstablishmentId = "X-Normetry-Establishment-Id";

    /// <summary><c>US</c>, <c>EU</c> or <c>BR</c>: the jurisdiction of that establishment.</summary>
    public const string Jurisdiction = "X-Normetry-Jurisdiction";

    /// <summary>
    /// <c>true</c> when the caller's profiles grant <c>documents.read</c>, absent otherwise: the AI
    /// service shows tenant document text (citations, quotes) only then.
    /// </summary>
    public const string DocumentsRead = "X-Normetry-Documents-Read";
}

/// <summary>
/// YARP routes <c>/ai/{**catch-all}</c> and <c>/ai/establishments/{**rest}</c> to the AI service
/// (ADR 004). Only tenant-scoped callers with <c>chat.ask</c> get through. The AI service trusts these headers, so the
/// gateway removes every internal header and credential the client sent and sets the internal
/// token, tenant and user from the validated token, plus the establishment and its jurisdiction on
/// establishment routes (<see cref="EstablishmentScope"/>). Responses are not buffered, so
/// server-sent events stream through.
/// </summary>
internal static class AiGateway
{
    /// <summary>
    /// The AI service's name in Aspire service discovery, which resolves it from
    /// <see cref="DestinationSetting"/>. Its cluster and that cluster's one destination carry it as
    /// their id.
    /// </summary>
    private const string ServiceName = HostingKeys.AiService;

    private const string Destination = $"http://{ServiceName}";

    private const string DestinationSetting = $"Services:{ServiceName}:http";

    /// <summary>Every path but an establishment's (<see cref="EstablishmentScope.RouteId"/>).</summary>
    private const string CatchAllRouteId = ServiceName;

    /// <summary>Every AI route lives under it; it is removed before forwarding.</summary>
    private const string PathPrefix = "/ai";

    private const string TokenSetting = HostingKeys.InternalTokenSetting;

    /// <summary>Same rule as the AI service, which refuses to start with a shorter token.</summary>
    private const int MinimumTokenLength = 32;

    public static WebApplicationBuilder AddAiGateway(this WebApplicationBuilder builder)
    {
        var token = ReadInternalToken(builder.Configuration);
        EnsureAiDestinationConfigured(builder.Configuration);

        builder
            .Services.AddReverseProxy()
            .LoadFromMemory(Routes(), Clusters())
            .AddServiceDiscoveryDestinationResolver()
            .AddTransforms(context =>
            {
                context.RequestTransforms.Add(new InternalHeadersTransform(token));
                context.AddResponseTransform(RemoveInternalResponseHeaders);
            });
        return builder;
    }

    private static string ReadInternalToken(IConfiguration configuration)
    {
        var token = configuration[TokenSetting];
        if (token is not { Length: >= MinimumTokenLength })
        {
            throw new InvalidOperationException(
                $"Set {TokenSetting} to the AI service's internal token ({MinimumTokenLength} characters or more)."
            );
        }
        return token;
    }

    /// <summary>
    /// Without it, service discovery falls back to a DNS lookup of "ai", which could send the
    /// token and the caller's tenant to whatever host answers to that name.
    /// </summary>
    private static void EnsureAiDestinationConfigured(IConfiguration configuration)
    {
        if (
            !configuration
                .GetSection(DestinationSetting)
                .GetChildren()
                .Any(endpoint => !string.IsNullOrEmpty(endpoint.Value))
        )
        {
            throw new InvalidOperationException(
                $"Set {DestinationSetting}:0 to the AI service's address (the AppHost does)."
            );
        }
    }

    /// <summary>
    /// The literal "establishments" segment wins over the catch-all, in any case. The id is not
    /// constrained in the pattern: a malformed one must get 404 from EstablishmentScope, not fall
    /// through to the catch-all route without establishment headers. Conversations and messages
    /// (each a model call) go through the establishment route, so it takes the stricter chat
    /// limit; reading and deleting conversations take only the global one.
    /// </summary>
    private static RouteConfig[] Routes() =>
        [
            Route(CatchAllRouteId, $"{PathPrefix}/{{**{EstablishmentScope.CatchAllRouteValue}}}"),
            Route(
                EstablishmentScope.RouteId,
                $"{PathPrefix}/{EstablishmentScope.EstablishmentsSegment}/{{**{EstablishmentScope.RouteValue}}}"
            ) with
            {
                RateLimiterPolicy = RateLimitPolicies.Chat,
            },
        ];

    private static ClusterConfig[] Clusters() =>
        [
            new ClusterConfig
            {
                ClusterId = ServiceName,
                Destinations = new Dictionary<string, DestinationConfig>
                {
                    [ServiceName] = new() { Address = Destination },
                },
            },
        ];

    private static RouteConfig Route(string id, string path) =>
        new RouteConfig
        {
            RouteId = id,
            ClusterId = ServiceName,
            // Every AI route is chat: conversations, messages and their search.
            AuthorizationPolicy = HasPermissionAttribute.PolicyName(Permissions.Chat.Ask),
            Match = new RouteMatch { Path = path },
        }
            .WithTransformPathRemovePrefix(PathPrefix)
            // A query-string token (SignalR style) must not reach the AI service or its logs.
            .WithTransformQueryRemoveKey(HubPaths.AccessTokenQueryKey);

    /// <summary>Nothing internal goes back to the client either, the token above all.</summary>
    private static ValueTask RemoveInternalResponseHeaders(ResponseTransformContext context)
    {
        var headers = context.HttpContext.Response.Headers;
        foreach (var name in headers.Keys.Where(IsInternal).ToArray())
        {
            headers.Remove(name);
        }
        return ValueTask.CompletedTask;
    }

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
        private static readonly string[] Credentials =
        [
            HeaderNames.Authorization,
            HeaderNames.Cookie,
        ];

        public override ValueTask ApplyAsync(RequestTransformContext context)
        {
            RemoveClientInternalHeadersAndCredentials(context);
            AddCallerHeaders(context);
            AddEstablishmentHeaders(context);
            return ValueTask.CompletedTask;
        }

        private static void RemoveClientInternalHeadersAndCredentials(
            RequestTransformContext context
        )
        {
            var inbound = context
                .HttpContext.Request.Headers.Keys.Where(IsInternal)
                .Concat(Credentials)
                .ToArray();
            foreach (var name in inbound)
            {
                RemoveHeader(context, name);
            }
        }

        /// <summary>
        /// The internal token, and the tenant and user of the validated token, plus
        /// <see cref="InternalHeaders.DocumentsRead"/> when the caller's permissions include
        /// <c>documents.read</c>.
        /// </summary>
        private void AddCallerHeaders(RequestTransformContext context)
        {
            // The route policy guarantees both; failing here means the policy was removed.
            if (!context.HttpContext.User.TryGetCaller(out var tenantId, out var userId))
            {
                throw new InvalidOperationException(
                    $"The /ai route must require a policy that includes {Policies.Tenant}."
                );
            }

            // The set the route's permission policy loaded; no permission check, no forwarding.
            var permissions = context.HttpContext.Features.Get<CallerPermissions>()
                ?? throw new InvalidOperationException(
                    "The /ai route must require a permission policy."
                );

            AddHeader(context, InternalHeaders.Token, token);
            AddHeader(context, InternalHeaders.TenantId, tenantId.ToString());
            AddHeader(context, InternalHeaders.UserId, userId);
            if (permissions.Names.Contains(Permissions.Documents.Read))
            {
                AddHeader(context, InternalHeaders.DocumentsRead, "true");
            }
        }

        /// <summary>
        /// The establishment <see cref="EstablishmentScope"/> loaded and its jurisdiction. An
        /// establishment route without it means the proxy pipeline lost that step.
        /// </summary>
        private static void AddEstablishmentHeaders(RequestTransformContext context)
        {
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
        }
    }
}
