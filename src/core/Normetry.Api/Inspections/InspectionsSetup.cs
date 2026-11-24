using Normetry.Application.Abstractions;
using Normetry.Application.Inspections;
using Normetry.Application.Usage;

namespace Normetry.Api.Inspections;

internal static class InspectionsSetup
{
    /// <summary>
    /// The <c>Inspections</c> settings, validated now against the chat plans, the review handlers'
    /// <see cref="FindingReviewService"/> and the notifier of <see cref="InspectionsHub"/>.
    /// </summary>
    public static WebApplicationBuilder AddInspections(
        this WebApplicationBuilder builder,
        ChatOptions chatPlans
    )
    {
        builder.BindValidated<InspectionsOptions>(InspectionsOptions.Section, options =>
            options.Validate(chatPlans)
        );
        builder.Services.AddScoped<FindingReviewService>();
        builder.Services.AddSingleton<
            IChangeNotifier<InspectionChange>,
            SignalRInspectionNotifier
        >();
        return builder;
    }
}
