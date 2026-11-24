namespace Normetry.Domain;

public static class Names
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

    /// <summary>The value when it is one of <paramref name="codes"/>. Throws otherwise.</summary>
    public static string KnownCode(string value, IReadOnlySet<string> codes, string paramName) =>
        codes.Contains(value)
            ? value
            : throw new ArgumentException($"'{value}' is not a known code.", paramName);

    /// <summary>The value when its enum names it. Throws otherwise, also for a missing one (0).</summary>
    public static TEnum Defined<TEnum>(TEnum value, string paramName)
        where TEnum : struct, Enum =>
        Enum.IsDefined(value)
            ? value
            : throw new ArgumentOutOfRangeException(
                paramName,
                value,
                $"Unknown {typeof(TEnum).Name}."
            );
}
