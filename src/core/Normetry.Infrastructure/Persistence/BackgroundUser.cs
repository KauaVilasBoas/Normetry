using Normetry.Application.Abstractions;

namespace Normetry.Infrastructure.Persistence;

/// <summary>
/// The user a background scope acts as. Only the development document seed sets it (the demo
/// Owner uploads the demo documents through the upload command). Set once, before anything in
/// the scope resolves <see cref="ICurrentUser"/>. An HTTP request never sets it, so its user still
/// comes only from the token. The display name and email are null: nothing in such a scope
/// syncs the user row.
/// </summary>
public sealed class BackgroundUser() : SetOnce<string>("user"), ICurrentUser
{
    public string Sub => Value;

    public string? DisplayName => null;

    public string? Email => null;

    internal void Set(string sub)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(sub);
        SetValue(sub);
    }
}
