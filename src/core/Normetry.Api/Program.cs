using System.Text.Json.Serialization;
using Normetry.Api.Auth;
using Normetry.Api.Chat;
using Normetry.Api.Documents;
using Normetry.Api.Gateway;
using Normetry.Api.Inspections;
using Normetry.Api.RateLimiting;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.Infrastructure;

var builder = WebApplication.CreateBuilder(args);

builder.AddServiceDefaults();
builder.AddAuth();
builder.AddAiGateway();
builder.AddChat();
builder.AddDocuments();
builder.AddInspections();
builder.AddRateLimiting();
builder.Services.AddProblemDetails();
builder
    .Services.AddControllers(options =>
        // FluentValidation is the only input validator, so every invalid field gets the same
        // problem response (with its code) instead of MVC's implicit [Required] on strings.
        options.SuppressImplicitRequiredAttributeForNonNullableReferenceTypes = true
    )
    // Enums travel by name ("Restaurant", "US"), never by number.
    .AddJsonOptions(options =>
        options.JsonSerializerOptions.Converters.Add(
            new JsonStringEnumConverter(allowIntegerValues: false)
        )
    );
builder.Services.AddDispatcher(typeof(Normetry.Application.AssemblyMarker).Assembly);
builder.Services.AddInfrastructure(builder.Configuration);
if (builder.Environment.IsDevelopment())
{
    builder.Services.AddDemoTenantSeed();
    builder.Services.AddDemoDocumentSeed();
}

var app = builder.Build();

app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();

// After authentication, which gives the partition its tenant and user, and before authorization,
// whose permission check reads the database.
app.UseRateLimiter();
app.UseAuthorization();
app.MapDefaultEndpoints();
app.MapControllers();
app.MapAiGateway();
app.MapDocumentsHub();
app.MapInspectionsHub();

app.Run();
