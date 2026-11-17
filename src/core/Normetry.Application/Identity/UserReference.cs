namespace Normetry.Application.Identity;

/// <summary>A user's <c>core.users</c> id and display name (from their token, display only).</summary>
public sealed record UserReference(Guid Id, string? DisplayName);
