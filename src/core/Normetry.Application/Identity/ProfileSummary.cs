namespace Normetry.Application.Identity;

/// <summary>
/// A profile as other resources show it. <see cref="SystemKey"/> is <c>owner</c>, <c>manager</c>
/// or <c>auditor</c> for a system profile, so clients match those without relying on the name.
/// </summary>
public sealed record ProfileSummary(Guid Id, string Name, string? SystemKey);
