using System.Diagnostics;
using Microsoft.Extensions.Logging;

namespace Normetry.BuildingBlocks.Cqrs;

/// <summary>
/// Logs the request type name and how long it took. Never the request itself: requests carry
/// tenant data and PII, which must not reach the logs.
/// </summary>
internal sealed class LoggingBehavior<TRequest, TResult>(
    ILogger<LoggingBehavior<TRequest, TResult>> logger
) : IPipelineBehavior<TRequest, TResult>
    where TRequest : IRequest<TResult>
{
    public async Task<TResult> HandleAsync(
        TRequest request,
        Func<Task<TResult>> next,
        CancellationToken cancellationToken
    )
    {
        var started = Stopwatch.GetTimestamp();
        var result = await next();
        logger.LogInformation(
            "{RequestName} handled in {ElapsedMilliseconds} ms",
            typeof(TRequest).Name,
            Stopwatch.GetElapsedTime(started).TotalMilliseconds
        );
        return result;
    }
}
