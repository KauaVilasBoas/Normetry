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

    /// <summary>
    /// The first <paramref name="maxLength"/> characters of the value, counted like
    /// <see cref="Required"/>, so a surrogate pair is never cut in half.
    /// </summary>
    public static string Truncate(string value, int maxLength)
    {
        var kept = 0;
        var characters = 0;
        foreach (var rune in value.EnumerateRunes())
        {
            if (characters == maxLength)
            {
                return value[..kept];
            }
            kept += rune.Utf16SequenceLength;
            characters++;
        }
        return value;
    }

    public static Guid RequiredId(Guid value, string paramName) =>
        value == Guid.Empty
            ? throw new ArgumentException("The id must not be empty.", paramName)
            : value;
}
