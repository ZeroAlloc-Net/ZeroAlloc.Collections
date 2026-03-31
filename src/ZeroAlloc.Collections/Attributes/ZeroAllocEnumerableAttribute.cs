namespace ZeroAlloc.Collections;

/// <summary>
/// Generates a zero-allocation ref struct enumerator for the decorated type.
/// </summary>
/// <remarks>
/// The generator locates the backing array and element count by inspecting the type's fields.
/// If the type has multiple array fields or multiple <c>int</c> fields, specify
/// <paramref name="arrayFieldName"/> and <paramref name="countFieldName"/> explicitly to avoid ambiguity.
/// </remarks>
/// <param name="arrayFieldName">
/// The name of the field that holds the backing array (e.g. <c>"_items"</c>).
/// When <c>null</c>, the generator picks the first non-static array field it finds
/// and emits a diagnostic if multiple candidates exist.
/// </param>
/// <param name="countFieldName">
/// The name of the field that holds the element count (e.g. <c>"_count"</c>).
/// When <c>null</c>, the generator picks the first non-static <c>int</c> field it finds
/// and emits a diagnostic if multiple candidates exist.
/// </param>
[AttributeUsage(AttributeTargets.Struct | AttributeTargets.Class, Inherited = false)]
public sealed class ZeroAllocEnumerableAttribute : Attribute
{
    /// <summary>
    /// Marks the type for zero-allocation enumerator generation using field auto-discovery.
    /// </summary>
    public ZeroAllocEnumerableAttribute() { }

    /// <summary>
    /// Marks the type for zero-allocation enumerator generation with explicit field names.
    /// </summary>
    /// <param name="arrayFieldName">The name of the backing array field.</param>
    /// <param name="countFieldName">The name of the count field.</param>
    public ZeroAllocEnumerableAttribute(string arrayFieldName, string countFieldName)
    {
        ArrayFieldName = arrayFieldName;
        CountFieldName = countFieldName;
    }

    /// <summary>Gets the explicit backing array field name, or <c>null</c> for auto-discovery.</summary>
    public string? ArrayFieldName { get; }

    /// <summary>Gets the explicit count field name, or <c>null</c> for auto-discovery.</summary>
    public string? CountFieldName { get; }
}
