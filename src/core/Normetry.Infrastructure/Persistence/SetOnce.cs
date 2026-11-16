namespace Normetry.Infrastructure.Persistence;

/// <summary>
/// A value a background service sets on its scope once, before anything in the scope reads it.
/// The first <see cref="IsSet"/> fixes the scope's source, so a later set throws instead of being
/// ignored.
/// </summary>
public abstract class SetOnce<T>(string name)
{
    private T? value;
    private bool set;
    private bool resolved;

    public bool IsSet
    {
        get
        {
            resolved = true;
            return set;
        }
    }

    protected T Value =>
        set ? value! : throw new InvalidOperationException($"The background scope has no {name}.");

    protected void SetValue(T newValue)
    {
        if (set || resolved)
        {
            throw new InvalidOperationException(
                $"The background scope already has a {name}, or resolved one before it was set."
            );
        }
        value = newValue;
        set = true;
    }
}
