using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.IdentityModel.Tokens;
using Normetry.Application.Abstractions;

namespace Normetry.Api.Auth;

/// <summary>Keycloak realm roles. Only these become role claims; defaults such as offline_access are dropped.</summary>
public static class NormetryRoles
{
    public const string Owner = "Owner";
    public const string Manager = "Manager";
    public const string Auditor = "Auditor";

    internal static readonly string[] All = [Owner, Manager, Auditor];
}

public static class Policies
{
    /// <summary>
    /// Authenticated user with exactly one valid tenant id. Every tenant-scoped endpoint requires
    /// it, directly or through a role policy (each role policy includes it). Fails with 403.
    /// </summary>
    public const string Tenant = "Tenant";
    public const string Owner = NormetryRoles.Owner;
    public const string Manager = NormetryRoles.Manager;
    public const string Auditor = NormetryRoles.Auditor;
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
    /// overridable by environment variables); everything else is set here.
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
                        var principal = context.Principal!;
                        if (
                            principal.FindAll("azp").ToArray() is not [{ Value: var clientId }]
                            || !acceptedClients.Contains(clientId)
                        )
                        {
                            context.Fail(
                                "The token was issued to a client this API does not accept."
                            );
                            return Task.CompletedTask;
                        }

                        var identity = (ClaimsIdentity)principal.Identity!;
                        foreach (var role in RealmRoles(principal).Intersect(NormetryRoles.All))
                        {
                            identity.AddClaim(new Claim(identity.RoleClaimType, role));
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

            foreach (var role in NormetryRoles.All)
            {
                options.AddPolicy(role, policy => policy.Combine(tenant).RequireRole(role));
            }

            // An endpoint that declares no authorization is tenant-scoped, never anonymous.
            options.FallbackPolicy = tenant;
        });

        builder.Services.AddHttpContextAccessor();
        builder.Services.AddScoped<HttpCurrentUser>();
        builder.Services.AddScoped<ICurrentUser>(sp => sp.GetRequiredService<HttpCurrentUser>());
        builder.Services.AddScoped<ITenantContext>(sp => sp.GetRequiredService<HttpCurrentUser>());

        return builder;
    }

    // Keycloak sends realm roles as {"realm_access": {"roles": [...]}}, which arrives as one JSON claim.
    private static string[] RealmRoles(ClaimsPrincipal principal)
    {
        if (principal.FindFirstValue("realm_access") is not { } json)
        {
            return [];
        }

        using var document = JsonDocument.Parse(json);
        return
            document.RootElement.ValueKind == JsonValueKind.Object
            && document.RootElement.TryGetProperty("roles", out var roles)
            && roles.ValueKind == JsonValueKind.Array
            ? roles
                .EnumerateArray()
                .Where(role => role.ValueKind == JsonValueKind.String)
                .Select(role => role.GetString()!)
                .ToArray()
            : [];
    }
}
