using Normetry.Api;
using Normetry.Api.Auth;
using Normetry.Application.Identity.GetCurrentUser;
using Normetry.BuildingBlocks.Cqrs;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddAuth();
builder.Services.AddProblemDetails();
builder.Services.AddDispatcher(typeof(Normetry.Application.AssemblyMarker).Assembly);

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

app.Run();
