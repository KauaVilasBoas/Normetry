using System.Globalization;

namespace Normetry.Api;

internal static class HttpResponseExtensions
{
    /// <summary>Sets <c>Retry-After</c> to <paramref name="delay"/> in whole seconds, rounded up.</summary>
    public static void SetRetryAfter(this HttpResponse response, TimeSpan delay) =>
        response.Headers.RetryAfter = WholeSeconds(delay);

    /// <summary>A delay as the rate limit headers write it: whole seconds, rounded up.</summary>
    public static string WholeSeconds(TimeSpan delay) =>
        ((long)Math.Ceiling(delay.TotalSeconds)).ToString(CultureInfo.InvariantCulture);
}
