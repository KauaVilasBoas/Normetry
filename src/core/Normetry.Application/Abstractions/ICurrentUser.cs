namespace Normetry.Application.Abstractions;

/// <summary>The caller of the current request, read from the validated access token.</summary>
public interface ICurrentUser
{
    /// <summary>The identity provider's subject id (<c>sub</c>). Never the email: users can change it.</summary>
    string UserId { get; }

    /// <summary>The Normetry roles (Owner, Manager, Auditor) the caller holds.</summary>
    IReadOnlyList<string> Roles { get; }
}
