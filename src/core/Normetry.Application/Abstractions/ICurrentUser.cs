namespace Normetry.Application.Abstractions;

/// <summary>The caller of the current request, read from the validated access token.</summary>
public interface ICurrentUser
{
    /// <summary>The identity provider's subject id (<c>sub</c>). Never the email: users can change it.</summary>
    string UserId { get; }

    /// <summary>The token's <c>name</c> (or <c>preferred_username</c>), for display only.</summary>
    string? DisplayName { get; }

    /// <summary>The token's <c>email</c>, for display only: nothing authorizes on it.</summary>
    string? Email { get; }

    /// <summary>The Normetry roles (Owner, Manager, Auditor) the caller holds.</summary>
    IReadOnlyList<string> Roles { get; }
}
