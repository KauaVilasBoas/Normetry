namespace Normetry.BuildingBlocks.Results;

/// <summary>
/// Lets generic pipeline code (the validation behavior) build a failed <c>Result</c> or
/// <c>Result&lt;T&gt;</c> without knowing which one it holds, and without reflection.
/// </summary>
internal interface IFailureFactory<TSelf>
    where TSelf : IFailureFactory<TSelf>
{
    static abstract TSelf Failure(Error error);
}

/// <summary>Outcome of an operation that returns no value.</summary>
public class Result : IFailureFactory<Result>
{
    private readonly Error? error;

    protected Result(Error? error) => this.error = error;

    public bool IsSuccess => error is null;

    public Error Error =>
        error ?? throw new InvalidOperationException("A successful result has no error.");

    public static Result Success() => new(null);

    public static Result Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(error);
    }

    public static implicit operator Result(Error error) => Failure(error);
}

/// <summary>Outcome of an operation that returns a <typeparamref name="TValue"/> on success.</summary>
public sealed class Result<TValue> : Result, IFailureFactory<Result<TValue>>
{
    private readonly TValue? value;

    private Result(TValue? value, Error? error)
        : base(error) => this.value = value;

    public TValue Value =>
        IsSuccess ? value! : throw new InvalidOperationException("A failed result has no value.");

    public static Result<TValue> Success(TValue value) => new(value, null);

    public static new Result<TValue> Failure(Error error)
    {
        ArgumentNullException.ThrowIfNull(error);
        return new(default, error);
    }

    public static implicit operator Result<TValue>(TValue value) => Success(value);

    public static implicit operator Result<TValue>(Error error) => Failure(error);
}
