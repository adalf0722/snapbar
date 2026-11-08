namespace SnapBar.Core;

/// <summary>Structured product errors on exceptions whose existing type is part of a caller's contract.</summary>
public sealed record ProductError(string Code, IReadOnlyList<object> Arguments)
{
    private const string DataKey = "SnapBar.ProductError";

    public static ProductError? FromException(Exception error) => error is ProductValidationException validation
        ? new(validation.Code, validation.Arguments) : error.Data[DataKey] as ProductError;

    public static InvalidDataException InvalidData(string code, params object[] arguments) =>
        Tag(new InvalidDataException(code), code, arguments);

    public static InvalidOperationException InvalidOperation(string code, params object[] arguments) =>
        Tag(new InvalidOperationException(code), code, arguments);

    private static T Tag<T>(T exception, string code, object[] arguments) where T : Exception
    {
        exception.Data[DataKey] = new ProductError(code, Array.AsReadOnly((object[])arguments.Clone()));
        return exception;
    }
}
