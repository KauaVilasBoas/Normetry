using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Normetry.Application.Abstractions;
using Normetry.Infrastructure.Persistence;

namespace Normetry.Api.Auth;

public static class Policies
{
    /// <summary>
    /// Authenticated user with exactly one valid tenant id. Every tenant-scoped endpoint requires
    /// it, directly or through a permission policy (<see cref="HasPermissionAttribute"/>), which
    /// includes it. Fails with 403. Only <c>/me</c> requires it alone.
    /// </summary>
    public const string Tenant = "Tenant";
}

internal static class AuthSetup
{
    // The clients (azp) whose access tokens the API accepts. normetry-dev is the local
    // password-grant client from deploy/keycloak, so it is accepted only in Development.
    private const string WebClientId = "normetry-web";
    private const string DevClientId = "normetry-dev";

    private const string AuthoritySetting =
        $"Authentication:Schemes:{JwtBearerDefaults.AuthenticationScheme}:Authority";

    /// <summary>
    /// JWT bearer authentication against the Keycloak realm. Only <c>Authority</c> and
    /// <c>ValidAudiences</c> come from <c>Authentication:Schemes:Bearer</c> (appsettings.json,
    /// overridable by environment variables); everything else is set here. Authorization is by
    /// permissions stored in the core database (ADR 012): realm roles authorize nothing.
    /// </summary>
    public static WebApplicationBuilder AddAuth(this WebApplicationBuilder builder)
    {
        var isDevelopment = builder.Environment.IsDevelopment();
        EnsureLoopbackAuthorityInDevelopment(builder.Configuration, isDevelopment);
        ConfigureJwtBearer(builder.Services, isDevelopment);
        AddTenantPolicies(builder.Services);
        AddPermissionAuthorization(builder.Services);
        AddCallerContext(builder.Services);
        return builder;
    }

    /// <summary>
    /// Development accepts normetry-dev tokens and plain-http metadata, so it must never run
    /// against a deployed identity provider.
    /// </summary>
    private static void EnsureLoopbackAuthorityInDevelopment(
        IConfiguration configuration,
        bool isDevelopment
    )
    {
        if (!isDevelopment)
        {
            return;
        }

        var authority = configuration[AuthoritySetting];
        if (
            Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri)
            && authorityUri.IsLoopback
        )
        {
            return;
        }

        throw new InvalidOperationException(
            $"The Development environment needs a loopback JWT authority, but "
                + $"{AuthoritySetting} is '{authority}'. "
                + "Run deployed hosts in a non-Development environment."
        );
    }

    private static void ConfigureJwtBearer(IServiceCollection services, bool isDevelopment)
    {
        string[] acceptedClients = isDevelopment ? [WebClientId, DevClientId] : [WebClientId];

        services
            .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                KeepOnlyAuthorityAndAudiences(options, isDevelopment);
                options.Events = new JwtBearerEvents
                {
                    OnMessageReceived = ReadHubTokenFromQueryString,
                    OnTokenValidated = context => RejectUnlistedClient(context, acceptedClients),
                };
            });
    }

    /// <summary>
    /// The configuration binder runs before the bearer options delegate and also reads
    /// ValidIssuer(s), SigningKeys, MetadataAddress, Forward* and the flags below from settings.
    /// Only Authority and ValidAudiences are kept: the issuer and signing keys come only from the
    /// authority's metadata, so no setting can add a trusted issuer or key.
    /// </summary>
    private static void KeepOnlyAuthorityAndAudiences(JwtBearerOptions options, bool isDevelopment)
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudiences = options.TokenValidationParameters.ValidAudiences,
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            RequireSignedTokens = true,
            RequireExpirationTime = true,
            NameClaimType = JwtClaimNames.Subject,
        };
        // Back to its default (null), so the metadata address is derived from Authority.
        options.MetadataAddress = null!;
        options.ForwardDefault =
            options.ForwardAuthenticate =
            options.ForwardChallenge =
            options.ForwardForbid =
            options.ForwardSignIn =
            options.ForwardSignOut =
                null;
        options.RequireHttpsMetadata = !isDevelopment;
        // Keep claim names as issued (JwtClaimNames): no mapping to WS-* URIs.
        options.MapInboundClaims = false;
    }

    /// <summary>
    /// Browsers cannot set headers on a WebSocket, so the SignalR client sends the access token in
    /// the query string. It is read there for the hubs only.
    /// </summary>
    private static Task ReadHubTokenFromQueryString(MessageReceivedContext context)
    {
        if (
            context.Request.Path.StartsWithSegments(HubPaths.Prefix)
            && context.Request.Query[HubPaths.AccessTokenQueryKey] is [{ Length: > 0 } token]
        )
        {
            context.Token = token;
        }
        return Task.CompletedTask;
    }

    /// <summary>A token without exactly one <c>azp</c>, or issued to another client, gets 401.</summary>
    private static Task RejectUnlistedClient(
        TokenValidatedContext context,
        string[] acceptedClients
    )
    {
        if (
            context.Principal!.FindAll(JwtClaimNames.AuthorizedParty).ToArray()
                is not [{ Value: var clientId }]
            || !acceptedClients.Contains(clientId)
        )
        {
            context.Fail("The token was issued to a client this API does not accept.");
        }
        return Task.CompletedTask;
    }

    private static void AddTenantPolicies(IServiceCollection services)
    {
        services.AddAuthorization(options =>
        {
            // Users are identified by sub only, so a token without it, or with an empty one, is
            // useless.
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireAssertion(context => context.User.TryGetSubject(out _))
                .Build();

            var tenant = new AuthorizationPolicyBuilder()
                .Combine(options.DefaultPolicy)
                .RequireAssertion(context => context.User.TryGetTenantId(out _))
                .Build();
            options.AddPolicy(Policies.Tenant, tenant);

            // An endpoint that declares no authorization is tenant-scoped, never anonymous.
            options.FallbackPolicy = tenant;
        });
    }

    /// <summary>[HasPermission] policies, checked against the caller's profiles on every request.</summary>
    private static void AddPermissionAuthorization(IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        services.AddSingleton<
            Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler,
            PermissionDeniedResultHandler
        >();
    }

    /// <summary>
    /// The caller a handler sees (<see cref="ICurrentUser"/>, <see cref="ITenantContext"/>): a
    /// request's comes only from its token, a background scope's from what it set.
    /// </summary>
    private static void AddCallerContext(IServiceCollection services)
    {
        services.AddHttpContextAccessor();
        services.AddScoped<HttpCurrentUser>();
        // Only the development document seed sets a background user; a request's user comes
        // only from its token.
        services.AddScoped<ICurrentUser>(sp =>
            sp.GetRequiredService<BackgroundUser>() is { IsSet: true } background
                ? background
                : sp.GetRequiredService<HttpCurrentUser>()
        );
        // A background service's scope (outbox dispatcher, queue consumer) sets its tenant from
        // the outbox row or the message; a request's tenant comes only from its token.
        services.AddScoped<ITenantContext>(sp =>
            sp.GetRequiredService<BackgroundTenant>() is { IsSet: true } background
                ? background
                : sp.GetRequiredService<HttpCurrentUser>()
        );
    }
}
