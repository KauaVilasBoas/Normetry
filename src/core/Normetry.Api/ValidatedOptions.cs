namespace Normetry.Api;

internal static class ValidatedOptions
{
    /// <summary>
    /// Binds the configuration section to a <typeparamref name="TOptions"/> (its defaults when the
    /// section is missing), runs <paramref name="validate"/> now, which throws on a bad value so the
    /// API refuses to start, and registers the instance as a singleton. Returns it for the setup
    /// that needs it next.
    /// </summary>
    public static TOptions BindValidated<TOptions>(
        this WebApplicationBuilder builder,
        string section,
        Action<TOptions> validate
    )
        where TOptions : class, new()
    {
        var options = builder.Configuration.GetSection(section).Get<TOptions>() ?? new TOptions();
        validate(options);
        builder.Services.AddSingleton(options);
        return options;
    }
}
