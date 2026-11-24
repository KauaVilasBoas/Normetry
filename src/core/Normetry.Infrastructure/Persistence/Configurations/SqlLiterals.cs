namespace Normetry.Infrastructure.Persistence.Configurations;

/// <summary>
/// The SQL string literals of check constraints and index filters, quoted in this one place.
/// Enums are stored by name, so an enum's literals are its names.
/// </summary>
internal static class SqlLiterals
{
    /// <summary><paramref name="value"/> as a SQL string literal.</summary>
    public static string Quote(string value) =>
        $"'{value.Replace("'", "''", StringComparison.Ordinal)}'";

    /// <summary>The values as comma-separated SQL string literals: the inside of an <c>IN (...)</c>.</summary>
    public static string List(IEnumerable<string> values) =>
        string.Join(", ", values.Select(Quote));

    /// <summary>Every name of <typeparamref name="TEnum"/>, as <see cref="List(IEnumerable{string})"/>.</summary>
    public static string List<TEnum>()
        where TEnum : struct, Enum => List(Enum.GetNames<TEnum>());
}
