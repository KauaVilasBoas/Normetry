using Normetry.Application.Abstractions;

namespace Normetry.Infrastructure.Persistence;

/// <summary>
/// The user a background scope acts as. Only the development document seed sets it (the demo
/// Owner uploads the demo documents through the upload command). Set once, before anything in
/// the scope resolves <see cref="ICurrentUser"/>. An HTTP request never sets it, so its user still
/// comes only from the token. The display name and email are null: nothing in such a scope
/// syncs the user row.
/// </summary>
public sealed class BackgroundUser : ICurrentUser
{
    private string? userId;
    private bool resolved;

    /// <summary>
    /// Read once, when the scope resolves <see cref="ICurrentUser"/>; from then on the scope's
    /// user source is fixed, so a late <see cref="Set"/> throws instead of being ignored.
    /// </summary>
    public bool IsSet
    {
        get
        {
            resolved = true;
            return userId is not null;
        }
    }

    public string UserId =>
        userId ?? throw new InvalidOperationException("The background scope has no user.");

    public string? DisplayName => null;

    public string? Email => null;

    internal void Set(string sub)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sub);
        if (userId is not null || resolved)
        {
            throw new InvalidOperationException(
                "The background scope already has a user, or resolved one before it was set."
            );
        }
        userId = sub;
    }
}
