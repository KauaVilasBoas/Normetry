using FluentValidation;
using Microsoft.Extensions.DependencyInjection;
using Normetry.BuildingBlocks.Cqrs;
using Normetry.BuildingBlocks.Results;
using Xunit;

namespace Normetry.UnitTests.Cqrs;

public sealed class DispatcherTests
{
    private static CancellationToken Ct => TestContext.Current.CancellationToken;

    [Fact]
    public async Task A_command_runs_its_own_handler()
    {
        var (dispatcher, calls) = Build();

        var result = await dispatcher.SendAsync(new Ping(), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(["ping handler"], calls);
    }

    [Fact]
    public async Task A_command_with_a_value_returns_its_handler_value()
    {
        var (dispatcher, _) = Build();

        var result = await dispatcher.SendAsync(new DoubleNumber(21), Ct);

        Assert.Equal(42, result.Value);
    }

    [Fact]
    public async Task A_query_runs_its_own_handler()
    {
        var (dispatcher, _) = Build();

        var result = await dispatcher.QueryAsync(new GetGreeting("Ana"), Ct);

        Assert.Equal("Hello, Ana", result.Value);
    }

    [Fact]
    public async Task Behaviors_run_in_registration_order_before_the_handler()
    {
        var (dispatcher, calls) = Build(services =>
            services
                .AddScoped<IPipelineBehavior<Ping, Result>>(sp => new Recording(
                    "first",
                    sp.GetRequiredService<Calls>()
                ))
                .AddScoped<IPipelineBehavior<Ping, Result>>(sp => new Recording(
                    "second",
                    sp.GetRequiredService<Calls>()
                ))
        );

        await dispatcher.SendAsync(new Ping(), Ct);

        Assert.Equal(["first", "second", "ping handler"], calls);
    }

    [Fact]
    public async Task A_behavior_can_short_circuit_the_rest_of_the_pipeline()
    {
        var (dispatcher, calls) = Build(services =>
            services
                .AddScoped<IPipelineBehavior<Ping, Result>, Forbid>()
                .AddScoped<IPipelineBehavior<Ping, Result>>(sp => new Recording(
                    "after",
                    sp.GetRequiredService<Calls>()
                ))
        );

        var result = await dispatcher.SendAsync(new Ping(), Ct);

        Assert.Equal(ErrorType.Forbidden, result.Error.Type);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task A_missing_handler_throws_naming_the_request_type()
    {
        var (dispatcher, _) = Build();

        var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
            dispatcher.SendAsync(new Unhandled(), Ct)
        );

        Assert.Contains(typeof(Unhandled).FullName!, exception.Message);
    }

    [Fact]
    public async Task A_validation_failure_returns_an_error_without_calling_the_handler()
    {
        var (dispatcher, calls) = Build();

        var result = await dispatcher.SendAsync(new Rename(""), Ct);

        Assert.False(result.IsSuccess);
        Assert.Equal(ErrorType.Validation, result.Error.Type);
        Assert.Equal(["Name"], result.Error.FieldErrors!.Keys);
        Assert.Empty(calls);
    }

    [Fact]
    public async Task A_valid_request_reaches_the_handler()
    {
        var (dispatcher, calls) = Build();

        var result = await dispatcher.SendAsync(new Rename("Kitchen"), Ct);

        Assert.True(result.IsSuccess);
        Assert.Equal(["rename handler"], calls);
    }

    [Fact]
    public async Task The_cancellation_token_reaches_the_handler()
    {
        var (dispatcher, _) = Build();
        using var cancellation = CancellationTokenSource.CreateLinkedTokenSource(Ct);

        var result = await dispatcher.QueryAsync(new EchoToken(), cancellation.Token);

        Assert.Equal(cancellation.Token, result.Value);
    }

    [Fact]
    public async Task A_second_dispatch_reuses_the_wrapper_built_by_the_first()
    {
        var (dispatcher, _) = Build();
        Assert.False(Dispatcher.Wrappers.ContainsKey(typeof(CacheProbe)));

        await dispatcher.SendAsync(new CacheProbe(), Ct);
        var first = Dispatcher.Wrappers[typeof(CacheProbe)];
        await dispatcher.SendAsync(new CacheProbe(), Ct);

        Assert.Same(first, Dispatcher.Wrappers[typeof(CacheProbe)]);
    }

    private static (IDispatcher Dispatcher, Calls Calls) Build(
        Action<IServiceCollection>? configure = null
    )
    {
        var services = new ServiceCollection()
            .AddLogging()
            .AddSingleton<Calls>()
            .AddDispatcher(typeof(DispatcherTests).Assembly);
        configure?.Invoke(services);

        var scope = services.BuildServiceProvider(validateScopes: true).CreateScope();
        return (
            scope.ServiceProvider.GetRequiredService<IDispatcher>(),
            scope.ServiceProvider.GetRequiredService<Calls>()
        );
    }

    private sealed class Calls : List<string>;

    private sealed record Ping : ICommand;

    private sealed class PingHandler(Calls calls) : ICommandHandler<Ping>
    {
        public Task<Result> HandleAsync(Ping request, CancellationToken cancellationToken)
        {
            calls.Add("ping handler");
            return Task.FromResult(Result.Success());
        }
    }

    private sealed record DoubleNumber(int Number) : ICommand<int>;

    private sealed class DoubleNumberHandler : ICommandHandler<DoubleNumber, int>
    {
        public Task<Result<int>> HandleAsync(
            DoubleNumber request,
            CancellationToken cancellationToken
        ) => Task.FromResult<Result<int>>(request.Number * 2);
    }

    private sealed record GetGreeting(string Name) : IQuery<string>;

    private sealed class GetGreetingHandler : IQueryHandler<GetGreeting, string>
    {
        public Task<Result<string>> HandleAsync(
            GetGreeting request,
            CancellationToken cancellationToken
        ) => Task.FromResult<Result<string>>($"Hello, {request.Name}");
    }

    private sealed record Rename(string Name) : ICommand;

    private sealed class RenameValidator : AbstractValidator<Rename>
    {
        public RenameValidator() => RuleFor(command => command.Name).NotEmpty();
    }

    private sealed class RenameHandler(Calls calls) : ICommandHandler<Rename>
    {
        public Task<Result> HandleAsync(Rename request, CancellationToken cancellationToken)
        {
            calls.Add("rename handler");
            return Task.FromResult(Result.Success());
        }
    }

    private sealed record EchoToken : IQuery<CancellationToken>;

    private sealed class EchoTokenHandler : IQueryHandler<EchoToken, CancellationToken>
    {
        public Task<Result<CancellationToken>> HandleAsync(
            EchoToken request,
            CancellationToken cancellationToken
        ) => Task.FromResult<Result<CancellationToken>>(cancellationToken);
    }

    private sealed record CacheProbe : ICommand;

    private sealed class CacheProbeHandler : ICommandHandler<CacheProbe>
    {
        public Task<Result> HandleAsync(CacheProbe request, CancellationToken cancellationToken) =>
            Task.FromResult(Result.Success());
    }

    private sealed record Unhandled : ICommand;

    private sealed class Recording(string name, Calls calls) : IPipelineBehavior<Ping, Result>
    {
        public Task<Result> HandleAsync(
            Ping request,
            Func<Task<Result>> next,
            CancellationToken cancellationToken
        )
        {
            calls.Add(name);
            return next();
        }
    }

    private sealed class Forbid : IPipelineBehavior<Ping, Result>
    {
        public Task<Result> HandleAsync(
            Ping request,
            Func<Task<Result>> next,
            CancellationToken cancellationToken
        ) => Task.FromResult(Result.Failure(new Error("forbidden", "No.", ErrorType.Forbidden)));
    }
}
