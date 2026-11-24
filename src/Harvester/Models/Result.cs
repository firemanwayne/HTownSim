namespace HoustonIncidents.Harvester.Models;

/// <summary>
/// Framework-agnostic success-or-error result.
/// </summary>
/// <typeparam name="T">The success value type.</typeparam>
internal sealed class Result<T>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="Result{T}"/> class.
    /// </summary>
    /// <param name="value">The success value, or default on failure.</param>
    /// <param name="error">The error message, or null on success.</param>
    private Result(T? value, string? error)
    {
        Value = value;

        Error = error;
    }

    /// <summary>Gets a value indicating whether the operation succeeded.</summary>
    [MemberNotNullWhen(true, nameof(Value))]
    [MemberNotNullWhen(false, nameof(Error))]
    public bool IsSuccess => Error is null;

    /// <summary>Gets the success value; non-null when <see cref="IsSuccess"/> is true.</summary>
    public T? Value { get; }

    /// <summary>Gets the error message; non-null when <see cref="IsSuccess"/> is false.</summary>
    public string? Error { get; }

    /// <summary>Creates a successful result.</summary>
    /// <param name="value">The success value.</param>
    /// <returns>A successful result.</returns>
    public static Result<T> Ok(T value) => new(value, null);

    /// <summary>Creates a failed result.</summary>
    /// <param name="error">The error message.</param>
    /// <returns>A failed result.</returns>
    public static Result<T> Fail(string error) => new(default, error);
}
