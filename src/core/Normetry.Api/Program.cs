using System.Text.Json.Serialization;
using Normetry.Api;
using Normetry.Api.Auth;
using Normetry.Api.Establishments;
using Normetry.Api.Gateway;
using Normetry.Application.Identity.GetCurrentUser;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddAuth();
builder.AddAiGateway();
builder.Services.AddProblemDetails();

// Enums travel by name ("Restaurant", "US"), never by number.
builder.Services.ConfigureHttpJsonOptions(options =>
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter(allowIntegerValues: false))
);

// A body that does not bind is a 400 in every environment. Development would otherwise throw,
// and the exception handler would answer 500.
builder.Services.Configure<RouteHandlerOptions>(options => options.ThrowOnBadRequest = false);
builder.Services.AddDispatcher(typeof(Normetry.Application.AssemblyMarker).Assembly);
builder.Services.AddInfrastructure(builder.Configuration);
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDemoTenantSeed();
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.MapDefaultEndpoints();

app.MapGet("/me", async (IDispatcher dispatcher, CancellationToken cancellationToken) =>
    {
        var result = await dispatcher.QueryAsync(new GetCurrentUserQuery(), cancellationToken);
        return result.IsSuccess ? Results.Ok(result.Value) : result.ToProblemDetails();
    })
    .RequireAuthorization(Policies.Tenant);
app.MapEstablishmentEndpoints();
app.MapAiGateway();

app.Run();
