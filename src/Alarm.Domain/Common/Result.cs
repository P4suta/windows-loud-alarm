namespace Alarm.Domain.Common;

/// <summary>
/// Closed disjoint union of "successful value" and "expected failure" — used wherever a
/// caller has a meaningful recovery path. Exceptions remain for genuinely unexpected state.
/// </summary>
/// <remarks>
/// Construction goes through the non-generic <see cref="Result"/> helper so that
/// CA1000 (no static members on generic types) holds.
/// </remarks>
public readonly record struct Result<TOk, TErr>
{
    public bool IsOk { get; }
    public TOk Value { get; }
    public TErr Error { get; }

    internal Result(bool isOk, TOk value, TErr error)
    {
        IsOk = isOk;
        Value = value;
        Error = error;
    }

    public TR Match<TR>(Func<TOk, TR> ok, Func<TErr, TR> err) =>
        IsOk ? ok(Value) : err(Error);

    public Result<TR, TErr> Map<TR>(Func<TOk, TR> f) =>
        IsOk ? Result.Ok<TR, TErr>(f(Value)) : Result.Err<TR, TErr>(Error);

    public Result<TR, TErr> Bind<TR>(Func<TOk, Result<TR, TErr>> f) =>
        IsOk ? f(Value) : Result.Err<TR, TErr>(Error);
}

/// <summary>Non-generic factory helpers for <see cref="Result{TOk, TErr}"/>.</summary>
public static class Result
{
    public static Result<TOk, TErr> Ok<TOk, TErr>(TOk value) =>
        new(isOk: true, value: value, error: default!);

    public static Result<TOk, TErr> Err<TOk, TErr>(TErr error) =>
        new(isOk: false, value: default!, error: error);
}

public readonly record struct Unit
{
    public static Unit Value => default;
}
