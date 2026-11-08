namespace SnapBar.Core;

/// <summary>A product-owned validation failure, localized only at the presentation boundary.</summary>
public sealed class ProductValidationException(string code, params object[] arguments) : ArgumentException(code)
{
    public string Code { get; } = code;
    public IReadOnlyList<object> Arguments { get; } = Array.AsReadOnly((object[])arguments.Clone());
}
