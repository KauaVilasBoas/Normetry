using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Normetry.Application.Abstractions;

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

    /// <summary>
    /// JWT bearer authentication against the Keycloak realm. Only <c>Authority</c> and
    /// <c>ValidAudiences</c> come from <c>Authentication:Schemes:Bearer</c> (appsettings.json,
    /// overridable by environment variables); everything else is set here. Authorization is by
    /// permissions stored in the core database (ADR 012): realm roles authorize nothing.
    /// </summary>
    public static WebApplicationBuilder AddAuth(this WebApplicationBuilder builder)
    {
        var isDevelopment = builder.Environment.IsDevelopment();
        string[] acceptedClients = isDevelopment ? [WebClientId, DevClientId] : [WebClientId];

        // Development accepts normetry-dev tokens and plain-http metadata, so it must never run
        // against a deployed identity provider.
        var authority = builder.Configuration[
            $"Authentication:Schemes:{JwtBearerDefaults.AuthenticationScheme}:Authority"
        ];
        if (
            isDevelopment
            && !(
                Uri.TryCreate(authority, UriKind.Absolute, out var authorityUri)
                && authorityUri.IsLoopback
            )
        )
        {
            throw new InvalidOperationException(
                $"The Development environment needs a loopback JWT authority, but "
                    + $"Authentication:Schemes:Bearer:Authority is '{authority}'. "
                    + "Run deployed hosts in a non-Development environment."
            );
        }

        builder
            .Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
            .AddJwtBearer(options =>
            {
                // The configuration binder runs before this delegate and also reads ValidIssuer(s),
                // SigningKeys, MetadataAddress, Forward* and the flags below from settings. Only
                // Authority and ValidAudiences are kept: the issuer and signing keys come only from
                // the authority's metadata, so no setting can add a trusted issuer or key.
                options.TokenValidationParameters = new TokenValidationParameters
                {
                    ValidAudiences = options.TokenValidationParameters.ValidAudiences,
                    ValidateIssuer = true,
                    ValidateAudience = true,
                    ValidateLifetime = true,
                    ValidateIssuerSigningKey = true,
                    RequireSignedTokens = true,
                    RequireExpirationTime = true,
                    NameClaimType = "sub",
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
                // Keep claim names as issued (sub, tenant_id, azp): no mapping to WS-* URIs.
                options.MapInboundClaims = false;

                options.Events = new JwtBearerEvents
                {
                    OnTokenValidated = context =>
                    {
                        if (
                            context.Principal!.FindAll("azp").ToArray()
                                is not [{ Value: var clientId }]
                            || !acceptedClients.Contains(clientId)
                        )
                        {
                            context.Fail(
                                "The token was issued to a client this API does not accept."
                            );
                        }
                        return Task.CompletedTask;
                    },
                };
            });

        builder.Services.AddAuthorization(options =>
        {
            // Users are identified by sub only, so a token without it is useless.
            options.DefaultPolicy = new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .RequireClaim("sub")
                .Build();

            var tenant = new AuthorizationPolicyBuilder()
                .Combine(options.DefaultPolicy)
                .RequireAssertion(context => HttpCurrentUser.TryGetTenantId(context.User, out _))
                .Build();
            options.AddPolicy(Policies.Tenant, tenant);

            // An endpoint that declares no authorization is tenant-scoped, never anonymous.
            options.FallbackPolicy = tenant;
        });
        // [HasPermission] policies, checked against the caller's profiles on every request.
        builder.Services.AddSingleton<IAuthorizationPolicyProvider, PermissionPolicyProvider>();
        builder.Services.AddScoped<IAuthorizationHandler, PermissionHandler>();
        builder.Services.AddSingleton<
            Microsoft.AspNetCore.Authorization.IAuthorizationMiddlewareResultHandler,
            PermissionDeniedResultHandler
        >();

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<HttpCurrentUser>();
        builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<HttpCurrentUser>());
        builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<HttpCurrentUser>());

        return builder;
    }
}
