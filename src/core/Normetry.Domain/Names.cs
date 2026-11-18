namespace Normetry.Domain;

internal static class Names
{
    /// <summary>The name without surrounding spaces. Throws when it is blank or too long.</summary>
    public static string Required(string? value, int maxLength, string paramName)
    {
        var name = value?.Trim();
        if (string.IsNullOrEmpty(name))
        {
            throw new ArgumentException("A name is required.", paramName);
        }
        // Characters as PostgreSQL's varchar and the message contracts count them, not UTF-16 units.
        if (name.EnumerateRunes().Count() > maxLength)
        {
            throw new ArgumentException($"A name has at most {maxLength} characters.", paramName);
        }
        return name;
    }

    public static Guid RequiredId(Guid value, string paramName) =>
        value == Guid.Empty
            ? throw new ArgumentException("The id must not be empty.", paramName)
            : value;
}
